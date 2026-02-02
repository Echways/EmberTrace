using System.Collections.Generic;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace EmberTrace.RoslynAnalyzers;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class UsageAnalyzers : DiagnosticAnalyzer
{
    public const string ScopeNotDisposedId = "ETA001";
    public const string AsyncScopeNotAwaitedId = "ETA002";
    public const string FlowHandleNotEndedId = "ETA003";

    private static readonly DiagnosticDescriptor ScopeNotDisposed = new(
        ScopeNotDisposedId,
        "Scope is not disposed",
        "Scope should be wrapped in a using statement",
        "EmberTrace.Usage",
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor AsyncScopeNotAwaited = new(
        AsyncScopeNotAwaitedId,
        "AsyncScope is not awaited",
        "AsyncScope should be wrapped in an await using statement",
        "EmberTrace.Usage",
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor FlowHandleNotEnded = new(
        FlowHandleNotEndedId,
        "FlowHandle is not ended",
        "FlowHandle should call End or TryEnd",
        "EmberTrace.Usage",
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(ScopeNotDisposed, AsyncScopeNotAwaited, FlowHandleNotEnded);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();

        context.RegisterCompilationStartAction(startContext =>
        {
            var tracerType = startContext.Compilation.GetTypeByMetadataName("EmberTrace.Tracer");
            var flowHandleType = startContext.Compilation.GetTypeByMetadataName("EmberTrace.Flow.FlowHandle");
            if (tracerType is null || flowHandleType is null)
                return;

            startContext.RegisterSyntaxNodeAction(
                ctx => AnalyzeInvocation(ctx, tracerType),
                SyntaxKind.InvocationExpression);

            startContext.RegisterOperationBlockStartAction(
                ctx => AnalyzeFlowHandleBlock(ctx, tracerType, flowHandleType));
        });
    }

    private static void AnalyzeInvocation(SyntaxNodeAnalysisContext context, INamedTypeSymbol tracerType)
    {
        var invocation = (InvocationExpressionSyntax)context.Node;
        var symbol = context.SemanticModel.GetSymbolInfo(invocation, context.CancellationToken).Symbol as IMethodSymbol;
        if (symbol is null)
            return;

        if (!SymbolEqualityComparer.Default.Equals(symbol.ContainingType, tracerType))
            return;

        if (symbol.Name == "Scope")
        {
            var usingInfo = GetUsingInfo(invocation);
            if (!usingInfo.IsUsing)
                context.ReportDiagnostic(Diagnostic.Create(ScopeNotDisposed, invocation.GetLocation()));
        }
        else if (symbol.Name == "ScopeAsync")
        {
            var usingInfo = GetUsingInfo(invocation);
            if (!usingInfo.IsUsing || !usingInfo.IsAwait)
                context.ReportDiagnostic(Diagnostic.Create(AsyncScopeNotAwaited, invocation.GetLocation()));
        }
    }

    private static void AnalyzeFlowHandleBlock(
        OperationBlockStartAnalysisContext context,
        INamedTypeSymbol tracerType,
        INamedTypeSymbol flowHandleType)
    {
        var created = new Dictionary<ILocalSymbol, SyntaxNode>(SymbolEqualityComparer.Default);
        var ended = new HashSet<ILocalSymbol>(SymbolEqualityComparer.Default);

        context.RegisterOperationAction(opContext =>
        {
            var decl = (IVariableDeclaratorOperation)opContext.Operation;
            if (!SymbolEqualityComparer.Default.Equals(decl.Symbol.Type, flowHandleType))
                return;

            if (decl.Initializer?.Value is IInvocationOperation inv && IsFlowStartNewHandle(inv, tracerType))
                created[decl.Symbol] = decl.Syntax;
        }, OperationKind.VariableDeclarator);

        context.RegisterOperationAction(opContext =>
        {
            var assignment = (ISimpleAssignmentOperation)opContext.Operation;
            if (assignment.Target is not ILocalReferenceOperation localRef)
                return;

            if (!SymbolEqualityComparer.Default.Equals(localRef.Local.Type, flowHandleType))
                return;

            if (assignment.Value is IInvocationOperation inv && IsFlowStartNewHandle(inv, tracerType))
                created[localRef.Local] = assignment.Syntax;
        }, OperationKind.SimpleAssignment);

        context.RegisterOperationAction(opContext =>
        {
            var invocation = (IInvocationOperation)opContext.Operation;
            if (invocation.Instance is not ILocalReferenceOperation localRef)
                return;

            if (!SymbolEqualityComparer.Default.Equals(localRef.Local.Type, flowHandleType))
                return;

            var name = invocation.TargetMethod.Name;
            if (name == "End" || name == "TryEnd")
                ended.Add(localRef.Local);
        }, OperationKind.Invocation);

        context.RegisterOperationBlockEndAction(endContext =>
        {
            foreach (var kvp in created)
            {
                if (ended.Contains(kvp.Key))
                    continue;

                endContext.ReportDiagnostic(Diagnostic.Create(FlowHandleNotEnded, kvp.Value.GetLocation()));
            }
        });
    }

    private static bool IsFlowStartNewHandle(IInvocationOperation invocation, INamedTypeSymbol tracerType)
    {
        var method = invocation.TargetMethod;
        return method.Name == "FlowStartNewHandle" &&
               SymbolEqualityComparer.Default.Equals(method.ContainingType, tracerType);
    }

    private static UsingInfo GetUsingInfo(SyntaxNode node)
    {
        for (var current = node; current is not null; current = current.Parent)
        {
            if (current is UsingStatementSyntax usingStatement)
            {
                if (usingStatement.Expression == node)
                    return new UsingInfo(true, usingStatement.AwaitKeyword != default);

                if (usingStatement.Declaration is not null)
                    return new UsingInfo(true, usingStatement.AwaitKeyword != default);
            }

            if (current is LocalDeclarationStatementSyntax localDecl && localDecl.UsingKeyword != default)
                return new UsingInfo(true, localDecl.AwaitKeyword != default);
        }

        return new UsingInfo(false, false);
    }

    private readonly struct UsingInfo
    {
        public readonly bool IsUsing;
        public readonly bool IsAwait;

        public UsingInfo(bool isUsing, bool isAwait)
        {
            IsUsing = isUsing;
            IsAwait = isAwait;
        }
    }
}
