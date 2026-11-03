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
        var tagArm = tags is not null && tags.TryGetValue(ArmTag, out var armValue) ? armValue : string.Empty;
        var tagRound = tags is not null
                       && tags.TryGetValue(RoundTag, out var roundValue)
                       && int.TryParse(roundValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : 0;

        return new TraceRun(
            label,
            arm ?? tagArm,
            round ?? tagRound,
            session.Analyze(),
            session.Environment,
            session.WasOverflow,
            session.DroppedEvents,
            session.Metadata);
    }
}
