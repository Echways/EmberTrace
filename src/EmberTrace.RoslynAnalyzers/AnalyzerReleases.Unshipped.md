; Unshipped analyzer release
; https://github.com/dotnet/roslyn-analyzers/blob/main/src/Microsoft.CodeAnalysis.Analyzers/ReleaseTrackingAnalyzers.Help.md

### New Rules

Rule ID | Category | Severity | Notes
--------|----------|----------|-------
ETA001 | EmberTrace.Usage | Warning | Scope should be wrapped in using
ETA002 | EmberTrace.Usage | Warning | AsyncScope should be wrapped in await using
ETA003 | EmberTrace.Usage | Warning | FlowHandle should call End or TryEnd
