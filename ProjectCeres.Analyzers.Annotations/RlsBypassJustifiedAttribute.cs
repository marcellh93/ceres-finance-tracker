using System;

namespace ProjectCeres.Analyzers.Annotations;

[AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = false)]
public sealed class RlsBypassJustifiedAttribute : Attribute
{
    public RlsBypassJustifiedAttribute(string ticket) => Ticket = ticket;
    public string Ticket { get; }
}
