using System.Collections.Immutable;
using System.Composition;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Formatting;

namespace EmberTrace.RoslynAnalyzers;

[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(UsageCodeFixProvider))]
[Shared]
public sealed class UsageCodeFixProvider : CodeFixProvider
{
    private const string ScopeNotDisposedId = "ETA001";
    private const string AsyncScopeNotAwaitedId = "ETA002";

    public override ImmutableArray<string> FixableDiagnosticIds =>
        ImmutableArray.Create(ScopeNotDisposedId, AsyncScopeNotAwaitedId);

    public override FixAllProvider GetFixAllProvider()
    {
        return WellKnownFixAllProviders.BatchFixer;
    }

    public override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);
        if (root is null)
            return;

        var diagnostic = context.Diagnostics[0];
        if (root.FindNode(diagnostic.Location.SourceSpan, getInnermostNodeForTie: true)
                .FirstAncestorOrSelf<InvocationExpressionSyntax>() is not { } invocation)
            return;

        var isAsync = diagnostic.Id == AsyncScopeNotAwaitedId;
        var keyword = isAsync ? "await using" : "using";

        switch (invocation.Parent)
        {
            case EqualsValueClauseSyntax
            {
                Parent: VariableDeclaratorSyntax
                {
                    Parent: VariableDeclarationSyntax { Parent: LocalDeclarationStatementSyntax declaration }
                }
            }:
                Register(
                    context,
                    diagnostic,
                    $"Declare with '{keyword}'",
                    _ => Task.FromResult(AddUsingKeyword(context.Document, root, declaration, isAsync)));
                break;

            case ExpressionStatementSyntax { Parent: BlockSyntax block } statement:
                Register(
                    context,
                    diagnostic,
                    $"Wrap the rest of the block in '{keyword}'",
                    _ => Task.FromResult(WrapRestOfBlock(context.Document, root, block, statement, invocation,
                        isAsync)));
                break;
        }
    }

    private static void Register(CodeFixContext context, Diagnostic diagnostic, string title,
        Func<CancellationToken, Task<Document>> createChangedDocument)
    {
        context.RegisterCodeFix(CodeAction.Create(title, createChangedDocument, title), diagnostic);
    }

    private static Document AddUsingKeyword(
        Document document,
        SyntaxNode root,
        LocalDeclarationStatementSyntax declaration,
        bool isAsync)
    {
        var updated = declaration
            .WithLeadingTrivia(SyntaxTriviaList.Empty)
            .WithUsingKeyword(SyntaxFactory.Token(SyntaxKind.UsingKeyword).WithTrailingTrivia(SyntaxFactory.Space));

        if (isAsync)
            updated = updated.WithAwaitKeyword(SyntaxFactory.Token(SyntaxKind.AwaitKeyword)
                .WithTrailingTrivia(SyntaxFactory.Space));

        updated = updated.WithLeadingTrivia(declaration.GetLeadingTrivia());

        return document.WithSyntaxRoot(root.ReplaceNode(declaration, updated));
    }

    private static Document WrapRestOfBlock(
        Document document,
        SyntaxNode root,
        BlockSyntax block,
        ExpressionStatementSyntax statement,
        InvocationExpressionSyntax invocation,
        bool isAsync)
    {
        var index = block.Statements.IndexOf(statement);

        var usingStatement = SyntaxFactory.UsingStatement(
                isAsync ? SyntaxFactory.Token(SyntaxKind.AwaitKeyword) : default,
                SyntaxFactory.Token(SyntaxKind.UsingKeyword),
                SyntaxFactory.Token(SyntaxKind.OpenParenToken),
                null,
                invocation.WithoutTrivia(),
                SyntaxFactory.Token(SyntaxKind.CloseParenToken),
                SyntaxFactory.Block(block.Statements.Skip(index + 1)))
            .WithLeadingTrivia(statement.GetLeadingTrivia())
            .WithAdditionalAnnotations(Formatter.Annotation);

        var statements = SyntaxFactory.List(block.Statements.Take(index).Append<StatementSyntax>(usingStatement));

        return document.WithSyntaxRoot(root.ReplaceNode(block, block.WithStatements(statements)));
    }
}