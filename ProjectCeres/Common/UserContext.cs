namespace ProjectCeres.Common;

/// <summary>
/// Discriminated union representing the four cases that <see cref="ICurrentUserAccessor"/>
/// can resolve to. Stage 7.6.7 / ADR-0073 — replaces the prior <c>Guid.Empty</c> overload
/// where one bit pattern meant four different things (pre-auth HTTP, EF model creation,
/// background thread without scope, test forgot to bind a user).
///
/// <para>
/// Pattern-match on <see cref="UserContext"/> in any code that needs to behave differently
/// per case. The C# exhaustiveness checker reports missing cases at compile time.
/// </para>
///
/// <para>
/// The private constructor closes the hierarchy — only the four nested records can derive
/// from <see cref="UserContext"/>. New cases require a code-review; pattern-match sites
/// stay exhaustive by design.
/// </para>
/// </summary>
public abstract record UserContext
{
    private UserContext() { }

    /// <summary>Authenticated request or background scope with a real user.</summary>
    public sealed record Resolved(Guid UserId) : UserContext;

    /// <summary>Pre-auth HTTP request reaching one of the documented pre-auth call sites.</summary>
    public sealed record PreAuth(string CallSite) : UserContext;

    /// <summary>Background work that hasn't yet entered an <see cref="IBackgroundJobScope"/>.</summary>
    public sealed record Background(string Reason) : UserContext;

    /// <summary>EF model creation or test bootstrap before any context is established.</summary>
    public sealed record Uninitialized : UserContext
    {
        /// <summary>Singleton — there's no per-instance state to discriminate.</summary>
        public static readonly Uninitialized Instance = new();
    }
}
