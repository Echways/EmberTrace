using System.Globalization;
using System.Runtime;
using System.Runtime.InteropServices;
using EmberTrace.Sessions;

namespace EmberTrace.Internal;

internal interface IEnvironmentReader
{
    string? ReadFile(string path);
    IReadOnlyDictionary<string, string> GetEnvironmentVariables();
}

internal sealed class SystemEnvironmentReader : IEnvironmentReader
{
    public static SystemEnvironmentReader Instance { get; } = new();

    public string? ReadFile(string path)
    {
        try
        {
            return File.Exists(path) ? File.ReadAllText(path) : null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    public IReadOnlyDictionary<string, string> GetEnvironmentVariables()
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (System.Collections.DictionaryEntry entry in Environment.GetEnvironmentVariables())
            if (entry.Key is string key && entry.Value is string value)
                result[key] = value;

        return result;
    }
}

internal static class EnvironmentProbe
{
    public const string GovernorPath = "/sys/devices/system/cpu/cpu0/cpufreq/scaling_governor";
    public const string NoTurboPath = "/sys/devices/system/cpu/intel_pstate/no_turbo";
    public const string BoostPath = "/sys/devices/system/cpu/cpufreq/boost";
    public const string CgroupCpuMaxPath = "/sys/fs/cgroup/cpu.max";
    public const string CgroupMemoryMaxPath = "/sys/fs/cgroup/memory.max";
    public const string ProcessStatusPath = "/proc/self/status";

    private static readonly string[] KnobPrefixes = ["DOTNET_", "COMPlus_"];

    public static TraceEnvironment Capture(IEnvironmentReader reader, IReadOnlyDictionary<string, string>? tags)
    {
        var variables = reader.GetEnvironmentVariables();
        var values = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["os"] = RuntimeInformation.OSDescription,
            ["runtime"] = RuntimeInformation.FrameworkDescription,
            ["arch"] = RuntimeInformation.ProcessArchitecture.ToString(),
            ["processors"] = Environment.ProcessorCount.ToString(CultureInfo.InvariantCulture),
            ["gc.server"] = GCSettings.IsServerGC ? "true" : "false",
            ["gc.latency"] = GCSettings.LatencyMode.ToString(),
            ["tiered.pgo"] = Knob(variables, "TieredPGO", "System.Runtime.TieredPGO"),
            ["tiered.compilation"] = Knob(variables, "TieredCompilation", "System.Runtime.TieredCompilation"),
            ["memory.available"] = GC.GetGCMemoryInfo().TotalAvailableMemoryBytes.ToString(CultureInfo.InvariantCulture)
        };

        foreach (var (name, value) in variables)
            if (KnobPrefixes.Any(prefix => name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)))
                values["env." + name] = value;

        Put(values, "cpu.governor", Read(reader, GovernorPath));
        Put(values, "cpu.turbo", Turbo(reader));
        Put(values, "cpu.affinity", Affinity(reader));
        Put(values, "cgroup.cpu.max", Read(reader, CgroupCpuMaxPath));
        Put(values, "cgroup.memory.max", Read(reader, CgroupMemoryMaxPath));

        if (tags is not null)
            foreach (var (key, value) in tags)
                values["tag." + key] = value;

        return new TraceEnvironment(values);
    }

    private static string Knob(IReadOnlyDictionary<string, string> variables, string name, string appContextSwitch)
    {
        foreach (var prefix in KnobPrefixes)
            foreach (var (key, value) in variables)
                if (string.Equals(key, prefix + name, StringComparison.OrdinalIgnoreCase))
                    return value.Trim() == "0" ? "false" : "true";

        if (AppContext.TryGetSwitch(appContextSwitch, out var enabled))
            return enabled ? "true" : "false";

        return "true";
    }

    private static string? Turbo(IEnvironmentReader reader)
    {
        var noTurbo = Read(reader, NoTurboPath);
        if (noTurbo is not null)
            return noTurbo == "0" ? "on" : "off";

        var boost = Read(reader, BoostPath);
        return boost is null ? null : boost == "0" ? "off" : "on";
    }

    private static string? Affinity(IEnvironmentReader reader)
    {
        const string key = "Cpus_allowed_list:";

        var status = reader.ReadFile(ProcessStatusPath);
        if (status is null)
            return null;

        foreach (var line in status.Split('\n'))
            if (line.StartsWith(key, StringComparison.Ordinal))
                return line[key.Length..].Trim();

        return null;
    }

    private static string? Read(IEnvironmentReader reader, string path)
    {
        var text = reader.ReadFile(path)?.Trim();
        return string.IsNullOrEmpty(text) ? null : text;
    }

    private static void Put(Dictionary<string, string> values, string key, string? value)
    {
        if (value is not null)
            values[key] = value;
    }
}
