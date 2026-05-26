using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace ProjectCeres.Analyzers;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class PreAuthScopeTransactionAnalyzer : DiagnosticAnalyzer
{
    private const string PreAuthScopeAttributeFullName = "ProjectCeres.Analyzers.Annotations.PreAuthScopeAttribute";
    private const string BeginTransactionMethodName = "BeginTransactionAsync";

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(Diagnostics.CER001_PreAuthScopeTransactionType);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSyntaxNodeAction(AnalyzeInvocation, SyntaxKind.InvocationExpression);
    }

    private static void AnalyzeInvocation(SyntaxNodeAnalysisContext ctx)
    {
        if (ctx.Node is not InvocationExpressionSyntax invocation) return;
        if (invocation.Expression is not MemberAccessExpressionSyntax memberAccess) return;
        if (memberAccess.Name.Identifier.Text != BeginTransactionMethodName) return;

        // Walk up to the enclosing type declaration
        var enclosingType = invocation.FirstAncestorOrSelf<TypeDeclarationSyntax>();
        if (enclosingType is null) return;

        // Check if the enclosing type carries [PreAuthScope]
        var typeSymbol = ctx.SemanticModel.GetDeclaredSymbol(enclosingType);
        if (typeSymbol is null) return;

        var hasPreAuthScope = typeSymbol.GetAttributes()
            .Any(a => a.AttributeClass?.ToDisplayString() == PreAuthScopeAttributeFullName);

        if (!hasPreAuthScope) return;

        // Exclude PreAuthRlsScope.cs itself (the helper LEGITIMATELY calls BeginTransactionAsync)
        var filePath = invocation.SyntaxTree.FilePath ?? string.Empty;
        if (filePath.EndsWith("PreAuthRlsScope.cs", System.StringComparison.OrdinalIgnoreCase)) return;

        ctx.ReportDiagnostic(Diagnostic.Create(
            Diagnostics.CER001_PreAuthScopeTransactionType,
            invocation.GetLocation(),
            typeSymbol.Name));
    }
}
