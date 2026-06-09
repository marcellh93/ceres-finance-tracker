using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace ProjectCeres.Analyzers;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class PreAuthScopeMarkerAnalyzer : DiagnosticAnalyzer
{
    private const string PreAuthScopeAttributeFullName = "ProjectCeres.Analyzers.Annotations.PreAuthScopeAttribute";
    private const string BeginPreAuthUserScopeMethodName = "BeginPreAuthUserScopeAsync";

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(Diagnostics.CER006_PreAuthScopeMarkerMissing);

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
        if (memberAccess.Name.Identifier.Text != BeginPreAuthUserScopeMethodName) return;

        // Walk up to the enclosing type declaration (lexical, not semantic — no GetEnclosingSymbol
        // null cases for lambdas / property getters / local functions; see spec section 2.1).
        var enclosingType = invocation.FirstAncestorOrSelf<TypeDeclarationSyntax>();
        if (enclosingType is null) return;

        var typeSymbol = ctx.SemanticModel.GetDeclaredSymbol(enclosingType);
        if (typeSymbol is null) return;

        // Fire when the enclosing class is NOT marked [PreAuthScope] (the reverse of CER001).
        var hasPreAuthScope = typeSymbol.GetAttributes()
            .Any(a => a.AttributeClass?.ToDisplayString() == PreAuthScopeAttributeFullName);
        if (hasPreAuthScope) return;

        ctx.ReportDiagnostic(Diagnostic.Create(
            Diagnostics.CER006_PreAuthScopeMarkerMissing,
            invocation.GetLocation(),
            typeSymbol.Name));
    }
}
