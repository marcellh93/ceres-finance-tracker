# ADR-0025 — Error Handling: Typed Domain Exceptions and Global Middleware

**Status:** Accepted

**Context:**

Services need to signal three distinct categories of expected failure back to the controller:

- **Not found:** a requested entity does not exist (or, in Phase 3, belongs to another user)
- **Validation error:** the input is well-formed but fails a business rule (e.g. negative amount, wrong currency on a transfer, duplicate name)
- **Business rule violation:** a state-changing operation is not permitted given the current system state (e.g. deactivating a system category)

Several patterns were available:

- **Typed domain exceptions:** services throw; controllers catch specific exception types
- **`Result<T>` / discriminated union:** every method returns a result object with a success/failure variant
- **Nullable returns:** `null` means failure; the caller interprets absence as an error
- **Error codes / enums:** services return an enum indicating what went wrong

Unexpected errors (unhandled exceptions, database failures) are a separate concern and need a global handler so they do not propagate as unformatted stack traces to the browser.

## Decision

**Expected failures:** Services throw typed domain exceptions. The exception type communicates the category of failure. Controllers catch specific domain exception types and redirect with a user-facing error message via `TempData`. Services never return `null` to signal failure.

```
NotFoundException       → resource not found or not accessible
ValidationException     → business rule or input validation failure
```

**Unexpected failures:** A global exception handler is registered in `Program.cs` via `app.UseExceptionHandler("/Error")`. A dedicated `ErrorController` returns a plain error page in production. The developer exception page (`app.UseDeveloperExceptionPage()`) is used in development only. Unexpected exceptions bubble up to this handler — they are never caught silently.

## Consequences

**Positive:**
- Services are easy to unit test — domain exceptions are assertable with `Assert.Throws` or FluentAssertions `.Should().Throw<>()`
- Controllers stay thin — a `try/catch` block per action is all that is needed to handle expected failures
- Clear separation between expected business failures and unexpected system errors
- No ambiguity from nullable returns — a missing entity is explicit, not inferred from `null`

**Negative:**
- Exceptions as flow control are a code smell in some paradigms; accepted here because domain exceptions represent genuinely exceptional business states (not normal conditional logic)
- Every controller action that calls a service must catch the relevant domain exception types — this is a convention enforced by code review, not the compiler

**Related decisions:**
- ADR-0017: the service layer owns all business logic; controllers are HTTP-only and delegate to services
- ADR-0026: ViewModels carry input validation via Data Annotations and ModelState; domain exceptions handle business rule violations after input has already been validated
