using System.Reflection;
using EmberTrace.Generator.Generator;

namespace EmberTrace.Generator.Tests;

[TestClass]
public class StableIdParityTests
{
    [TestMethod]
    [DataRow("OrderService.GetAsync")]
    [DataRow("A")]
    [DataRow("")]
    [DataRow("Очень.Длинное.Имя")]
    [DataRow("OrderService.GetAsync(int)")]
    public void GeneratorStableId_MatchesRuntimeStableId(string name)
    {
        Assert.AreEqual(RuntimeStableId(name), GeneratorStable(name),
            $"The generator and the runtime must agree on the id of '{name}'");
    }

    private static int GeneratorStable(string name)
    {
        var type = typeof(TraceMetadataGenerator).Assembly.GetType("EmberTrace.Internal.TraceIds", true)!;
        return (int)type.GetMethod("Stable", BindingFlags.Public | BindingFlags.Static)!
            .Invoke(null, [name])!;
    }

    internal static int RuntimeStableId(string name)
    {
        var type = typeof(Tracer).Assembly.GetType("EmberTrace.Internal.TraceIds", true)!;
        return (int)type.GetMethod("Stable", BindingFlags.Public | BindingFlags.Static)!
            .Invoke(null, [name])!;
    }
}
