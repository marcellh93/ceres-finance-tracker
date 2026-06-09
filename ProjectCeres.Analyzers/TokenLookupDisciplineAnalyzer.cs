using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace ProjectCeres.Analyzers;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class TokenLookupDisciplineAnalyzer : DiagnosticAnalyzer
{
    private const string ModelsNamespacePrefix = "ProjectCeres.Models";
    private const string IUserOwnedFullName = "ProjectCeres.Common.IUserOwned";

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(Diagnostics.CER005_TokenLookupDiscipline);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSymbolAction(AnalyzeNamedType, SymbolKind.NamedType);
    }

    private static void AnalyzeNamedType(SymbolAnalysisContext ctx)
    {
        if (ctx.Symbol is not INamedTypeSymbol type) return;

        // Q1: a class named *Token (TypeKind.Class checked first — cheapest, and excludes the
        // EmailChangeTokenPurpose enum and any future enum/struct named *Token).
        if (type.TypeKind != TypeKind.Class) return;
        if (!type.Name.EndsWith("Token", System.StringComparison.Ordinal)) return;

        // Q2: under the ProjectCeres.Models namespace.
        var ns = type.ContainingNamespace?.ToDisplayString() ?? string.Empty;
        if (!ns.StartsWith(ModelsNamespacePrefix, System.StringComparison.Ordinal)) return;

        // Q3: implements ProjectCeres.Common.IUserOwned (the user-owned-row marker).
        var isUserOwned = type.AllInterfaces.Any(i => i.ToDisplayString() == IUserOwnedFullName);
        if (!isUserOwned) return;

        // Q4: carries a byte[] TokenLookup property. Missing OR wrong-typed -> fire.
        var hasByteArrayLookup = type.GetMembers()
            .OfType<IPropertySymbol>()
            .Any(p => p.Name == "TokenLookup"
                      && p.Type is IArrayTypeSymbol { ElementType.SpecialType: SpecialType.System_Byte });
        if (hasByteArrayLookup) return;

        ctx.ReportDiagnostic(Diagnostic.Create(
            Diagnostics.CER005_TokenLookupDiscipline,
            type.Locations.FirstOrDefault() ?? Location.None,
            type.Name));
    }
}
