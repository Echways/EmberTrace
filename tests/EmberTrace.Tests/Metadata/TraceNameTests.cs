using EmberTrace.Metadata;
using EmberTrace.Sessions;

namespace EmberTrace.Tests.Metadata;

[TestClass]
[DoNotParallelize]
public class TraceNameTests
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
    public void TraceName_Id_EqualsTracerId()
    {
        var name = new TraceName("TraceNameTests.Equal");

        Assert.AreEqual(Tracer.Id("TraceNameTests.Equal"), name.Id);
        Assert.AreEqual("TraceNameTests.Equal", name.Name);
        Assert.AreEqual("TraceNameTests.Equal", name.ToString());
    }

    [TestMethod]
    public void TraceName_RegistersMetadataOnce_FirstCategoryWins()
    {
        var first = new TraceName("TraceNameTests.Once", "IO");
        var second = new TraceName("TraceNameTests.Once", "Cpu");

        Assert.AreEqual(first.Id, second.Id);
        Assert.IsTrue(Tracer.Names.TryGet(first.Id, out var meta));
        Assert.AreEqual("TraceNameTests.Once", meta.Name);
        Assert.AreEqual("IO", meta.Category);
    }

    [TestMethod]
    public void TraceName_WithoutCategory_UsesTheDefaultCategory()
    {
        var name = new TraceName("TraceNameTests.DefaultCategory");

        Assert.IsTrue(Tracer.Names.TryGet(name.Id, out var meta));
        Assert.AreEqual("Default", meta.Category);
    }

    [TestMethod]
    public void TraceName_NullName_Throws()
    {
        Assert.ThrowsExactly<ArgumentNullException>(() => new TraceName(null!));
    }

    [TestMethod]
    public void Scope_TraceName_RecordsSameIdAsScopeInt()
    {
        var name = new TraceName("TraceNameTests.Recorded");
        Tracer.Start();
        using (Tracer.Scope(name))
        {
        }

        using (Tracer.Scope(name.Id))
        {
        }

        var session = Tracer.Stop();

        var events = session.SortedEvents();
        Assert.HasCount(4, events);
        Assert.IsTrue(events.All(e => e.Id == name.Id));
        Assert.AreEqual(2, events.Count(e => e.Kind == TraceEventKind.Begin));
    }

    [TestMethod]
    public void Scope_TraceName_WhileStopped_IsInert()
    {
        var name = new TraceName("TraceNameTests.Stopped");

        using (Tracer.Scope(name))
        {
        }

        Assert.IsFalse(Tracer.IsRunning);
    }

    [TestMethod]
    public void Scope_DefaultTraceName_RecordsIdZero()
    {
        Tracer.Start();
        using (Tracer.Scope(default(TraceName)))
        {
        }

        var session = Tracer.Stop();

        Assert.IsTrue(session.SortedEvents().All(e => e.Id == 0));
        Assert.AreEqual(string.Empty, default(TraceName).Name);
    }

    [TestMethod]
    public void TraceName_Collision_UsesIdCollisionMode_Warn()
    {
        var collisions = new List<TracerIdCollision>();
        Tracer.IdCollisionMode = TracerIdCollisionMode.Warn;
        Tracer.OnIdCollision = collisions.Add;

        var first = new TraceName("TN_lpfs");
        var second = new TraceName("TN_4vja");

        Assert.AreEqual(first.Id, second.Id);
        Assert.HasCount(1, collisions);
        Assert.AreEqual("TN_lpfs", collisions[0].ExistingName);
        Assert.AreEqual("TN_4vja", collisions[0].NewName);
    }

    [TestMethod]
    public void TraceName_Collision_UsesIdCollisionMode_Throw()
    {
        Tracer.IdCollisionMode = TracerIdCollisionMode.Throw;
        Tracer.OnIdCollision = null;

        _ = new TraceName("TN_lpfp");

        Assert.ThrowsExactly<InvalidOperationException>(() => new TraceName("TN_4vjb"));
    }

    [TestMethod]
    public void TraceName_Collision_UsesIdCollisionMode_Ignore()
    {
        var reported = 0;
        Tracer.IdCollisionMode = TracerIdCollisionMode.Ignore;
        Tracer.OnIdCollision = _ => reported++;

        var first = new TraceName("TN_lpfq");
        var second = new TraceName("TN_4vjc");

        Assert.AreEqual(first.Id, second.Id);
        Assert.AreEqual(0, reported);
    }
}