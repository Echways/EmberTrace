namespace EmberTrace.Internal;

internal static class TraceIds
{
    public static int Stable(string name)
    {
        if (name is null) throw new ArgumentNullException(nameof(name));

        unchecked
        {
            const uint offset = 2166136261;
            const uint prime = 16777619;

            var h = offset;
            foreach (var c in name)
            {
                h ^= c;
                h *= prime;
            }

            h &= 0x7fffffff;
            return (int)(h == 0 ? 1 : h);
        }
    }

    public static int Category(string? category)
    {
        return string.IsNullOrWhiteSpace(category) ? 0 : Stable(category!);
    }
}