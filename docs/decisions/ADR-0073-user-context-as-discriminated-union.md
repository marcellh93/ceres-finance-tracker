# ADR-0073 — `UserContext` as a discriminated union replacing the `Guid.Empty` overload

**Status:** Accepted (Phase 3, Stage 7.6.7 shipped 2026-05-14)

**Date:** 2026-05-14

**Supersedes (in part):**

- The "`Guid.Empty` as the safe default" amendment to ADR-0067 in `multi-tenancy-strategy.md` § Background processes and non-HTTP contexts and `planning-resolved.md`'s Stage 7 entry. ADR-0067's broader decision (background-process user resolution via `IUserScope` + `IUserJobRunner`) is **not** superseded — only the way "no user resolved yet" is represented in the type system.

## Context

Stage 7's `ICurrentUserAccessor` returns `Guid` for the current user. When no user is resolved, it returns `Guid.Empty`. Stage 7.5 extended this contract by treating `Guid.Empty` as a signal in the `RowLevelSecurityInterceptor` (no GUC set, fail-closed via RLS policy) and in `BackgroundJobScope` (doorway refusal — throws `InvalidOperationException`).

Across the codebase, `Guid.Empty` from `ICurrentUserAccessor` now means at least four different things:

1. **Pre-authentication HTTP request.** Login, register, password-reset request, lockout-unlock confirm. Legitimate state; the interceptor silently resets the GUC, the policy fails closed. Tagged by `IPreAuthCallSiteTagger`.
2. **EF Core model-creation time.** Global query filter expressions are evaluated eagerly before any HTTP context exists. Must not throw or the app fails to start. Originally drove the ADR-0067 amendment from "throw on missing user" to "return `Guid.Empty`."
3. **Background thread without a scope.** A background job that didn't route through `BackgroundJobScope.RunAsync`. This is a bug — `BackgroundJobScope` is the doorway and refuses `Guid.Empty`, but if a caller bypasses it, the failure mode is silent zero-row queries instead of a loud refusal.
4. **Test setup forgot to bind a user.** Identical signature to case 3 from the runtime's perspective.

The interceptor disambiguates cases 1 and 2 from cases 3 and 4 by consulting `IPreAuthCallSiteTagger`, which is a string-typed registry of `"ControllerName.ActionName"` keys. The tagger is itself a workaround for the type system's inability to express "this `Guid.Empty` is one of the legitimate ones."

This causes three concrete problems observed during Stage 7.5:

**Problem A — silent failure when a test forgets to bind a user.** A new integration test that does not wire `FakeCurrentUserAccessor(realUserId)` into its DI/fixture will pass model creation, fail every read with zero rows, and the test will fail at the assertion instead of at the cause. The diagnostic surface for "you forgot to bind a user" looks identical to "RLS is correctly filtering."

**Problem B — adding a new pre-auth route requires editing a stringly-typed registry.** `PreAuthCallSiteTagger` holds `"Auth.Register"`, `"Auth.Login"`, `"Auth.LoginTotp"`, `"Auth.PasswordResetRequest"`, `"LockoutUnlock.Confirm"`. Stage 8 adds email verification, an email-change confirmation flow, and a magic-link login — each of which is pre-auth and must be added to this list. Forgetting causes a warning log per request (cosmetic) AND, more importantly, doesn't fail the build — there is no compile-time relationship between the route handler and its pre-auth registration.

**Problem C — logs cannot distinguish bug from expected behavior.** A `LogWarning` "DB connection opened with no resolved user" fires for every legitimate pre-auth request as well as for every bug-induced case. Filtering to find real bugs requires text-matching the `IsLegitimatePreAuth` decision, which isn't logged.

## Decision

Replace `ICurrentUserAccessor.UserId : Guid` with `ICurrentUserAccessor.Context : UserContext`, a discriminated union (C# sealed class hierarchy) with four cases:

```csharp
public abstract record UserContext
{
    private UserContext() { }

    /// <summary>Authenticated request or background scope with a real user.</summary>
    public sealed record Resolved(Guid UserId) : UserContext;

    /// <summary>Pre-auth HTTP request reaching one of the documented pre-auth call sites.</summary>
    public sealed record PreAuth(string CallSite) : UserContext;

    /// <summary>Background work that hasn't yet entered an IBackgroundJobScope.</summary>
    public sealed record Background(string Reason) : UserContext;

    /// <summary>EF model creation or test bootstrap before any context is established.</summary>
    public sealed record Uninitialized : UserContext;
}
```

`ICurrentUserAccessor.UserId : Guid` is preserved as a convenience accessor that returns `Resolved.UserId` when the context is `Resolved`, and `Guid.Empty` for every other case. This preserves the EF global query filter expression `e.UserId == _currentUser.UserId` without rewriting every filter registration.

The `RowLevelSecurityInterceptor` switches on `Context`:

- `Resolved(userId)` → `SELECT set_config('app.current_user_ref', '<uuid>', false)`.
- `PreAuth(callSite)` → `RESET "app.current_user_ref"`. No warning log; `callSite` is included in `LogDebug` for traceability.
- `Background(reason)` → `LogError` + `RESET`. This case is always a bug — the background path should have routed through `BackgroundJobScope.RunAsync`.
- `Uninitialized` → silent skip (no DB roundtrip). This case can only fire at EF model-creation time; logging would be noise.

`IPreAuthCallSiteTagger` is **deleted**. The pre-auth call sites are tagged by the endpoint authoring code itself — each `[AllowAnonymous]` action that hits the database is decorated with `[PreAuthCallSite("Auth.Register")]` (or similar) and the `HttpContextCurrentUserAccessor` reads the attribute. New pre-auth routes become a compile-time addition (an attribute on the action) rather than a string-registry update.

`BackgroundJobScope.RunAsync` continues to refuse `Guid.Empty` at the doorway, but the refusal becomes `InvalidOperationException("Background jobs must declare a user. Current context: <Background reason | Uninitialized | PreAuth>")` — the exception message names which case the caller was in.

The new `UserContextRequiredException` is thrown by service-layer code that requires a `Resolved` context and is given anything else. Example call site: `AccountService.CreateAsync` (which today does not check) gains a `_currentUser.Require()` extension that returns `Resolved` or throws `UserContextRequiredException(expected: nameof(Resolved), actual: context.GetType().Name)`.

## Rationale

The change pays off in four ways, each tied to a Stage 7.5 pain point:

**1. The type system enforces the disambiguation.** Today `Guid.Empty` is the same bit pattern for four distinct cases; the disambiguation lives in `IPreAuthCallSiteTagger`, the call-site stack frame inspection, and developer memory. After the change, a `switch (context)` either handles every case explicitly or fails the C# exhaustiveness check.

**2. Pre-auth becomes compile-time-checked.** Adding a new pre-auth route means adding `[PreAuthCallSite("Email.Verify")]` to the action method. There is no registry to forget to update. The architecture test that today scans `IPreAuthCallSiteTagger` for drift becomes a Roslyn scan of `[AllowAnonymous]` actions that hit `_db` — anything without a `[PreAuthCallSite]` attribute fails the build.

**3. Logs become self-documenting.** "Background job refused: no user declared" is replaced by "Background job refused. Context was: `Background(reason: "Hangfire job 'SendDigestEmail' did not call BackgroundJobScope.RunAsync")`." The exception message names which case fired.

**4. Tests fail at the cause, not the symptom.** A test that forgets to bind a user gets `Uninitialized`. The first DB read raises `UserContextRequiredException(expected: "Resolved", actual: "Uninitialized")` at the service-layer boundary, not zero rows at the assertion site. Stage 7.5 lost ~30 minutes of diagnosis time per test-state-pollution cycle for exactly this reason.

## Alternatives considered

**Status quo — keep `Guid.Empty` overload.** Rejected: the Stage 7.5 implementation note in ADR-0068 already documents three corrections that flowed from this overload, and Stage 8 adds two new pre-auth routes which would each require manual `IPreAuthCallSiteTagger` updates. The cost of carrying the overload through the remaining Phase 3 stages is higher than the one-time rewrite.

**Tagged enum `(Guid UserId, UserState State)`.** Rejected: enums don't enforce that `UserId` is meaningful only when `State == Resolved`. A caller can still read `UserId` from an `Uninitialized` context and get `Guid.Empty`, reintroducing the original overload.

**Nullable `Guid?`.** Rejected: collapses Background, PreAuth, and Uninitialized into the same `null` case. Solves the model-creation problem but not the diagnostic problem.

**`Result<Guid, NoUserResolved>` (railway-oriented).** Rejected: introduces a new error-monad abstraction for one use case. The cost of the pattern is higher than the cost of an exception for this codebase's style.

## Consequences

- New file `ProjectCeres/Common/UserContext.cs` defines the type.
- `ICurrentUserAccessor` gains `UserContext Context { get; }`; `Guid UserId` remains as a derived convenience accessor.
- `HttpContextCurrentUserAccessor` rewritten to construct the right `UserContext` case (Resolved on cookie hit; PreAuth on `[PreAuthCallSite]` attribute on the current endpoint; Background when `IUserScope.Current` is set without an HTTP context; Uninitialized otherwise).
- New `[PreAuthCallSiteAttribute(string name)]` is added; the five known pre-auth actions are decorated; `IPreAuthCallSiteTagger` interface + `PreAuthCallSiteTagger` class are deleted.
- `RowLevelSecurityInterceptor` rewritten to switch on `Context` instead of consulting the tagger.
- `BackgroundJobScope.RunAsync` exception message includes the rejected context's case.
- `UserContextRequiredException` added; service-layer helper `ICurrentUserAccessor.Require()` extension throws when context is not `Resolved`.
- New architecture test: every `[AllowAnonymous]` action whose body touches `_db` carries a `[PreAuthCallSite]` attribute. Replaces the existing tagger-keys allow-list.
- All consumers of `_currentUser.UserId` that need a real user (not just the comparison value for the global filter) migrate to `_currentUser.Require().UserId`. The migration is mechanical; the architecture test for `IgnoreQueryFilters()` is the model.
- Tests: any test fixture that currently binds `FakeCurrentUserAccessor(Guid.Empty)` and expects it to mean "no user" must change to `FakeCurrentUserAccessor(UserContext.Uninitialized.Instance)`. The `FakeCurrentUserAccessor` test double in Stage 7 takes a `Guid` parameter; it becomes a thin wrapper around a `UserContext`.
- Risk: ~200 LOC ripple across `ICurrentUserAccessor` consumers (every service, the WAF, every test fixture). Mitigated by the convenience `UserId` accessor — most call sites don't need to change.
- ADR-0067's amendment line in `multi-tenancy-strategy.md` and `planning-resolved.md` is rewritten (not deleted; annotated) to reflect that `Guid.Empty` is now an internal compatibility accessor on a typed context, not the contract.

## Cross-references

- ADR-0065 — EF Core Global Query Filters with Explicit Redundancy and Admin-Only Bypass.
- ADR-0067 — Background-process user resolution with `IUserScope` and runner (the amendment line is what this ADR formalises).
- ADR-0068 — PostgreSQL Row-Level Security (the Stage 7.5 implementation that surfaced the diagnostic gap this ADR closes).
- `docs/multi-tenancy-strategy.md` § Background processes and non-HTTP contexts.
- `docs/roadmap-phase-three.md` § Stage 7.6 — the implementation stage that ships this ADR.
- Stage 7.6 brainstorm spec: `docs/superpowers/specs/2026-05-14-stage-7-6-error-legibility-and-ocp-cleanup.md`.
