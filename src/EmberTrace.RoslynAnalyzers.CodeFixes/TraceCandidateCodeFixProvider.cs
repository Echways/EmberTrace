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
        return Task.FromResult(document.WithSyntaxRoot(
            root.ReplaceNode(method, new SyntaxNode[] { Declaration(method), Core(method) })));
    }

    private static MethodDeclarationSyntax Declaration(MethodDeclarationSyntax method)
    {
        var modifiers = method.Modifiers.Where(static modifier => !modifier.IsKind(SyntaxKind.AsyncKeyword)).ToList();

        if (!modifiers.Any(static modifier => IsAccessibility(modifier.Kind())))
            modifiers.Insert(0, SyntaxFactory.Token(SyntaxKind.PrivateKeyword));

        modifiers.Add(SyntaxFactory.Token(SyntaxKind.PartialKeyword));

        return method
            .WithModifiers(SyntaxFactory.TokenList(modifiers))
            .WithBody(null)
            .WithExpressionBody(null)
            .WithSemicolonToken(SyntaxFactory.Token(SyntaxKind.SemicolonToken))
            .AddAttributeLists(SyntaxFactory.AttributeList(SyntaxFactory.SingletonSeparatedList(
                SyntaxFactory.Attribute(SyntaxFactory.ParseName("Trace")))))
            .WithAdditionalAnnotations(Formatter.Annotation);
    }

    private static MethodDeclarationSyntax Core(MethodDeclarationSyntax method)
    {
        var kinds = method.Modifiers
            .Select(static modifier => modifier.Kind())
            .Where(static kind => kind is SyntaxKind.StaticKeyword or SyntaxKind.AsyncKeyword or SyntaxKind.ReadOnlyKeyword)
            .Prepend(SyntaxKind.PrivateKeyword);

        return method
            .WithIdentifier(SyntaxFactory.Identifier(method.Identifier.Text + "Core"))
            .WithModifiers(SyntaxFactory.TokenList(kinds.Select(static kind => SyntaxFactory.Token(kind))))
            .WithBody(method.Body!.WithStatements(SyntaxFactory.List(method.Body.Statements.Skip(1))))
            .WithAttributeLists(default)
            .WithAdditionalAnnotations(Formatter.Annotation);
    }

    private static bool IsAccessibility(SyntaxKind kind)
    {
        return kind is SyntaxKind.PublicKeyword or SyntaxKind.PrivateKeyword or SyntaxKind.ProtectedKeyword
            or SyntaxKind.InternalKeyword;
    }
}
