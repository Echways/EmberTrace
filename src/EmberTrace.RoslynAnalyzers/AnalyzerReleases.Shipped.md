; Shipped analyzer releases
; https://github.com/dotnet/roslyn-analyzers/blob/main/src/Microsoft.CodeAnalysis.Analyzers/ReleaseTrackingAnalyzers.Help.md

## Release 0.2

### New Rules

Rule ID | Category | Severity | Notes
--------|----------|----------|-------
ETA001 | EmberTrace.Usage | Warning | Scope should be wrapped in using
ETA002 | EmberTrace.Usage | Warning | AsyncScope should be wrapped in await using
ETA003 | EmberTrace.Usage | Warning | FlowHandle should call End or TryEnd

### Removed Rules

Rule ID | Category | Severity | Notes
--------|----------|----------|-------

### Changed Rules

Rule ID | New Category | New Severity | Old Category | Old Severity | Notes
--------|--------------|--------------|--------------|--------------|-------
