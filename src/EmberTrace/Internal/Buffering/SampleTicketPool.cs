namespace EmberTrace.Internal.Buffering;

internal readonly struct SampleSlot(int index, int everyN)
{
    public readonly int Index = index;
    public readonly int EveryN = everyN;
}

internal sealed class SampleTicketPool
{
    public const int BlockSize = 127;
    public const int GlobalSlot = 0;
    private readonly long[] _issued;

    private readonly Dictionary<int, SampleSlot> _slotsById;

    public SampleTicketPool(IReadOnlyDictionary<int, int>? everyNById)
    {
        _slotsById = new Dictionary<int, SampleSlot>(everyNById?.Count ?? 0);

        if (everyNById is not null)
            foreach (var pair in everyNById.Where(pair => pair.Value > 1))
                _slotsById[pair.Key] = new SampleSlot(_slotsById.Count + 1, pair.Value);

        _issued = new long[_slotsById.Count + 1];
    }

    public int SlotCount => _issued.Length;

    public bool TryGetSlot(int id, out SampleSlot slot)
    {
        return _slotsById.TryGetValue(id, out slot);
    }

    public long RentBlock(int slot)
    {
        return Interlocked.Add(ref _issued[slot], BlockSize) - BlockSize;
    }
}