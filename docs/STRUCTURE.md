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
  concepts/
    index.md
    flows/
  reference/
    index.md
    api/
      index.md
      tracer.md
    source-generator/
  troubleshooting/
    index.md
    README.md
```

## Правила именования

- **guides/** — пошаговые сценарии («сделай X»)
- **concepts/** — ментальная модель и invariants («как оно работает внутри»)
- **reference/** — точная справка/контракты (API, конфиги, генераторы, форматы)
- **troubleshooting/** — симптомы → причины → фиксы
- **assets/** — изображения/диаграммы, которые референсятся из docs


## Что можно сделать дальше (если захочешь ещё “строже”)

- Перейти с `README.md` внутри разделов на `index.md` (как в Docusaurus/MkDocs),
  и оставить `README.md` только как redirect.
- Добавить страницы для остальных публичных типов (например `TraceSession`, `SessionOptions`, exporters/reporters), если захочешь 100% coverage.
