using System.Reflection;

namespace EmberTrace.Extensions.Hosting.Tests.Recording;

[TestClass]
[DoNotParallelize]
public sealed class ScaffoldingTests
{
    [TestMethod]
    public void HostingAssemblyIsReferenced()
    {
        var assembly = Assembly.Load("EmberTrace.Extensions.Hosting");

        Assert.AreEqual("EmberTrace.Extensions.Hosting", assembly.GetName().Name);
    }

    [TestMethod]
    public void AspNetCoreSharedFrameworkIsAvailable()
    {
        var context = new Microsoft.AspNetCore.Http.DefaultHttpContext();

        Assert.IsNotNull(context.Request);
    }
}
