namespace EmberTrace.Tests.Export;

[TestClass]
[DoNotParallelize]
public class MarkedCompleteApiTests
{
    [TestMethod]
    public void MarkedComplete_WithOptions_ProducesSliceAndSession()
    {
        var dir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(dir);

        try
        {
            var result = TraceExport.MarkedComplete(
                "revision",
                () => Thread.Sleep(1),
                new MarkedCompleteOptions { OutputPath = Path.Combine(dir, "trace.json") });

            Assert.IsNotNull(result.CapturedSession);
            Assert.AreEqual("revision", result.Name);
            Assert.IsTrue(File.Exists(result.SlicePath));
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [TestMethod]
    public void MarkedComplete_UniqueOption_AppendsCallerLineToName()
    {
        var dir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(dir);

        try
        {
            var result = TraceExport.MarkedComplete(
                "revision",
                static () => { },
                new MarkedCompleteOptions { OutputPath = Path.Combine(dir, "trace.json"), Unique = true });

            StringAssert.StartsWith(result.Name, "revision_L");
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [TestMethod]
    public async Task MarkedCompleteAsync_WithOptions_ProducesSliceAndSession()
    {
        var dir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(dir);

        try
        {
            var result = await TraceExport.MarkedCompleteAsync(
                "revision-async",
                static () => Task.CompletedTask,
                new MarkedCompleteOptions { OutputPath = Path.Combine(dir, "trace.json") });

            Assert.AreEqual("revision-async", result.Name);
            Assert.IsTrue(File.Exists(result.SlicePath));
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }
}
