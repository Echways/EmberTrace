using System.Globalization;

namespace EmberTrace.Extensions.Hosting.Recording;

internal static class DumpFileName
{
    public static string Create(string prefix, string? kind, DateTimeOffset now, string extension)
    {
        var stamp = now.UtcDateTime.ToString("yyyyMMdd-HHmmss-fff", CultureInfo.InvariantCulture);

        return kind is null
            ? $"{prefix}-{stamp}{extension}"
            : $"{prefix}-{kind}-{stamp}{extension}";
    }
}
