using System.Collections.Immutable;
using System.Composition;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace EmberTrace.RoslynAnalyzers;

[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(UsageCodeFixProvider))]
[Shared]
public sealed class UsageCodeFixProvider : CodeFixProvider
{
    private const string ScopeNotDisposedId = "ETA001";
    private const string AsyncScopeNotAwaitedId = "ETA002";

    public override ImmutableArray<string> FixableDiagnosticIds =>
        ImmutableArray.Create(ScopeNotDisposedId, AsyncScopeNotAwaitedId);

    public override FixAllProvider GetFixAllProvider() => WellKnownFixAllProviders.BatchFixer;

    public override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);
        if (root is null)
            return;

        var diagnostic = context.Diagnostics.First();
        var invocation = root.FindNode(diagnostic.Location.SourceSpan).FirstAncestorOrSelf<InvocationExpressionSyntax>();
        if (invocation is null)
            return;

        var statement = invocation.FirstAncestorOrSelf<ExpressionStatementSyntax>();
        if (statement is null)
            return;

        var isAsync = diagnostic.Id == AsyncScopeNotAwaitedId;
        var title = isAsync ? "Wrap in await using" : "Wrap in using";

        context.RegisterCodeFix(
            CodeAction.Create(
                title,
                cancellationToken => WrapInUsingAsync(context.Document, root, statement, invocation, isAsync, cancellationToken),
                equivalenceKey: title),
            diagnostic);
    }

    private static Task<Document> WrapInUsingAsync(
        Document document,
        SyntaxNode root,
        ExpressionStatementSyntax statement,
        InvocationExpressionSyntax invocation,
        bool isAsync,
        CancellationToken cancellationToken)
    {
        var usingStatement = SyntaxFactory.UsingStatement(
                isAsync ? SyntaxFactory.Token(SyntaxKind.AwaitKeyword) : default,
                SyntaxFactory.Token(SyntaxKind.UsingKeyword),
                SyntaxFactory.Token(SyntaxKind.OpenParenToken),
                declaration: null,
                expression: invocation,
                SyntaxFactory.Token(SyntaxKind.CloseParenToken),
                SyntaxFactory.Block())
            .WithLeadingTrivia(statement.GetLeadingTrivia())
            .WithTrailingTrivia(statement.GetTrailingTrivia());

        var newRoot = root.ReplaceNode(statement, usingStatement);
        return Task.FromResult(document.WithSyntaxRoot(newRoot));
    }
}
