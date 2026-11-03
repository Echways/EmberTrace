using EmberTrace.Metadata;
using EmberTrace.Sessions;

namespace EmberTrace.Format.Internal;

internal sealed record StreamPreamble(
    SessionHeader Header,
    IReadOnlyDictionary<int, string> ThreadNames,
    ITraceMetadataProvider Metadata,
    TraceEnvironment? Environment,
    TraceEnvironment? EnvironmentAtStop,
    int EventCount);