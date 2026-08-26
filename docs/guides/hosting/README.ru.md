English version: [./README.md](./README.md)

# Интеграция с хостингом (ASP.NET Core)

`EmberTrace.Extensions.Hosting` превращает flight recorder из того, что нужно писать кодом, в то, что
достаточно настроить: сессия живёт столько же, сколько хост, каждый запрос становится скоупом и flow,
а защищённый endpoint отдаёт последние N секунд файлом.

```bash
dotnet add package EmberTrace.Extensions.Hosting
```

Пакет объявляет framework-reference на `Microsoft.AspNetCore.App`, поэтому его место — в веб-приложениях.
Worker service тоже может его использовать, но тогда ему потребуется рантайм ASP.NET Core.

## Подключение

```csharp
var builder = WebApplication.CreateBuilder(args);

builder.Services.AddEmberTrace();

var app = builder.Build();

app.UseRouting();
app.UseEmberTrace();
app.MapEmberTraceDump();

app.Run();
```

`UseEmberTrace()` вызывается **после** `UseRouting()`. Именно роутинг привязывает endpoint к запросу, а
endpoint — источник шаблона маршрута. Если поставить middleware раньше, он продолжит работать, но каждый
запрос будет записан как `HTTP GET`, а не как `GET /orders/{id}`.

`AddEmberTrace()` биндит секцию `EmberTrace`, регистрирует recorder и hosted service и валидирует
конфигурацию на старте. Альтернативные формы:

```csharp
builder.Services.AddEmberTrace(options => options.MaxRetentionWindow = TimeSpan.FromSeconds(60));
builder.Services.AddEmberTrace("Tracing");
builder.Services.AddEmberTrace(builder.Configuration.GetSection("Tracing"));
```

## Конфигурация

```json
{
  "EmberTrace": {
    "Enabled": true,
    "ChunkCapacity": 16384,
    "MaxTotalEvents": 0,
    "MaxTotalChunks": 256,
    "MaxRetentionWindow": "00:00:30",
    "OverflowPolicy": "DropOldest",
    "EnableRuntimeMetadata": true,
    "RuntimeCounters": "Gc, Memory",
    "RuntimeCounterInterval": "00:00:00.050",
    "SampleEveryNGlobal": 0,
    "MaxEventsPerSecond": 0,
    "EnabledCategories": [],
    "DisabledCategories": [],
    "ShutdownDumpDirectory": null,
    "Requests": {
      "Enabled": true,
      "UseRoutePattern": true,
      "RecordFlow": true,
      "Category": "Http",
      "MaxTrackedRoutes": 1024,
      "IgnoredPaths": [ "/health", "/healthz", "/embertrace" ]
    },
    "Dump": {
      "Enabled": false,
      "Path": "/embertrace/dump",
      "ApiKey": null,
      "AuthorizationPolicy": null,
      "RestrictToLoopback": true,
      "AllowAnonymous": false,
      "Window": "00:00:10",
      "MaxWindow": "00:05:00",
      "FileNamePrefix": "embertrace"
    }
  }
}
```

Значения по умолчанию — это значения flight recorder: `DropOldest` с окном удержания в 30 секунд и
ограничением в 256 чанков. `MaxRetentionWindow` работает только с `DropOldest`; любая другая политика не
проходит валидацию на старте и даёт понятное сообщение вместо исключения из `Tracer.Start`.

Категории настраиваются по имени и хешируются в идентификаторы так же, как это делает `Tracer.CategoryId`,
поэтому `"EnabledCategories": [ "Http" ]` записывает запросы и ничего больше.

## Что записывает middleware

Для каждого запроса, не попавшего в `IgnoredPaths`:

- асинхронный скоуп с именем `"{METHOD} {шаблон маршрута}"`, например `GET /orders/{id}`; количество
  идентификаторов ограничено `MaxTrackedRoutes`, и после достижения лимита новые маршруты схлопываются
  в `HTTP {METHOD}`;
- flow, который начинается до пайплайна и заканчивается после него. Если `Activity.Current` — это W3C
  activity, идентификатор flow выводится из её trace id, поэтому один и тот же flow id идентифицирует
  этот запрос во всех сервисах, участвовавших в распределённой трассировке.

Идентификатор flow публикуется в запросе:

```csharp
using EmberTrace.Extensions.Hosting.Http;

app.MapGet("/orders/{id:int}", (int id, HttpContext context) =>
{
    var flowId = context.GetEmberTraceFlowId();
    _ = Task.Run(() =>
    {
        Tracer.FlowStep(Tracer.Id("Orders.Background"), flowId);
    });

    return Results.Ok(id);
});
```

## Endpoint для дампа

`MapEmberTraceDump()` публикует `GET /embertrace/dump`. Он **выключен по умолчанию** и не позволит себя
включить без защиты: если `Dump:Enabled` выставлен, конфигурация обязана содержать `ApiKey`,
`AuthorizationPolicy`, `RestrictToLoopback` (значение по умолчанию) или явный `AllowAnonymous`.

```bash
curl -OJ "http://localhost:5080/embertrace/dump?window=10"
curl -H "X-EmberTrace-Key: $KEY" "http://localhost:5080/embertrace/dump?window=00:00:30&format=chrome" > trace.json
```

- `window` принимает секунды (`10`) или `TimeSpan` (`00:00:30`) и ограничивается значением
  `Dump:MaxWindow`. `window=0` означает «всё, что есть в буфере».
- `format` — это `ember` (по умолчанию, бинарный, читается через `TraceFormat.Read`) или `chrome`
  (Chrome Trace JSON, открывается в Perfetto).
- Ответы содержат заголовки `X-EmberTrace-Events` и `X-EmberTrace-Dropped`.

Коды ответов: `404`, если endpoint выключен или вызывающая сторона не прошла ограничение по loopback —
endpoint не сообщает о своём существовании; `401` при отсутствующем или неверном ключе; `503`, если сессия
не запущена; `400` при неизвестном `format`.

Чтобы поставить его за собственную аутентификацию:

```json
"Dump": { "Enabled": true, "RestrictToLoopback": false, "AuthorizationPolicy": "Diagnostics" }
```

Политика применяется как endpoint convention — ровно так же, как это сделал бы
`RequireAuthorization("Diagnostics")`.

## Жизненный цикл сессии

Hosted service запускает сессию при старте хоста и останавливает при его остановке. Если сессия уже
запущена — тестовым хостом, вторым хостом в том же процессе или кодом приложения, который сам вызвал
`Tracer.Start`, — EmberTrace логирует предупреждение, присоединяется к ней и **не** останавливает её при
выключении. Владение — это то, что решает, кто вызывает `Tracer.Stop`.

Задайте `ShutdownDumpDirectory`, чтобы финальная сессия записывалась туда как
`{prefix}-shutdown-{timestamp}.ember` во время штатной остановки. Ошибка записи логируется, но никогда не
выбрасывается наружу.

## Смотрите также

- [Flight recorder (снапшоты на живой сессии)](../flight-recorder/README.ru.md)
- [Runtime-счётчики](../runtime-counters/README.ru.md)
- [Бинарный формат сессии (.ember)](../format/README.ru.md)
