using System.Globalization;
using EmberTrace.Analysis.Stats;
using EmberTrace.Metadata;
using EmberTrace.Sessions;

namespace EmberTrace.Analysis.Compare;

public sealed record TraceRun(
    string Label,
    string Arm,
    int Round,
    TraceStats Stats,
    TraceEnvironment? Environment,
    bool WasOverflow,
    long DroppedEvents,
    ITraceMetadataProvider? Metadata = null)
{
    private const string ArmTag = "tag.arm";
    private const string RoundTag = "tag.round";

    public static TraceRun FromSession(TraceSession session, string label, string? arm = null, int? round = null)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(label);

        var tags = session.Environment?.Values;
        var resolvedArm = arm ?? Tag(tags, ArmTag) ?? throw MissingTag(nameof(arm), ArmTag);
        var resolvedRound = round ?? ParseRound(Tag(tags, RoundTag)) ?? throw MissingTag(nameof(round), RoundTag);

        return new TraceRun(
            label,
            resolvedArm,
            resolvedRound,
            session.Analyze(),
            session.Environment,
            session.WasOverflow,
            session.DroppedEvents,
            session.Metadata);
    }

    private static string? Tag(IReadOnlyDictionary<string, string>? tags, string key)
    {
        return tags is not null && tags.TryGetValue(key, out var value) ? value : null;
    }

    private static int? ParseRound(string? value)
    {
        return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var round) ? round : null;
    }

    private static ArgumentException MissingTag(string parameter, string tag)
    {
        return new ArgumentException(
            $"The session carries no valid '{tag}' tag; pass '{parameter}' explicitly or tag the session when recording.",
            parameter);
    }
}
