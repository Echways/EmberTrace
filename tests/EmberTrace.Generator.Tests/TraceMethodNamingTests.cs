using System.Collections.Immutable;
using EmberTrace.Generator.Generator;

namespace EmberTrace.Generator.Tests;

[TestClass]
public class TraceMethodNamingTests
{
    [TestMethod]
    public void SingleMethod_IsNamedTypeDotMethod()
    {
        var resolved = TraceMethodNaming.Resolve([Item("GetAsync", "GetAsync`0(int)", "(int)")]);

        Assert.AreEqual("OrderService.GetAsync", resolved[0].Name);
    }

    [TestMethod]
    public void Overloads_AreDisambiguatedByTheirDisplaySignature()
    {
        var resolved = TraceMethodNaming.Resolve([
            Item("GetAsync", "GetAsync`0(int)", "(int)"),
            Item("GetAsync", "GetAsync`0(string)", "(string)"),
            Item("Save", "Save`0()", "()")
        ]);

        var names = resolved.Select(r => r.Name).ToList();

        CollectionAssert.Contains(names, "OrderService.GetAsync(int)");
        CollectionAssert.Contains(names, "OrderService.GetAsync(string)");
        CollectionAssert.Contains(names, "OrderService.Save");
    }

    [TestMethod]
    public void Ids_MatchTheRuntimeHashOfTheResolvedName()
    {
        var resolved = TraceMethodNaming.Resolve([Item("GetAsync", "GetAsync`0(int)", "(int)")]);

        Assert.AreEqual(StableIdParityTests.RuntimeStableId("OrderService.GetAsync"), resolved[0].Id);
    }

    [TestMethod]
    public void ExplicitNameAndId_Win()
    {
        var item = Item("GetAsync", "GetAsync`0(int)", "(int)") with { ExplicitName = "fetch order", ExplicitId = 77 };

        var resolved = TraceMethodNaming.Resolve([item]);

        Assert.AreEqual("fetch order", resolved[0].Name);
        Assert.AreEqual(77, resolved[0].Id);
    }

    [TestMethod]
    public void ExplicitName_IsNotDisambiguated()
    {
        var resolved = TraceMethodNaming.Resolve([
            Item("GetAsync", "GetAsync`0(int)", "(int)") with { ExplicitName = "fetch" },
            Item("GetAsync", "GetAsync`0(string)", "(string)")
        ]);

        CollectionAssert.Contains(resolved.Select(r => r.Name).ToList(), "fetch");
        CollectionAssert.Contains(resolved.Select(r => r.Name).ToList(), "OrderService.GetAsync(string)");
    }

    [TestMethod]
    public void Resolve_IsOrderIndependent()
    {
        var a = Item("A", "A`0()", "()");
        var b = Item("B", "B`0()", "()");

        CollectionAssert.AreEqual(
            TraceMethodNaming.Resolve([a, b]).Select(r => r.Name).ToList(),
            TraceMethodNaming.Resolve([b, a]).Select(r => r.Name).ToList());
    }

    private static TraceMethodItem Item(string method, string signatureKey, string displaySignature)
    {
        return new TraceMethodItem(
            "Acme.Orders",
            new EquatableArray<string>(ImmutableArray.Create("partial class OrderService")),
            "OrderService",
            method,
            signatureKey,
            displaySignature,
            "public partial",
            "private async",
            "void",
            TraceReturnKind.Void,
            string.Empty,
            string.Empty,
            new EquatableArray<ParameterInfo>(ImmutableArray<ParameterInfo>.Empty),
            method + "Core",
            null,
            0,
            null,
            null);
    }
}
