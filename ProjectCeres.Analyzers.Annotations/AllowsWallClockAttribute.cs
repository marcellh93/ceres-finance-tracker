using System;

namespace ProjectCeres.Analyzers.Annotations;

[AttributeUsage(
    AttributeTargets.Method | AttributeTargets.Property | AttributeTargets.Constructor,
    AllowMultiple = false,
    Inherited = false)]
public sealed class AllowsWallClockAttribute : Attribute
{
    public AllowsWallClockAttribute(string reason) => Reason = reason;
    public string Reason { get; }
}
