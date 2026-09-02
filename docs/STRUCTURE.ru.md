English version: [./STRUCTURE.md](./STRUCTURE.md)

# Docs layout

## Текущая структура

```
docs/
  index.md
  README.md
  STRUCTURE.md              # этот файл
  assets/
  guides/
    index.md
    getting-started/
    usage/
    export/
    analysis/
    format/
    flight-recorder/
    auto-instrumentation/
    runtime-counters/
    hosting/
    testing/
  concepts/
    index.md
    flows/
  reference/
    index.md
    api/
      index.md
      tracer.md
      scope-reader.md
    source-generator/
    session-options/
    opentelemetry/
    roslyn-analyzers/
  troubleshooting/
    index.md
    README.md
```

Страница раздела лежит в `README.md`; `index.md` — оглавление каталога, у которого есть вложенные
страницы. У каждой страницы есть русская пара с суффиксом `.ru.md`, обе версии ссылаются друг на
друга первой строкой.

## Правила именования

- **guides/** — пошаговые сценарии («сделай X»)
- **concepts/** — ментальная модель и инварианты («как оно работает внутри»)
- **reference/** — точная справка/контракты (API, конфиги, генераторы, форматы)
- **troubleshooting/** — симптомы → причины → фиксы
- **assets/** — изображения и диаграммы, на которые ссылаются страницы docs
