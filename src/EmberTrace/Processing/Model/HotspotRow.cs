namespace EmberTrace.Processing.Model;

public sealed class HotspotRow
{
    public required int Id { get; init; }
    public required long Count { get; init; }
    public required double InclusiveMs { get; init; }
    public required double ExclusiveMs { get; init; }
}
