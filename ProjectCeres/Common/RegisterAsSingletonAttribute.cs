namespace ProjectCeres.Common;

/// <summary>
/// Escape hatch for the minority of Scrutor-scanned classes that need Singleton
/// lifetime instead of the default Scoped (Stage 13.a, spec D4). Apply directly
/// to the class being registered; the Scan(...) rules in Program.cs check for
/// this attribute and call WithSingletonLifetime() instead of WithScopedLifetime()
/// when present.
/// </summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = false)]
public sealed class RegisterAsSingletonAttribute : Attribute;
