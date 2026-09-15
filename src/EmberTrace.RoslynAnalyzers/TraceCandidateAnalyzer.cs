using System.Collections.Immutable;
using EmberTrace.Generator.Generator;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace EmberTrace.RoslynAnalyzers;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class TraceCandidateAnalyzer : DiagnosticAnalyzer
{
    public const string TraceCandidateId = "ETA004";

    private const string CoreSuffix = "Core";

    private static readonly DiagnosticDescriptor TraceCandidate = new(
        TraceCandidateId,
        "Method can be instrumented with Trace",
        "'{0}' opens with a manual scope and can become a [Trace] partial method",
        "EmberTrace.Usage",
        DiagnosticSeverity.Info,
        true);

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(TraceCandidate);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();

        context.RegisterCompilationStartAction(startContext =>
        {
            var tracerType = startContext.Compilation.GetTypeByMetadataName("EmberTrace.Tracer");
            if (tracerType is null)
                return;

            startContext.RegisterSyntaxNodeAction(ctx => Analyze(ctx, tracerType), SyntaxKind.MethodDeclaration);
        });
    }

    private static void Analyze(SyntaxNodeAnalysisContext context, INamedTypeSymbol tracerType)
    {
        var method = (MethodDeclarationSyntax)context.Node;

        if (method.Modifiers.Any(SyntaxKind.PartialKeyword) || method.Body is not { Statements.Count: > 0 } body)
            return;

        if (!OpensWithTracerScope(context, body, tracerType))
            return;

        if (context.SemanticModel.GetDeclaredSymbol(method, context.CancellationToken) is not { } symbol
            || !TraceMethodShape.CanWrap(symbol, method)
            || IsCoreOfATracedPartial(symbol)
            || !symbol.ContainingType.GetMembers(symbol.Name + CoreSuffix).IsEmpty)
            return;

        context.ReportDiagnostic(Diagnostic.Create(TraceCandidate, method.Identifier.GetLocation(),
            method.Identifier.Text));
    }

    private static bool OpensWithTracerScope(SyntaxNodeAnalysisContext context, BlockSyntax body,
        INamedTypeSymbol tracerType)
    {
        return body.Statements[0] is LocalDeclarationStatementSyntax declaration
               && declaration.UsingKeyword.RawKind != 0
               && declaration.Declaration.Variables.Count == 1
               && declaration.Declaration.Variables[0].Initializer?.Value is InvocationExpressionSyntax invocation
               && context.SemanticModel.GetSymbolInfo(invocation, context.CancellationToken).Symbol
                   is IMethodSymbol { Name: "Scope" or "ScopeAsync" } scope
               && SymbolEqualityComparer.Default.Equals(scope.ContainingType, tracerType);
    }

    private static bool IsCoreOfATracedPartial(IMethodSymbol method)
    {
        var name = method.Name;
        if (!name.EndsWith(CoreSuffix, StringComparison.Ordinal))
            return false;

        foreach (var member in method.ContainingType.GetMembers(name.Substring(0, name.Length - CoreSuffix.Length)))
            if (member is IMethodSymbol { IsPartialDefinition: true })
                return true;

        return false;
    }
}
