Русская версия: [./STRUCTURE.ru.md](./STRUCTURE.ru.md)

# Docs layout

## Current structure

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

## Naming rules

- **guides/** - step-by-step scenarios ("do X")
- **concepts/** - mental model and invariants ("how it works inside")
- **reference/** - precise docs/contracts (API, configs, generators, formats)
- **troubleshooting/** - symptoms -> causes -> fixes
- **assets/** - images/diagrams referenced from docs

## What you can do next (if you want it even "stricter")

- Switch from `README.md` inside sections to `index.md` (as in Docusaurus/MkDocs),
  and keep `README.md` only as redirect.
- Add pages for remaining public types (for example `TraceSession`, `SessionOptions`, exporters/reporters) if you want 100% coverage.
