using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace ProjectCeres.Analyzers;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class DateTimeWallClockAnalyzer : DiagnosticAnalyzer
{
    private const string DateTimeFullName = "System.DateTime";
    private const string AllowsWallClockAttributeFullName = "ProjectCeres.Analyzers.Annotations.AllowsWallClockAttribute";
    private const string ModelsNamespacePrefix = "ProjectCeres.Models";

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(Diagnostics.CER004_DateTimeWallClock);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSyntaxNodeAction(AnalyzeMemberAccess, SyntaxKind.SimpleMemberAccessExpression);
    }

    private static void AnalyzeMemberAccess(SyntaxNodeAnalysisContext ctx)
    {
        if (ctx.Node is not MemberAccessExpressionSyntax memberAccess) return;
        var name = memberAccess.Name.Identifier.Text;
        if (name != "UtcNow" && name != "Now") return;

        var typeInfo = ctx.SemanticModel.GetTypeInfo(memberAccess.Expression);
        if (typeInfo.Type?.ToDisplayString() != DateTimeFullName) return;

        // Exclude /Migrations/ files
        var filePath = memberAccess.SyntaxTree.FilePath ?? string.Empty;
        if (filePath.Contains("/Migrations/") || filePath.Contains("\\Migrations\\")) return;

        // Exclude property initialisers inside ProjectCeres.Models namespace
        var enclosingType = memberAccess.FirstAncestorOrSelf<TypeDeclarationSyntax>();
        if (enclosingType is not null)
        {
            var typeSymbol = ctx.SemanticModel.GetDeclaredSymbol(enclosingType);
            var ns = typeSymbol?.ContainingNamespace?.ToDisplayString() ?? string.Empty;
            if (ns.StartsWith(ModelsNamespacePrefix, System.StringComparison.Ordinal))
            {
                // Check if we're inside a property initialiser
                var enclosingProperty = memberAccess.FirstAncestorOrSelf<PropertyDeclarationSyntax>();
                if (enclosingProperty?.Initializer is not null) return;
            }
        }

        // Check enclosing member for [AllowsWallClock]
        var enclosingMember = memberAccess.FirstAncestorOrSelf<MemberDeclarationSyntax>();
        if (enclosingMember is not null)
        {
            var memberSymbol = ctx.SemanticModel.GetDeclaredSymbol(enclosingMember);
            if (memberSymbol?.GetAttributes().Any(a =>
                a.AttributeClass?.ToDisplayString() == AllowsWallClockAttributeFullName) == true)
            {
                return;
            }
        }

        ctx.ReportDiagnostic(Diagnostic.Create(
            Diagnostics.CER004_DateTimeWallClock,
            memberAccess.GetLocation(),
            $"DateTime.{name}"));
    }
}
