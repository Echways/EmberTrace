using EmberTrace.Generator.Generator;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace EmberTrace.Generator.Tests;

[TestClass]
public class MethodSignatureTests
{
    [TestMethod]
    public void Modifiers_MoveThePartialKeywordLast()
    {
        var (method, node) = Find("public partial class C { public partial void M(); }", "M");

        Assert.AreEqual("public partial", MethodSignature.Modifiers(node));
        Assert.AreEqual("private async", MethodSignature.HelperModifiers(node));
        Assert.IsNotNull(method);
    }

    [TestMethod]
    public void Modifiers_PreserveNewAndStatic()
    {
        var (_, node) = Find("public partial class C { public static new partial void M(); }", "M");

        Assert.AreEqual("public static new partial", MethodSignature.Modifiers(node));
        Assert.AreEqual("private static async", MethodSignature.HelperModifiers(node));
    }

    [TestMethod]
    public void Parameters_DropDefaultValuesAndKeepRefKinds()
    {
        var (method, _) = Find(
            "public partial class C { public partial void M(int a, ref string b, out int c, in double d, int e = 5); }",
            "M");

        var parameters = MethodSignature.Parameters(method!);

        Assert.AreEqual("int a", parameters[0].Declaration);
        Assert.AreEqual("ref string b", parameters[1].Declaration);
        Assert.AreEqual("out int c", parameters[2].Declaration);
        Assert.AreEqual("in double d", parameters[3].Declaration);
        Assert.AreEqual("int e", parameters[4].Declaration);
        Assert.AreEqual("ref b", parameters[1].Argument);
        Assert.AreEqual("out c", parameters[2].Argument);
        Assert.AreEqual("in d", parameters[3].Argument);
        Assert.AreEqual("e", parameters[4].Argument);
    }

    [TestMethod]
    public void Parameters_EscapeKeywordNames()
    {
        var (method, _) = Find("public partial class C { public partial void M(int @class); }", "M");

        Assert.AreEqual("int @class", MethodSignature.Parameters(method!)[0].Declaration);
        Assert.AreEqual("@class", MethodSignature.Parameters(method!)[0].Argument);
    }

    [TestMethod]
    public void Render_KeepsNullableAnnotations()
    {
        var (method, _) = Find("#nullable enable\npublic partial class C { public partial string? M(string? a); }",
            "M");

        Assert.AreEqual("string?", MethodSignature.Render(method!.ReturnType));
        Assert.AreEqual("string?", MethodSignature.Parameters(method)[0].Type);
    }

    [TestMethod]
    public void Constraints_AreRenderedInLegalOrder()
    {
        var (method, _) = Find(
            "public partial class C { public partial void M<T, TKey>() where T : class, System.IDisposable, new() where TKey : unmanaged; }",
            "M");

        Assert.AreEqual("<T, TKey>", MethodSignature.TypeParameters(method!));
        Assert.AreEqual(
            " where T : class, global::System.IDisposable, new() where TKey : unmanaged",
            MethodSignature.Constraints(method!));
    }

    [TestMethod]
    public void TypeChain_RunsOutermostFirstAndKeepsTypeParameters()
    {
        var (method, _) = Find(
            "public partial class Outer<T> { public readonly partial struct Inner { public partial void M(); } }",
            "M");

        var chain = MethodSignature.TypeChain(method!.ContainingType);

        Assert.AreEqual(2, chain.Length);
        Assert.AreEqual("partial class Outer<T>", chain[0]);
        Assert.AreEqual("readonly partial struct Inner", chain[1]);
    }

    [TestMethod]
    [DataRow("void M()", (int)TraceReturnKind.Void)]
    [DataRow("int M()", (int)TraceReturnKind.Value)]
    [DataRow("System.Threading.Tasks.Task M()", (int)TraceReturnKind.Task)]
    [DataRow("System.Threading.Tasks.Task<int> M()", (int)TraceReturnKind.TaskOfT)]
    [DataRow("System.Threading.Tasks.ValueTask M()", (int)TraceReturnKind.ValueTask)]
    [DataRow("System.Threading.Tasks.ValueTask<int> M()", (int)TraceReturnKind.ValueTaskOfT)]
    public void ReturnKind_IsClassifiedFromTheReturnType(string signature, int expected)
    {
        var (method, _) = Find("public partial class C { public partial " + signature + "; }", "M");

        Assert.AreEqual((TraceReturnKind)expected, MethodSignature.ReturnKind(method!));
    }

    [TestMethod]
    public void SignatureKey_SeparatesOverloads()
    {
        var (first, _) = Find("public partial class C { public partial void M(int a); }", "M");
        var (second, _) = Find("public partial class C { public partial void M(string a); }", "M");

        Assert.AreNotEqual(MethodSignature.SignatureKey(first!), MethodSignature.SignatureKey(second!));
        Assert.AreEqual("(int)", MethodSignature.DisplaySignature(first!));
    }

    private static (IMethodSymbol? Method, MethodDeclarationSyntax Node) Find(string code, string name)
    {
        var compilation = GeneratorTestHost.Compile(code);
        var tree = compilation.SyntaxTrees.First();
        var node = tree.GetRoot().DescendantNodes().OfType<MethodDeclarationSyntax>()
            .First(m => m.Identifier.Text == name);

        return ((IMethodSymbol?)compilation.GetSemanticModel(tree).GetDeclaredSymbol(node), node);
    }
}
