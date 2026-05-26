using System.Collections.Immutable;
using System.Linq;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace ProjectCeres.Analyzers;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class RlsBypassJustifiedTicketFormatAnalyzer : DiagnosticAnalyzer
{
    private const string RlsBypassJustifiedAttributeFullName = "ProjectCeres.Analyzers.Annotations.RlsBypassJustifiedAttribute";
    private static readonly Regex TicketFormat = new(@"^(CER|TICKET|ADR)-\d+$", RegexOptions.Compiled);

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(Diagnostics.CER010_RlsBypassJustifiedTicketFormat);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSymbolAction(AnalyzeMethodAttributes, SymbolKind.Method);
    }

    private static void AnalyzeMethodAttributes(SymbolAnalysisContext ctx)
    {
        if (ctx.Symbol is not IMethodSymbol method) return;

        foreach (var attr in method.GetAttributes())
        {
            if (attr.AttributeClass?.ToDisplayString() != RlsBypassJustifiedAttributeFullName) continue;
            if (attr.ConstructorArguments.Length == 0) continue;
            if (attr.ConstructorArguments[0].Value is not string ticket) continue;
            if (TicketFormat.IsMatch(ticket)) continue;

            var location = attr.ApplicationSyntaxReference?.GetSyntax().GetLocation()
                ?? method.Locations.FirstOrDefault()
                ?? Location.None;

            ctx.ReportDiagnostic(Diagnostic.Create(
                Diagnostics.CER010_RlsBypassJustifiedTicketFormat,
                location,
                ticket));
        }
    }
}
