# Устройство docs

[English](STRUCTURE.md) · [Русский](STRUCTURE.ru.md) · [Документация](README.ru.md)

Как организованы страницы и куда добавлять новую.

## Структура

```
docs/
  README.md
  STRUCTURE.md
  assets/
  guides/
    getting-started/
    usage/
    auto-instrumentation/
    export/
    analysis/
    format/
    flight-recorder/
    runtime-counters/
    hosting/
    testing/
  concepts/
    flows/
  reference/
    api/
      index.md
      tracer.md
      scope-reader.md
    source-generator/
    session-options/
    opentelemetry/
    roslyn-analyzers/
  troubleshooting/
```

`README.md` — оглавление. Страница лежит в `README.md` своего каталога; у каталога с несколькими страницами есть
собственный `index.md`. У каждой страницы есть русская пара с суффиксом `.ru.md`, и они ссылаются друг на друга в первой
строке.

## Разделы

| Каталог       | Что в нём                                                                                                                                  |
| -------------------- | ------------------------------------------------------------------------------------------------------------------------------------------------- |
| `guides/`          | Пошаговые сценарии: сделай X.                                                                                              |
| `concepts/`        | Ментальная модель и инварианты: как это устроено внутри.                                           |
| `reference/`       | Точные контракты: API, настройки, генератор, диагностики.                                             |
| `troubleshooting/` | Симптом, причина, решение.                                                                                                   |
| `assets/`          | Изображения для страниц; перегенерируются скриптом `samples/EmberTrace.DocScreenshots/capture.py`. |
