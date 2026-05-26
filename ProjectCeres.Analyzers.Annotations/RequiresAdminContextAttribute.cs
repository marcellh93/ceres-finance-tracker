using System;

namespace ProjectCeres.Analyzers.Annotations;

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = false, Inherited = false)]
public sealed class RequiresAdminContextAttribute : Attribute
{
}
