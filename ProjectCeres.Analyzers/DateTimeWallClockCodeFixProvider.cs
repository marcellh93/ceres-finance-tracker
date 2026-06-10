using System.Collections.Immutable;
using System.Composition;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace ProjectCeres.Analyzers;

[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(DateTimeWallClockCodeFixProvider)), Shared]
public sealed class DateTimeWallClockCodeFixProvider : CodeFixProvider
{
    private const string Title = "Use _timeProvider.GetUtcNow().UtcDateTime";
    private const string TimeProviderFieldName = "_timeProvider";
    private const string TimeProviderFullName = "System.TimeProvider";

    public override ImmutableArray<string> FixableDiagnosticIds =>
        ImmutableArray.Create(Diagnostics.CER004_DateTimeWallClock.Id);

    public override FixAllProvider GetFixAllProvider() => WellKnownFixAllProviders.BatchFixer;

    public override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);
        if (root is null) return;

        var diagnostic = context.Diagnostics[0];
        var node = root.FindNode(diagnostic.Location.SourceSpan)
            .FirstAncestorOrSelf<MemberAccessExpressionSyntax>();
        if (node is null) return;

        var enclosingType = node.FirstAncestorOrSelf<TypeDeclarationSyntax>();
        if (enclosingType is null) return;

        var semanticModel = await context.Document.GetSemanticModelAsync(context.CancellationToken).ConfigureAwait(false);
        if (semanticModel is null) return;
        if (semanticModel.GetDeclaredSymbol(enclosingType, context.CancellationToken) is not INamedTypeSymbol typeSymbol) return;

        var hasTimeProviderField = typeSymbol.GetMembers(TimeProviderFieldName)
            .OfType<IFieldSymbol>()
            .Any(f => !f.IsStatic && IsTimeProviderOrSubtype(f.Type));
        if (!hasTimeProviderField) return;

        context.RegisterCodeFix(
            CodeAction.Create(
                title: Title,
                createChangedDocument: ct => ReplaceWithTimeProviderAsync(context.Document, root, node, ct),
                equivalenceKey: Title),
            diagnostic);
    }

    private static bool IsTimeProviderOrSubtype(ITypeSymbol? type)
    {
        for (var t = type; t is not null; t = t.BaseType)
        {
            if (t.ToDisplayString() == TimeProviderFullName) return true;
        }
        return false;
    }

    private static Task<Document> ReplaceWithTimeProviderAsync(
        Document document, SyntaxNode root, MemberAccessExpressionSyntax node, System.Threading.CancellationToken ct)
    {
        var replacement = SyntaxFactory.ParseExpression("_timeProvider.GetUtcNow().UtcDateTime")
            .WithTriviaFrom(node);
        var newRoot = root.ReplaceNode(node, replacement);
        return Task.FromResult(document.WithSyntaxRoot(newRoot));
    }
}
