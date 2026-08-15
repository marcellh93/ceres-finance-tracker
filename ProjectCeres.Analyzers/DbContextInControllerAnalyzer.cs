using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace ProjectCeres.Analyzers;

/// <summary>CER007 — flags AppDbContext usage inside Controllers/ (ADR-0017).</summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class DbContextInControllerAnalyzer : DiagnosticAnalyzer
{
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(Diagnostics.CER007_DbContextInController);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSyntaxNodeAction(AnalyzeParameter, SyntaxKind.Parameter);
    }

    private static void AnalyzeParameter(SyntaxNodeAnalysisContext context)
    {
        var parameter = (ParameterSyntax)context.Node;
        if (parameter.Type is null) return;

        var path = context.Node.SyntaxTree.FilePath ?? string.Empty;
        if (!IsControllerPath(path)) return;

        var typeInfo = context.SemanticModel.GetTypeInfo(parameter.Type, context.CancellationToken);
        if (typeInfo.Type?.Name != "AppDbContext") return;

        var owner = parameter.FirstAncestorOrSelf<TypeDeclarationSyntax>();
        var ownerName = owner?.Identifier.Text ?? "Controller";

        context.ReportDiagnostic(Diagnostic.Create(
            Diagnostics.CER007_DbContextInController,
            parameter.GetLocation(),
            ownerName));
    }

    private static bool IsControllerPath(string path)
    {
        var normalized = path.Replace('\\', '/');
        return normalized.Contains("/Controllers/") && !normalized.Contains("/Migrations/");
    }
}
