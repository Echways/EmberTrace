using EmberTrace.Metadata;
using EmberTrace.Sessions;

namespace EmberTrace.Tests.Tracing;

[TestClass]
[DoNotParallelize]
public class TracerNameRegistryTests
{
    private Action<TracerIdCollision>? _handler;
    private TracerIdCollisionMode _mode;

    [TestInitialize]
    public void Save()
    {
        _mode = Tracer.IdCollisionMode;
        _handler = Tracer.OnIdCollision;
    }

    [TestCleanup]
    public void Restore()
    {
        Tracer.IdCollisionMode = _mode;
        Tracer.OnIdCollision = _handler;
    }

    [TestMethod]
    public void IdCollisionMode_DefaultsToWarn_InEveryBuildConfiguration()
    {
        Assert.AreEqual(TracerIdCollisionMode.Warn, Tracer.IdCollisionMode);
    }

    [TestMethod]
    public void Id_CollidingNames_Warn_InvokesHandler()
    {
        var collisions = new List<TracerIdCollision>();
        Tracer.IdCollisionMode = TracerIdCollisionMode.Warn;
        Tracer.OnIdCollision = collisions.Add;

        var first = Tracer.Id("ETC_j2lh");
        var second = Tracer.Id("ETC_vCxa");

        Assert.AreEqual(first, second);
        Assert.HasCount(1, collisions);
        Assert.AreEqual("ETC_j2lh", collisions[0].ExistingName);
        Assert.AreEqual("ETC_vCxa", collisions[0].NewName);
    }

    [TestMethod]
    public void Id_CollidingNames_Throw_Throws()
    {
        Tracer.IdCollisionMode = TracerIdCollisionMode.Throw;
        Tracer.OnIdCollision = null;

        Tracer.Id("ETC_j2lk");

        Assert.ThrowsExactly<InvalidOperationException>(() => Tracer.Id("ETC_vCxb"));
    }

    [TestMethod]
    public void Id_CollidingNames_Ignore_StaysSilent()
    {
        var reported = 0;
        Tracer.IdCollisionMode = TracerIdCollisionMode.Ignore;
        Tracer.OnIdCollision = _ => reported++;

        var first = Tracer.Id("ETC_j2lj");
        var second = Tracer.Id("ETC_vCxc");

        Assert.AreEqual(first, second);
        Assert.AreEqual(0, reported);
    }

    [TestMethod]
    public void Id_SameNameTwice_IsNotACollision()
    {
        var reported = 0;
        Tracer.IdCollisionMode = TracerIdCollisionMode.Throw;
        Tracer.OnIdCollision = _ => reported++;

        Assert.AreEqual(Tracer.Id("ETC_Stable"), Tracer.Id("ETC_Stable"));
        Assert.AreEqual(0, reported);
    }

    [TestMethod]
    public void EnableRuntimeMetadata_DefaultsToFalse_InEveryBuildConfiguration()
    {
        Assert.IsFalse(new SessionOptions().EnableRuntimeMetadata);
    }

    [TestMethod]
    public void SessionMetadata_WithRuntimeMetadata_ResolvesNamesRegisteredByTracerId()
    {
        var id = Tracer.Id("ETC_RuntimeNamed");

        using var ts = new TracingSession();
        ts.Start(new SessionOptions { EnableRuntimeMetadata = true, ChunkCapacity = 256 });
        var session = ts.Stop();

        Assert.IsTrue(session.Metadata.TryGet(id, out var meta));
        Assert.AreEqual("ETC_RuntimeNamed", meta.Name);
    }

    [TestMethod]
    public void SessionMetadata_WithoutRuntimeMetadata_DoesNotResolveRuntimeNames()
    {
        var id = Tracer.Id("ETC_OptedOut");

        using var ts = new TracingSession();
        ts.Start(new SessionOptions { ChunkCapacity = 256 });
        var session = ts.Stop();

        Assert.IsFalse(session.Metadata.TryGet(id, out _));
    }

    [TestMethod]
    public void Start_WithRuntimeMetadata_DoesNotMutateGlobalProviders()
    {
        var id = Tracer.Id("ETC_NotGlobal");

        using (var enabled = new TracingSession())
        {
            enabled.Start(new SessionOptions { EnableRuntimeMetadata = true, ChunkCapacity = 256 });
            enabled.Stop();
        }

        Assert.IsFalse(TraceMetadata.CreateDefault().TryGet(id, out _));

        using var plain = new TracingSession();
        plain.Start(new SessionOptions { ChunkCapacity = 256 });
        Assert.IsFalse(plain.Stop().Metadata.TryGet(id, out _));
    }

    [TestMethod]
    public void MaxTrackedNames_Zero_MeansUnlimited()
    {
        var previous = Tracer.MaxTrackedNames;
        try
        {
            Tracer.MaxTrackedNames = 0;
            var id = Tracer.Id("ETC_Unbounded");

            using var ts = new TracingSession();
            ts.Start(new SessionOptions { EnableRuntimeMetadata = true, ChunkCapacity = 256 });

            Assert.IsTrue(ts.Stop().Metadata.TryGet(id, out _));
        }
        finally
        {
            Tracer.MaxTrackedNames = previous;
        }
    }
}