using System.Reflection;

namespace EmberTrace.Tests.Export;

[TestClass]
public class TraceExportSurfaceTests
{
    private const BindingFlags Declared =
        BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;

    [TestMethod]
    public void ExportAssembly_HasNoObsoleteMembers()
    {
        var obsolete = typeof(TraceExport).Assembly.GetExportedTypes()
            .SelectMany(type => type.GetMembers(Declared))
            .Where(member => member.IsDefined(typeof(ObsoleteAttribute), false))
            .Select(member => $"{member.DeclaringType!.Name}.{member.Name}")
            .ToArray();

        Assert.IsEmpty(obsolete);
    }

    [TestMethod]
    public void TraceExport_ExposesOneOverloadPerOperation()
    {
        var methods = typeof(TraceExport).GetMethods(Declared).Select(method => method.Name).Order().ToArray();

        CollectionAssert.AreEqual(
            new[] { "MarkedComplete", "MarkedCompleteAsync", "WriteChromeBeginEnd", "WriteChromeComplete" },
            methods);
    }
}
