using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace ProjectCeres.Analyzers;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class IgnoreQueryFiltersOnUserOwnedAnalyzer : DiagnosticAnalyzer
{
    private const string IgnoreQueryFiltersMethodName = "IgnoreQueryFilters";
    private const string IUserOwnedFullName = "ProjectCeres.Common.IUserOwned";
    private const string AppDbContextFullName = "ProjectCeres.Data.AppDbContext";
    private const string RlsBypassJustifiedAttributeFullName = "ProjectCeres.Analyzers.Annotations.RlsBypassJustifiedAttribute";

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(Diagnostics.CER002_IgnoreQueryFiltersOnUserOwned);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterOperationAction(AnalyzeInvocation, OperationKind.Invocation);
    }

    private static void AnalyzeInvocation(OperationAnalysisContext ctx)
    {
        if (ctx.Operation is not IInvocationOperation invocation) return;
        if (invocation.TargetMethod.Name != IgnoreQueryFiltersMethodName) return;

        // Receiver must be a Queryable<T> where T implements IUserOwned
        var receiverType = invocation.Instance?.Type
            ?? (invocation.Arguments.Length > 0 ? invocation.Arguments[0].Value.Type : null);
        if (receiverType is not INamedTypeSymbol named) return;

        var elementType = named.TypeArguments.FirstOrDefault();
        if (elementType is null) return;

        var implementsUserOwned = elementType.AllInterfaces
            .Any(i => i.ToDisplayString() == IUserOwnedFullName);
        if (!implementsUserOwned) return;

        // Check the containing DbContext type — must be AppDbContext (not AdminDbContext)
        // Find the enclosing field/property receiver and check its type
        //
        // Scope-limitation: GetEnclosingSymbol as IMethodSymbol returns null for property
        // getters / setters, auto-property initialisers, lambdas, and local functions.
        // The analyzer silently exits in those cases — CER002 cannot fire there today.
        // CER006 (planned for 9.5g) is expected to surface this gap.
        var containingType = invocation.SemanticModel?.GetEnclosingSymbol(invocation.Syntax.SpanStart) as IMethodSymbol;
        if (containingType is null) return;

        // Walk the containing class's fields/properties/constructors via short-circuit ||
        var containingClass = containingType.ContainingType;
        var usesAppDbContext = containingClass != null && (
            containingClass.GetMembers().OfType<IFieldSymbol>()
                .Any(f => f.Type.ToDisplayString() == AppDbContextFullName)
            || containingClass.GetMembers().OfType<IPropertySymbol>()
                .Any(p => p.Type.ToDisplayString() == AppDbContextFullName)
            || containingClass.InstanceConstructors
                .SelectMany(c => c.Parameters)
                .Any(p => p.Type.ToDisplayString() == AppDbContextFullName)
        );

        if (!usesAppDbContext) return;

        // Check if the enclosing method has [RlsBypassJustified]
        var hasJustification = containingType.GetAttributes()
            .Any(a => a.AttributeClass?.ToDisplayString() == RlsBypassJustifiedAttributeFullName);
        if (hasJustification) return;

        ctx.ReportDiagnostic(Diagnostic.Create(
            Diagnostics.CER002_IgnoreQueryFiltersOnUserOwned,
            invocation.Syntax.GetLocation(),
            elementType.Name));
    }
}
