using System.Collections;
using System.Collections.Immutable;

namespace EmberTrace.Generator.Generator;

internal readonly struct EquatableArray<T> : IEquatable<EquatableArray<T>>, IEnumerable<T>
    where T : IEquatable<T>
{
    private readonly ImmutableArray<T> _values;

    internal EquatableArray(ImmutableArray<T> values)
    {
        _values = values;
    }

    internal ImmutableArray<T> Values => _values.IsDefault ? ImmutableArray<T>.Empty : _values;

    public bool Equals(EquatableArray<T> other)
    {
        var left = Values;
        var right = other.Values;
        if (left.Length != right.Length)
            return false;

        for (var i = 0; i < left.Length; i++)
            if (!left[i].Equals(right[i]))
                return false;

        return true;
    }

    public override bool Equals(object? obj)
    {
        return obj is EquatableArray<T> other && Equals(other);
    }

    public override int GetHashCode()
    {
        var hash = 17;
        foreach (var value in Values)
            hash = unchecked(hash * 31 + value.GetHashCode());

        return hash;
    }

    public IEnumerator<T> GetEnumerator()
    {
        return ((IEnumerable<T>)Values).GetEnumerator();
    }

    IEnumerator IEnumerable.GetEnumerator()
    {
        return GetEnumerator();
    }
}