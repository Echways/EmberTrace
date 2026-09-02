Русская версия: [./STRUCTURE.ru.md](./STRUCTURE.ru.md)

# Docs layout

## Current structure

```
docs/
  index.md
  README.md
  STRUCTURE.md              # this file
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

A section directory holds its page in `README.md`; `index.md` is the table of contents of a
directory that has children. Every page has a Russian sibling with the `.ru.md` suffix, and both
versions link to each other on the first line.

## Naming rules

- **guides/** - step-by-step scenarios ("do X")
- **concepts/** - mental model and invariants ("how it works inside")
- **reference/** - precise docs/contracts (API, configs, generators, formats)
- **troubleshooting/** - symptoms -> causes -> fixes
- **assets/** - images/diagrams referenced from docs
