namespace EmberTrace.Internal;

internal static class RuntimeConfig
{
    public static bool GetBool(string name, bool defaultValue)
    {
        return AppContext.TryGetSwitch(name, out var value) ? value : defaultValue;
    }

    public static T GetEnum<T>(string name, T defaultValue) where T : struct, Enum
    {
        return AppContext.GetData(name) is string raw && Enum.TryParse<T>(raw, true, out var value)
            ? value
            : defaultValue;
    }
}