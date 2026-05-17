---
name: verify-against-codebase
description: >
  Router skill. The pre-flight audit was split into `verify-backend` (ASP.NET / EF Core
  conventions) and `verify-frontend` (React / shadcn / design-system conventions) so a
  `.tsx`-only diff doesn't pay for a backend audit and vice versa. This entry stays as
  a router so existing transcripts, hooks, and memories that reference the old name still
  resolve. Use when reviewing a plan, spec, or piece of code BEFORE dispatching it to
  subagents or committing — the router routes to one or both siblings based on the
  artifact's domain. Triggers on phrases like "verify this plan", "check this for
  project conventions", "audit before dispatch", "is this consistent with the codebase".
---

# verify-against-codebase (router)

This skill was split into two siblings on 2026-05-17:

- **`verify-backend`** — for artifacts under `ProjectCeres/`, or any `.cs` / `.csproj` / ASP.NET / EF Core code, or plans whose deliverable is server-side.
- **`verify-frontend`** — for artifacts under `ProjectCeres.Client/`, or any `.ts` / `.tsx` / shadcn / Tailwind / design-system code, or plans whose deliverable is client-side.

This file stays as a router so existing references keep resolving — the playbook gates (Phase B `pre-spec-write`, Phase F `pre-commit`) accept the legacy name `verify-against-codebase` AND either sibling as proof of audit.

## How to route

Read the artifact (plan, spec, or staged diff) and inspect its scope:

1. **Files touched** — if any tracked-extension file is named, sort by directory:
   - `ProjectCeres/`, `ProjectCeres.Tests/` (anything non-`.tsx`/`.ts`-only), `*.csproj`, `*.sln`, `*.cs` → **backend**
   - `ProjectCeres.Client/`, `*.tsx`, `*.ts` (in the client tree) → **frontend**
2. **Domain signals in the prose** — if no files are named (e.g. a spec under planning):
   - Mentions of HTTP status codes, EF migrations, ASP.NET pipeline, model fields, `Program.cs`, `DbContext`, controllers, services, the `docs/api-contract.md` doc → **backend**
   - Mentions of shadcn, base-ui, Radix, Tailwind, `<Badge>`, `<Numeric>`, `<StatTile>`, `docs/design-system.md`, React hooks, Vitest, Vite, the React client → **frontend**

**Dispatch rule:**

- **Only backend signals** → invoke `verify-backend`. Skip `verify-frontend`.
- **Only frontend signals** → invoke `verify-frontend`. Skip `verify-backend`.
- **Both, or ambiguous (e.g. an integration spec that crosses the boundary)** → invoke **both**.
- **Neither (pure doc / planning / process artifact)** → neither sibling applies; report `verify-against-codebase: not applicable — artifact has no code-convention scope.`

## What this router does NOT do

- Does not perform any audit itself. The work lives in the two siblings.
- Does not run tests, linters, or type-checkers (neither sibling does either — see `feedback_never_skip_tests_to_make_them_pass.md`'s tier rule for which test commands to run for which diff).

## Why split

The original single skill had 8 audit steps; steps §1 and §8 applied to both domains, but §2/§3/§4 were backend-only and §5/§6 were frontend-only. A `.tsx`-only diff that runs the unsplit audit reads three irrelevant sections and wastes attention. A `.cs`-only diff reads two irrelevant sections. The split makes the audit work proportional to the diff.

The legacy gate behavior (Phase B `pre-spec-write` requires verify before any new spec write; Phase F `pre-commit` requires verify before any code commit) is preserved: invoking the appropriate sibling — OR the legacy name as a router — satisfies the gate.
