using System;

namespace ProjectCeres.Analyzers.Annotations;

[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class PreAuthScopeAttribute : Attribute
{
}
