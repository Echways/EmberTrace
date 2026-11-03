using EmberTrace.Internal;
using EmberTrace.Sessions;
using EmberTrace.Tracing;

namespace EmberTrace.Tests.Sessions;

[TestClass]
public class EnvironmentCompatibilityTests
{
    [TestMethod]
    public void Compare_GovernorDiffers_IsBlocking()
    {
        var mismatches = EnvironmentCompatibility.Compare(
            Env(("cpu.governor", "performance")), Env(("cpu.governor", "powersave")));

        var mismatch = mismatches.Single();
        Assert.AreEqual("cpu.governor", mismatch.Key);
        Assert.AreEqual("performance", mismatch.A);
        Assert.AreEqual("powersave", mismatch.B);
        Assert.AreEqual(MismatchSeverity.Blocking, mismatch.Severity);
    }

    [TestMethod]
    [DataRow("cpu.turbo")]
    [DataRow("runtime")]
    [DataRow("tiered.pgo")]
    [DataRow("tiered.compilation")]
    [DataRow("gc.server")]
    [DataRow("gc.latency")]
    [DataRow("cpu.affinity")]
    [DataRow("cgroup.cpu.max")]
    [DataRow("cgroup.memory.max")]
    public void Compare_BlockingKeys_AreBlocking(string key)
    {
        var mismatch = EnvironmentCompatibility.Compare(Env((key, "1")), Env((key, "2"))).Single();

        Assert.AreEqual(MismatchSeverity.Blocking, mismatch.Severity);
    }

    [TestMethod]
    [DataRow("os")]
    [DataRow("arch")]
    [DataRow("processors")]
    [DataRow("memory.available")]
    [DataRow("env.DOTNET_gcServer")]
    public void Compare_OtherKeys_AreWarnings(string key)
    {
        var mismatch = EnvironmentCompatibility.Compare(Env((key, "1")), Env((key, "2"))).Single();

        Assert.AreEqual(MismatchSeverity.Warning, mismatch.Severity);
    }

    [TestMethod]
    public void Compare_KeyMissingOnOneSide_ReportsNullValue()
    {
        var mismatch = EnvironmentCompatibility.Compare(Env(("cpu.governor", "performance")), Env()).Single();

        Assert.AreEqual("performance", mismatch.A);
        Assert.IsNull(mismatch.B);
    }

    [TestMethod]
    public void Compare_OneSideNull_IsWarning()
    {
        var mismatch = EnvironmentCompatibility.Compare(null, Env(("os", "Linux"))).Single();

        Assert.AreEqual(MismatchSeverity.Warning, mismatch.Severity);
    }

    [TestMethod]
    public void Compare_BothNull_IsWarning()
    {
        var mismatch = EnvironmentCompatibility.Compare(null, null).Single();

        Assert.AreEqual(MismatchSeverity.Warning, mismatch.Severity);
    }

    [TestMethod]
    public void Compare_OnlyTagsDiffer_IsEmpty()
    {
        var mismatches = EnvironmentCompatibility.Compare(
            Env(("os", "Linux"), ("tag.arm", "A"), ("tag.round", "1")),
            Env(("os", "Linux"), ("tag.arm", "B")));

        Assert.IsEmpty(mismatches);
    }

    [TestMethod]
    public void Compare_IdenticalEnvironments_IsEmpty()
    {
        Assert.IsEmpty(EnvironmentCompatibility.Compare(Env(("os", "Linux")), Env(("os", "Linux"))));
    }

    [TestMethod]
    public void Probe_GovernorChangedBetweenStartAndStop_ReportedByEnvironmentAtStop()
    {
        var reader = new FakeEnvironmentReader();
        reader.Files[EnvironmentProbe.GovernorPath] = "performance\n";
        var profiler = new Profiler(environmentReader: reader);

        profiler.Start();
        reader.Files[EnvironmentProbe.GovernorPath] = "powersave\n";
        var session = profiler.Stop();

        Assert.AreEqual("performance", session.Environment!.Values["cpu.governor"]);
        Assert.AreEqual("powersave", session.EnvironmentAtStop!.Values["cpu.governor"]);
        var mismatch = EnvironmentCompatibility.Compare(session.Environment, session.EnvironmentAtStop).Single();
        Assert.AreEqual(MismatchSeverity.Blocking, mismatch.Severity);
    }

    [TestMethod]
    public void Probe_NoSysfs_FallsBackToPowerSchemeAndAffinityMask()
    {
        var reader = new FakeEnvironmentReader { PowerScheme = "381b4222-f694-41f0-9685-ff5bb260df2e\n", AffinityMask = "ff" };

        var values = EnvironmentProbe.Capture(reader, null).Values;

        Assert.AreEqual("381b4222-f694-41f0-9685-ff5bb260df2e", values["cpu.governor"]);
        Assert.AreEqual("ff", values["cpu.affinity"]);
    }

    [TestMethod]
    public void Probe_CollectsFixedKeysTagsAndDotnetVariables()
    {
        var reader = new FakeEnvironmentReader();
        reader.Variables["DOTNET_TieredPGO"] = "0";
        reader.Variables["COMPlus_gcServer"] = "1";
        reader.Variables["PATH"] = "/usr/bin";
        reader.Files[EnvironmentProbe.CgroupCpuMaxPath] = "max 100000\n";
        var tags = new Dictionary<string, string> { ["arm"] = "B", ["round"] = "2" };

        var values = EnvironmentProbe.Capture(reader, tags).Values;

        foreach (var key in new[] { "os", "runtime", "arch", "processors", "gc.server", "gc.latency", "tiered.pgo", "tiered.compilation", "memory.available" })
            Assert.IsTrue(values.ContainsKey(key), key);
        Assert.AreEqual("false", values["tiered.pgo"]);
        Assert.AreEqual("1", values["env.COMPlus_gcServer"]);
        Assert.AreEqual("0", values["env.DOTNET_TieredPGO"]);
        Assert.IsFalse(values.ContainsKey("env.PATH"));
        Assert.AreEqual("max 100000", values["cgroup.cpu.max"]);
        Assert.AreEqual("B", values["tag.arm"]);
        Assert.AreEqual("2", values["tag.round"]);
    }

    [TestMethod]
    public void Probe_UnreadableFiles_AreOmittedWithoutThrowing()
    {
        var values = EnvironmentProbe.Capture(new FakeEnvironmentReader(), null).Values;

        Assert.IsFalse(values.ContainsKey("cpu.governor"));
        Assert.IsFalse(values.ContainsKey("cgroup.memory.max"));
        Assert.IsFalse(values.ContainsKey("cpu.turbo"));
    }

    [TestMethod]
    [DataRow("/sys/devices/system/cpu/intel_pstate/no_turbo", "1", "off")]
    [DataRow("/sys/devices/system/cpu/intel_pstate/no_turbo", "0", "on")]
    [DataRow("/sys/devices/system/cpu/cpufreq/boost", "1", "on")]
    [DataRow("/sys/devices/system/cpu/cpufreq/boost", "0", "off")]
    public void Probe_Turbo_IsNormalisedFromEitherSource(string path, string raw, string expected)
    {
        var reader = new FakeEnvironmentReader();
        reader.Files[path] = raw;

        Assert.AreEqual(expected, EnvironmentProbe.Capture(reader, null).Values["cpu.turbo"]);
    }

    [TestMethod]
    public void CaptureEnvironment_Disabled_LeavesBothNull()
    {
        var profiler = new Profiler(environmentReader: new FakeEnvironmentReader());

        profiler.Start(new SessionOptions { CaptureEnvironment = false });
        var session = profiler.Stop();

        Assert.IsNull(session.Environment);
        Assert.IsNull(session.EnvironmentAtStop);
    }

    [TestMethod]
    public void Start_WithTags_PutsThemInBothSnapshots()
    {
        var profiler = new Profiler(environmentReader: new FakeEnvironmentReader());

        profiler.Start(new SessionOptions { Tags = new Dictionary<string, string> { ["arm"] = "A" } });
        var session = profiler.Stop();

        Assert.AreEqual("A", session.Environment!.Values["tag.arm"]);
        Assert.AreEqual("A", session.EnvironmentAtStop!.Values["tag.arm"]);
    }

    [TestMethod]
    public void Start_WhenTheEnvironmentProbeFails_StillStartsAndStopsWithoutEnvironment()
    {
        var profiler = new Profiler(environmentReader: new ThrowingEnvironmentReader());

        profiler.Start();
        var session = profiler.Stop();

        Assert.IsFalse(profiler.IsRunning);
        Assert.IsNull(session.Environment);
        Assert.IsNull(session.EnvironmentAtStop);
    }

    [TestMethod]
    public void Start_WhenTheEnvironmentProbeFails_CanBeStartedAgain()
    {
        var profiler = new Profiler(environmentReader: new ThrowingEnvironmentReader());

        profiler.Start();
        profiler.Stop();
        profiler.Start();

        Assert.IsTrue(profiler.IsRunning);
        profiler.Stop();
    }

    private static TraceEnvironment Env(params (string Key, string Value)[] values)
    {
        return new TraceEnvironment(values.ToDictionary(v => v.Key, v => v.Value));
    }

    private sealed class ThrowingEnvironmentReader : IEnvironmentReader
    {
        public string? ReadFile(string path)
        {
            throw new InvalidOperationException("probe failed");
        }

        public IReadOnlyDictionary<string, string> GetEnvironmentVariables()
        {
            throw new InvalidOperationException("probe failed");
        }

        public string? ReadPowerScheme()
        {
            throw new InvalidOperationException("probe failed");
        }

        public string? ReadAffinityMask()
        {
            throw new InvalidOperationException("probe failed");
        }
    }

    private sealed class FakeEnvironmentReader : IEnvironmentReader
    {
        public Dictionary<string, string> Files { get; } = [];
        public Dictionary<string, string> Variables { get; } = [];
        public string? PowerScheme { get; set; }
        public string? AffinityMask { get; set; }

        public string? ReadPowerScheme()
        {
            return PowerScheme;
        }

        public string? ReadAffinityMask()
        {
            return AffinityMask;
        }

        public string? ReadFile(string path)
        {
            return Files.GetValueOrDefault(path);
        }

        public IReadOnlyDictionary<string, string> GetEnvironmentVariables()
        {
            return Variables;
        }
    }
}
