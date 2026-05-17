---
name: verify-backend
description: >
  Use when reviewing a plan, spec, or piece of code that touches the ASP.NET / EF Core
  server (anything under ProjectCeres/, *.cs, *.csproj) BEFORE dispatching it to subagents
  or committing — to catch backend-side project-convention conflicts (wrong HTTP status
  codes, references to model fields that don't exist, error shapes that don't match the
  project's error factory, derived-column violations). Triggers on phrases like
  "verify this plan", "check this for project conventions", "audit before dispatch",
  "is this consistent with the codebase" — when the artifact is backend-scoped. Run BEFORE
  the work happens — corrections are cheapest before commitment.
---

# verify-backend

This skill is the backend half of the verify-against-codebase split. For frontend artifacts use `verify-frontend`. For mixed artifacts invoke both. The legacy `verify-against-codebase` skill is a router that dispatches to one or both based on the diff.

## Why this skill exists

A plan or spec written from generic defaults will drift from a project's actual server-side conventions. Examples this project has hit:

- ASP.NET `Program.cs` configures `InvalidModelStateResponseFactory` to return **422 Unprocessable Entity**, not 400 — but plans written from defaults assume 400.
- The `Account` model is intentionally minimal (no `OpeningBalance`, `OpeningBalanceDate`, `CreatedAt` fields per the project's "no derived columns" rule) — but plans assume those fields exist because they're common in finance apps.
- MFA Stage 6b.1 first draft proposed a homegrown `MfaTicketService` that duplicated ASP.NET Identity's built-in `Identity.TwoFactorUserId` cookie — caught only because the audit forced a read of `Program.cs` and the Identity registration.

Each of these caused implementer rework. Catching them before dispatch saves a round trip.

## When to use

Run before:
- Committing to a written plan that touches ASP.NET / EF Core code (after `writing-plans`, before `subagent-driven-development`).
- Dispatching a long-running subagent at backend work.
- Committing a non-trivial server-side change.

Run also when:
- The user says "verify this", "audit this against the codebase", "check this for project conventions" — and the artifact is backend-scoped.
- A subagent reports `DONE_WITH_CONCERNS` flagging a possible server-side convention mismatch.

Do NOT run for:
- Trivial typo fixes.
- Pure documentation updates.
- Pure frontend artifacts — use `verify-frontend` instead.
- Code that already passed a similar check earlier in the session and hasn't changed.

## The 6-step pre-flight (backend)

For each artifact you're verifying, read the following in order.

### 1. Project rules — `CLAUDE.md`

Skim every bullet. The "What NOT to Do" section, "Architecture Rules (Non-Obvious)" section, and the "After Completing Any Stage" rule are load-bearing. If the artifact violates any of them, that's a stop-the-line finding.

### 2. API conventions — `docs/api-contract.md`

Confirm:
- Response wrapper shape (DTO vs raw array).
- Success status codes (200 vs 201 with Location).
- Error status codes (**this project: 422 with `ValidationProblemDetails` for ModelState failures**, NOT 400).
- camelCase JSON keys (ASP.NET default).

### 3. ASP.NET pipeline — `ProjectCeres/Program.cs`

Confirm:
- `InvalidModelStateResponseFactory` is set (returns **422**).
- This means: a manual `return ValidationProblem(ModelState)` returns **400** because it bypasses the factory. Use `return UnprocessableEntity(...)` directly with the same JSON shape if you need to add cross-field errors after the auto-validation step.
- DI registrations match what the artifact assumes.

### 4. Affected models — `ProjectCeres/Models/<Name>.cs`

For every model the artifact references, read the actual class. Verify:
- Field names exist exactly as written.
- Field nullability matches.
- Required props for entity creation are all set in seed/test helpers.
- Per project rules, derived values (`Account` balance, `Budget` actual spend, net worth) are NOT stored as columns — don't try to set them.

Common surprise: `Account` has `Id`, `Name`, `AccountTypeId`, `CurrencyId`, `Description?`, `IsActive`, `ExcludeFromSpendable`, `LiabilityRepaymentType?`, `InterestRate?` — and **nothing else**. No `OpeningBalance`, no `OpeningBalanceDate`, no `CreatedAt`.

### 5. Razor vs SPA boundary

Confirm:
- New Razor changes do not reference SPA-only patterns (React hooks in a .cshtml file, `useApi` outside a React tree).
- The migration boundary in `docs/planning-phase3-spa-migration.md` is respected — don't modify a Razor controller for a feature whose SPA migration is in flight.

### 6. Active session memory & open questions

Skim the most recent commits (`git log --oneline -20`) and check `docs/planning-resolved.md` for decisions that override earlier docs. A doc may say one thing but planning-resolved settled it differently.

## Output format

Report a short list:

```
verify-backend: N conventions verified.
   M conflicts found:

   1. [file:line in artifact] Asserts 400; project returns 422.
      → Change to HttpStatusCode.UnprocessableEntity.
   2. [file:line in artifact] Sets Account.OpeningBalance; field doesn't exist on the model.
      → Drop. Account has only Id/Name/AccountTypeId/CurrencyId/IsActive + a few optional fields.
```

If there are no conflicts:

```
verify-backend: clean. Safe to dispatch.
```

## Red flags — STOP before dispatching

- Any HTTP status code in the artifact that isn't documented in `docs/api-contract.md`.
- Any reference to a model field you didn't verify by reading the model class.
- A plan that says "Returns 400 with ValidationProblemDetails" — that combo doesn't exist in this project.
- An artifact that sets a derived value (`Account.Balance`, `Budget.ActualSpend`, net worth) as a column.

If any red flag is present, fix the artifact before dispatching. Catching it pre-dispatch costs minutes; catching it mid-execution costs a subagent round trip plus rework.

## What this skill does NOT do

- Does not run tests. Run `dotnet test` separately, scoped per `feedback_never_skip_tests_to_make_them_pass.md`'s tier rule (TIER B for `.cs`-only diffs).
- Does not lint or type-check.
- Does not audit frontend artifacts — use `verify-frontend`.
- Does not validate spec completeness — that's the spec-reviewer subagent's job.

This skill is a backend-convention pre-flight, nothing more.
