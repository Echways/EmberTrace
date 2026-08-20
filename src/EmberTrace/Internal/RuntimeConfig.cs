using System;

namespace EmberTrace.Internal;

internal static class RuntimeConfig
{
    public static bool GetBool(string name, bool defaultValue) =>
        AppContext.TryGetSwitch(name, out var value) ? value : defaultValue;

    public static T GetEnum<T>(string name, T defaultValue) where T : struct, Enum =>
        AppContext.GetData(name) is string raw && Enum.TryParse<T>(raw, ignoreCase: true, out var value)
            ? value
            : defaultValue;
}
