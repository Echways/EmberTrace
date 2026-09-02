using System.Buffers;
using System.Text;

namespace EmberTrace.Export;

internal static class TraceFileNaming
{
    private const int StackCharLimit = 256;
    private const int MaxFileNameBytes = 255;
    private const int MaxUtf8BytesPerChar = 3;

    private static readonly SearchValues<char> InvalidFileNameChars =
        SearchValues.Create(Path.GetInvalidFileNameChars());

    public static void EnsureDirectory(string path)
    {
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);
    }

    public static string MakeNameFromCaller(string? caller, string? tag)
    {
        var baseName = string.IsNullOrWhiteSpace(caller) ? "Marked" : caller;
        if (string.IsNullOrWhiteSpace(tag))
            return baseName;

        return $"{baseName}_{SanitizeTag(tag)}";
    }

    public static string SanitizeTag(string tag)
    {
        return MapChars(tag, static c => char.IsLetterOrDigit(c) || c == '-' || c == '_' ? c : '_');
    }

    public static string DefaultTracePath(string name)
    {
        var suffix = $"_{DateTime.UtcNow:yyyyMMdd_HHmmss}.json";
        var safe = TruncateUtf8(SafeFileName(name), MaxFileNameBytes - suffix.Length);
        return Path.Combine("traces", safe + suffix);
    }

    public static string SafeFileName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return "trace";

        return MapChars(name, static c => InvalidFileNameChars.Contains(c) || c == ' ' ? '_' : c);
    }

    public static string MapChars(string value, Func<char, char> map)
    {
        char[]? rented = null;
        var buffer = value.Length <= StackCharLimit
            ? stackalloc char[StackCharLimit]
            : rented = ArrayPool<char>.Shared.Rent(value.Length);

        try
        {
            for (var i = 0; i < value.Length; i++)
                buffer[i] = map(value[i]);

            return new string(buffer[..value.Length]);
        }
        finally
        {
            if (rented is not null)
                ArrayPool<char>.Shared.Return(rented);
        }
    }

    public static string TruncateUtf8(string value, int maxBytes)
    {
        if (maxBytes <= 0)
            return string.Empty;

        if (value.Length <= maxBytes / MaxUtf8BytesPerChar)
            return value;

        Span<byte> bytes = stackalloc byte[maxBytes];
        Encoding.UTF8.GetEncoder().Convert(value, bytes, true, out var charsUsed, out _, out _);
        return charsUsed >= value.Length ? value : value[..charsUsed];
    }
}
