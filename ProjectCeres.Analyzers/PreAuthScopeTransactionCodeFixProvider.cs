using System.Collections.Immutable;
using System.Composition;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace ProjectCeres.Analyzers;

[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(PreAuthScopeTransactionCodeFixProvider)), Shared]
public sealed class PreAuthScopeTransactionCodeFixProvider : CodeFixProvider
{
    private const string Title = "Use BeginPreAuthUserScopeAsync(userId, ct)";
    private const string TargetMethodName = "BeginPreAuthUserScopeAsync";

    public override ImmutableArray<string> FixableDiagnosticIds =>
        ImmutableArray.Create(Diagnostics.CER001_PreAuthScopeTransactionType.Id);

    public override FixAllProvider GetFixAllProvider() => WellKnownFixAllProviders.BatchFixer;

    public override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);
        if (root is null) return;

        var diagnostic = context.Diagnostics[0];
        var invocation = root.FindNode(diagnostic.Location.SourceSpan)
            .FirstAncestorOrSelf<InvocationExpressionSyntax>();
        if (invocation is null) return;
        if (invocation.Expression is not MemberAccessExpressionSyntax memberAccess) return;

        context.RegisterCodeFix(
            CodeAction.Create(
                title: Title,
                createChangedDocument: ct => ReplaceWithPreAuthScopeAsync(context.Document, root, invocation, memberAccess, ct),
                equivalenceKey: Title),
            diagnostic);
    }

    private static Task<Document> ReplaceWithPreAuthScopeAsync(
        Document document, SyntaxNode root, InvocationExpressionSyntax invocation,
        MemberAccessExpressionSyntax memberAccess, CancellationToken ct)
    {
        // Swap the method name only — receiver (e.g. _db.Database) is left as-is on purpose.
        var newMemberAccess = memberAccess.WithName(SyntaxFactory.IdentifierName(TargetMethodName));

        var args = SyntaxFactory.ArgumentList(SyntaxFactory.SeparatedList(new[]
        {
            SyntaxFactory.Argument(SyntaxFactory.IdentifierName("userId")),
            SyntaxFactory.Argument(SyntaxFactory.IdentifierName("ct")),
        }));

        var newInvocation = invocation
            .WithExpression(newMemberAccess)
            .WithArgumentList(args)
            .WithTriviaFrom(invocation);

        var newRoot = root.ReplaceNode(invocation, newInvocation);
        return Task.FromResult(document.WithSyntaxRoot(newRoot));
    }
}
