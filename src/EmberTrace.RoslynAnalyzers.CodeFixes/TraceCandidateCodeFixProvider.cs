using System.Collections.Immutable;
using System.Composition;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Formatting;

namespace EmberTrace.RoslynAnalyzers;

[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(TraceCandidateCodeFixProvider))]
[Shared]
public sealed class TraceCandidateCodeFixProvider : CodeFixProvider
{
    private const string TraceCandidateId = "ETA004";

    public override ImmutableArray<string> FixableDiagnosticIds =>
        ImmutableArray.Create(TraceCandidateId);

    public override FixAllProvider GetFixAllProvider()
    {
        return WellKnownFixAllProviders.BatchFixer;
    }

    public override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);
        if (root is null)
            return;

        if (root.FindNode(context.Diagnostics[0].Location.SourceSpan, getInnermostNodeForTie: true)
                .FirstAncestorOrSelf<MethodDeclarationSyntax>() is not { Body: not null } method)
            return;

        context.RegisterCodeFix(
            CodeAction.Create(
                "Instrument with [Trace]",
                token => Apply(context.Document, root, method),
                nameof(TraceCandidateCodeFixProvider)),
            context.Diagnostics[0]);
    }

    private static Task<Document> Apply(Document document, SyntaxNode root, MethodDeclarationSyntax method)
    {
        var withoutScope = method.Body!.WithStatements(
            SyntaxFactory.List(method.Body.Statements.Skip(1)));

        var core = method
            .WithIdentifier(SyntaxFactory.Identifier(method.Identifier.Text + "Core"))
            .WithBody(withoutScope)
            .WithModifiers(SyntaxFactory.TokenList(SyntaxFactory.Token(SyntaxKind.PrivateKeyword)))
            .WithAttributeLists(default)
            .WithAdditionalAnnotations(Formatter.Annotation);

        var declaration = method
            .WithBody(null)
            .WithExpressionBody(null)
            .WithSemicolonToken(SyntaxFactory.Token(SyntaxKind.SemicolonToken))
            .AddModifiers(SyntaxFactory.Token(SyntaxKind.PartialKeyword))
            .AddAttributeLists(SyntaxFactory.AttributeList(SyntaxFactory.SingletonSeparatedList(
                SyntaxFactory.Attribute(SyntaxFactory.ParseName("Trace")))))
            .WithAdditionalAnnotations(Formatter.Annotation);

        return Task.FromResult(document.WithSyntaxRoot(
            root.ReplaceNode(method, new SyntaxNode[] { declaration, core })));
    }
}
