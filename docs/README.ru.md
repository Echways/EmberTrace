# Документация EmberTrace

[English](README.md) · [Русский](README.ru.md) · [Страница проекта](../README.ru.md)

EmberTrace — трассировщик и профилировщик для .NET внутри процесса, с горячим путём без аллокаций. Эти страницы описывают каждую его часть.

## Начало

| Страница | Что внутри |
| -------- | ---------- |
| [Быстрый старт](guides/getting-started/README.ru.md) | Установка, идентификаторы, первый scope, отчёт и файл трассы. |
| [Использование и API](guides/usage/README.ru.md) | Сессии, scope, идентификаторы, настройки, измерение коротких операций. |
| [Автоинструментация](guides/auto-instrumentation/README.ru.md) | `[Trace]` на методах и классах: обёртки, id, DI-декоратор. |

## Запись

| Страница | Что внутри |
| -------- | ---------- |
| [Flow](concepts/flows/README.ru.md) | Связи между потоками и `async/await`. |
| [Runtime-счётчики](guides/runtime-counters/README.ru.md) | GC, память, пул потоков и исключения на одной временной шкале. |
| [Flight recorder](guides/flight-recorder/README.ru.md) | Ограниченное окно недавнего прошлого, выгружаемое без остановки сессии. |

## Выгрузка и анализ

| Страница | Что внутри |
| -------- | ---------- |
| [Экспорт](guides/export/README.ru.md) | Файлы Chrome Trace и Perfetto, захват окон. |
| [Анализ и отчёты](guides/analysis/README.ru.md) | Агрегаты, перцентили, текстовые отчёты, сравнение прогонов, flame graph. |
| [Формат сессии](guides/format/README.ru.md) | Бинарный файл `.ember`: сохранение, потоковое чтение, совместимость. |
| [Тесты производительности](guides/testing/README.ru.md) | Бюджеты и гейты регрессий для CI. |

## Интеграции

| Страница | Что внутри |
| -------- | ---------- |
| [Хостинг ASP.NET Core](guides/hosting/README.ru.md) | Scope запросов, endpoint дампа, захват медленных запросов. |
| [OpenTelemetry](reference/opentelemetry/README.ru.md) | Сессии в виде спанов `Activity`. |
| [Roslyn-анализаторы](reference/roslyn-analyzers/README.ru.md) | Диагностики `ETA` и code fix. |

## Справочник

| Страница | Что внутри |
| -------- | ---------- |
| [API](reference/api/index.ru.md) | `Tracer` и `ScopeReader`. |
| [Генератор и метаданные](reference/source-generator/README.ru.md) | `TraceId`, `TraceName`, сгенерированные провайдеры, диагностики `ETG`. |
| [Настройки сессии](reference/session-options/README.ru.md) | Все члены `SessionOptions`. |

## Дальше

| Страница | Что внутри |
| -------- | ---------- |
| [Устранение неполадок](troubleshooting/README.ru.md) | Симптомы, причины и решения. |
| [Устройство docs](STRUCTURE.ru.md) | Как организованы эти страницы. |
