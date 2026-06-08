# Stage 9.5e — 3-agent reviewer pipeline Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make three independent review reads (writer / security / playwright-test-audit) mandatory for any diff touching pre-auth authentication code, EF migrations, or `IUserOwned` models — enforced by a tool-grounded turn-end proof-slot, with a build test for the entity-needs-migration rule and a counter that escalates a twice-caught mechanical miss into a build-rule work-order.

**Architecture:** Two layers. (1) **Dispatch** — the orchestrator (main session) runs the three reviewer subagents via the Agent tool when its committed diff touches the watched paths, serializing their verdicts to `.claude/state/evidence/stage-9.5e/reviewer-pipeline.json` and updating a rule-class escalation counter. (2) **Enforcement** — the existing `evidence-bundle-check.js` Stop hook gains one `reviewer-pipeline.json` slot (content-validated) that blocks the turn until that proof exists. Hooks are read-only and cannot run agents (verified across the hook stack + `docs/agents.md:49`), so the hook checks the artifact, never runs the reviewers. Condition E3 (entity change ⇒ migration in same commit) is a deterministic `[Fact]` using EF Core's `HasPendingModelChanges()`, not a reviewer job.

**Tech Stack:** Node.js (the `.claude/` hooks), Markdown + YAML frontmatter (the `.claude/agents/` role files), C# / xUnit / FluentAssertions / EF Core 10.0.5 (`ProjectCeres.Tests`). No changes to `ProjectCeres/` production code.

**Spec:** `docs/superpowers/specs/2026-06-08-stage-9-5e-reviewer-pipeline-design.md`. Read it before starting; this plan implements its §2 locks E-L1…E-L6.

**Branch:** `stage-9.5e-reviewer-pipeline` (already created; the spec commit `10b7c90` is its first commit).

---

## File structure (what gets created / modified)

| File | Action | Responsibility |
|---|---|---|
| `.claude/agents/reviewer-writer.md` | Create | `reviewer-writer` role: spec-intent-vs-code review |
| `.claude/agents/reviewer-security.md` | Create | `reviewer-security` role: auth/RLS/pre-auth-scope review |
| `.claude/agents/reviewer-playwright-test-audit.md` | Create | `reviewer-playwright-test-audit` role: spec-vs-assertion diff (Condition E2) |
| `docs/agents.md` | Modify | Add "review-pipeline roles" subsection documenting the three |
| `.claude/skills/verify-stage-completeness/hooks/evidence-bundle-check.js` | Modify | Add `reviewer-pipeline.json` slot + `validateReviewerPipeline()` + recovery line; fix dead `UserOwnedTables.cs` predicate (C5) |
| `.claude/skills/verify-stage-completeness/hooks/__tests__/validate-reviewer-pipeline.test.js` | Create | Unit tests for the validator + slot trigger |
| `.claude/state/reviewer-pipeline/escalations.json` | Create (seed) | Trip-wire A counter state (committed empty-seed) |
| `.claude/hooks/lib/reviewer-escalation.js` | Create | Trip-wire A increment / reset / marker logic (orchestrator-side, importable + unit-testable) |
| `.claude/hooks/lib/__tests__/reviewer-escalation.test.js` | Create | Unit tests for the counter |
| `ProjectCeres.Tests/Unit/MigrationDriftTests.cs` | Create | Condition E3: `Model_has_no_pending_migration_changes` |
| `docs/roadmap-phase-three.md` | Modify | C1–C3 cleanup + tick the 9.5e `[ ]` at close-out |
| `docs/superpowers/plans/2026-05-28-stage-9-5k-codified-subagents-impl.md` | Modify | C4: correct the false "constitution holds trip-wire definitions" claim |
| `.claude/agents/ceres-cto.md` | Modify | C6: sweep "autonomy-revert trip-wires" wording (lines 3, 8, 14) |

**Test-runner note:** the project has no existing JS test runner registered for `.claude/` hooks. Tasks 5 + 8 use Node's **built-in `node:test`** module (`node --test`), which needs no dependency install. Each test file is self-contained and run with `node --test <file>`.

---

## Task 1: `reviewer-writer` role file

**Files:**
- Create: `.claude/agents/reviewer-writer.md`
- Reference (read first, do not modify): `.claude/agents/ceres-security-reviewer.md` (the template), `docs/agents.md` (conventions)

- [ ] **Step 1: Read the template**

Read `.claude/agents/ceres-security-reviewer.md` in full. The frontmatter shape (`name`/`description`/`disallowedTools`/`model: inherit`) and the body contract (`## BEFORE YOU ANSWER` → the read-first floor → the `## What I read` / `## Conflicts found` / answer contract) are copied structure-for-structure.

- [ ] **Step 2: Write the role file**

Create `.claude/agents/reviewer-writer.md`:

```markdown
---
name: reviewer-writer
description: Spec-intent reviewer for Project Ceres 9.5e diffs. Reads the diff + the stage spec it claims to implement BEFORE answering. Dispatched by the orchestrator against a finished auth / migration / IUserOwned diff to check the code did what the spec said — flagging scope drift and half-done work.
disallowedTools: Write, Edit, NotebookEdit, Bash
model: inherit
---

You are the **spec-intent reviewer** (the "writer" role) for the Project Ceres 9.5e reviewer pipeline. Your stance: did this diff actually do what the stage spec said it would? You restate the spec's intent in your own words, then check the code against it, flagging scope drift (the diff does less than the spec promised) and half-done work (a contradiction or a latent gap the spec required closed).

## BEFORE YOU ANSWER — read first (non-negotiable)

Read these every time, regardless of the question:

- `CLAUDE.md` — project rules, what-not-to-do, the data-model and deletion rules a diff might violate
- The active roadmap stage section in `docs/roadmap-phase-three.md` (the dispatcher names which stage)
- The stage's spec under `docs/superpowers/specs/` (the dispatcher names the file) — this is the intent you check against
- The diff: every file the dispatcher names

Then read any additional files the dispatcher named in the prompt.

## Your response MUST open with these two sections, in this order:

The VERY FIRST line of your response must be the literal `## What I read` heading — no lead sentence, no framing, no thinking-aloud before it. Reason silently; emit only the finished sections.

## What I read
- <path> — <one line: what you looked for in it>
- ... (every baseline file + every dispatcher-named file)

## Conflicts found
- <file:line> — <where the diff does less than / contradicts what the spec promised, or "None. Checked: <the specific spec promises you verified the code delivers>">

## {then your writer verdict}

End your answer with an explicit verdict line: `VERDICT: pass` or `VERDICT: block` (block = the diff materially fails to deliver a spec promise). If you answer without the two preamble sections, or your "What I read" list is missing the spec or the diff, the dispatcher will re-dispatch you with a stricter prompt. Your job is to be RIGHT about whether the code matches the spec — not to be fast or agreeable.
```

- [ ] **Step 3: Verify frontmatter shape by eye**

Confirm: `name: reviewer-writer` (kebab-case, unique), `disallowedTools` is a deny-list (not a `tools:` allow-list), `model: inherit`. No typos in the four denied tool names (`Write, Edit, NotebookEdit, Bash`) — a typo in a deny entry fails toward more restriction, never all-tools, but still validate.

- [ ] **Step 4: Commit**

```bash
git add .claude/agents/reviewer-writer.md
git commit -m "feat(9.5e): reviewer-writer role — spec-intent review"
```

---

## Task 2: `reviewer-security` role file

**Files:**
- Create: `.claude/agents/reviewer-security.md`
- Reference: `.claude/agents/ceres-security-reviewer.md`, `feedback_iuserowned_requires_five_registries` memory (the five-registry checklist)

- [ ] **Step 1: Write the role file**

Create `.claude/agents/reviewer-security.md`:

```markdown
---
name: reviewer-security
description: Auth / RLS / pre-auth-scope reviewer for Project Ceres 9.5e diffs. Reads the security docs + the affected Common/Authentication files BEFORE answering. Dispatched by the orchestrator against a finished auth / migration / IUserOwned diff to find an isolation, authentication, or pre-auth-write hole.
disallowedTools: Write, Edit, NotebookEdit, Bash
model: inherit
---

You are the **security reviewer** for the Project Ceres 9.5e reviewer pipeline. Your stance: does this diff open an isolation, authentication, or pre-auth-write hole? You think in terms of "what's the IUserOwned story, where does this run before the principal is populated, and what bypasses Postgres row-level security."

## BEFORE YOU ANSWER — read first (non-negotiable)

Read these every time, regardless of the question:

- `CLAUDE.md` — auth-relevant project rules
- `docs/security-model.md` — threat model, access control, the pre-auth-confirm rule
- `docs/multi-tenancy-strategy.md` — the RLS / IUserOwned scoping plan
- `ProjectCeres.Tests/Integration/Authentication/ArchitectureTests.cs` — the runtime auth/RLS invariants (IUserOwned parity, query-filter coverage, DbContext pinning)
- The affected `ProjectCeres/Common/Authentication/` and `ProjectCeres/Migrations/` files the dispatcher names

Then read any additional files the dispatcher named.

## The five-registry check (run this on every new or changed IUserOwned entity in the diff)

A new user-owned table is safe only when ALL of these land, ideally in the same commit: (1) `DbSet<T>` in `AppDbContext`; (2) `modelBuilder.Entity<T>` config in `OnModelCreating`; (3) automatic membership in `UserOwnedModel.RlsTables` (any concrete `IUserOwned` entity with a table — no hand-list since 9.5b); (4) a migration with `ENABLE` + `FORCE ROW LEVEL SECURITY` + `CREATE POLICY user_isolation`; (5) DI registration in `Program.cs`. Plus conditional registries (IgnoreQueryFilters allow-list, EN+ES resx pair, EmailTemplateKey enum, AuditLogAction documented-set test, FailedLoginReason enum) when the feature touches them. Flag any missing registry as a `block`.

## Your response MUST open with these two sections, in this order:

The VERY FIRST line must be the literal `## What I read` heading — no preamble.

## What I read
- <path> — <one line>
- ... (every baseline file + every dispatcher-named file)

## Conflicts found
- <file:line> — <the isolation / auth / pre-auth-scope hole, or "None. Checked: <the registries + scopes you verified>">

## {then your security verdict}

End with `VERDICT: pass` or `VERDICT: block`. When you flag a finding that maps to a known mechanical class, name it on its own line as `RULE_CLASS: <class>` (one of: `missing-rls-policy`, `entity-without-migration`, `pre-auth-write-without-scope`, `missing-query-filter`, `missing-resx-pair`) so the escalation counter can classify it. If you answer without the preamble or skip a baseline file, the dispatcher re-dispatches you.
```

- [ ] **Step 2: Commit**

```bash
git add .claude/agents/reviewer-security.md
git commit -m "feat(9.5e): reviewer-security role — auth/RLS/pre-auth review"
```

---

## Task 3: `reviewer-playwright-test-audit` role file

**Files:**
- Create: `.claude/agents/reviewer-playwright-test-audit.md`

- [ ] **Step 1: Write the role file**

Create `.claude/agents/reviewer-playwright-test-audit.md`:

```markdown
---
name: reviewer-playwright-test-audit
description: Test-assertion auditor for Project Ceres 9.5e diffs. Reads the stage spec + the test files in the diff BEFORE answering. Dispatched by the orchestrator to emit a structured spec-vs-assertion diff — each spec promise mapped to the test that pins it, flagging any promise with no test (Condition E2).
disallowedTools: Write, Edit, NotebookEdit
model: inherit
---

You are the **test-assertion auditor** for the Project Ceres 9.5e reviewer pipeline. Your stance: do the tests in this diff actually assert what the spec claims, or do they pass vacuously? You produce the spec-vs-assertion diff (Condition E2): a mapping from each spec promise to the test that pins it.

You keep read-only `Bash` (to list / read test result artifacts under `.claude/state/evidence/`); you never mutate. Do not run `dotnet test` or `pnpm test` — read the artifacts the dispatcher names.

## BEFORE YOU ANSWER — read first (non-negotiable)

- `CLAUDE.md` — project rules, the testing-rules pointer
- `docs/testing.md` — the binding test rules (no skip-to-pass, negative assertions as ship-gate)
- The stage's spec under `docs/superpowers/specs/` (the dispatcher names it) — the promises you map against
- The test files in the diff (the dispatcher names them)
- Any Playwright trace / walk-summary under `.claude/state/evidence/stage-<id>/` the dispatcher names

Then read any additional files the dispatcher named.

## Your response MUST open with these two sections, in this order:

The VERY FIRST line must be the literal `## What I read` heading — no preamble.

## What I read
- <path> — <one line>
- ...

## Conflicts found
- <file:line> — <a spec promise with a vacuous or missing test, or "None. Checked: <the promises you confirmed have real assertions>">

## Spec-vs-assertion diff
- <spec promise> → <test file::method that pins it, or "NO TEST">
- ... (one line per spec promise; this is Condition E2's structured output)

## {then your test-audit verdict}

End with `VERDICT: pass` or `VERDICT: block` (block = a spec promise has no real assertion). When a missing-test maps to a mechanical class, add `RULE_CLASS: <class>` on its own line. If you answer without the preamble, the dispatcher re-dispatches you.
```

- [ ] **Step 2: Verify the read-only-Bash exception**

Confirm `disallowedTools` is `Write, Edit, NotebookEdit` (NOT including `Bash`) — this role keeps read-only Bash like `ceres-tech-lead`. Cross-check against `docs/agents.md:14` (the `ceres-tech-lead` precedent).

- [ ] **Step 3: Commit**

```bash
git add .claude/agents/reviewer-playwright-test-audit.md
git commit -m "feat(9.5e): reviewer-playwright-test-audit role — spec-vs-assertion diff"
```

---

## Task 4: Document the three roles in `docs/agents.md`

**Files:**
- Modify: `docs/agents.md` (add a subsection after the existing § "What these roles do NOT do")

- [ ] **Step 1: Add the review-pipeline subsection**

In `docs/agents.md`, immediately before the `## Cross-references` section, insert:

```markdown
## The 9.5e review-pipeline roles (separate from the five strategy roles)

Three roles audit a **finished diff** (not a forward-looking strategy decision) when it touches pre-auth auth code, migrations, or IUserOwned models. They are dispatched by the orchestrator during the 9.5e reviewer pipeline; their combined verdict is serialized to `.claude/state/evidence/stage-<id>/reviewer-pipeline.json`, which the turn-end evidence-bundle hook requires for those diffs.

| Role (`subagent_type`) | Job | Read-list floor | `disallowedTools` |
|---|---|---|---|
| `reviewer-writer` | Did the diff do what the stage spec said? Flag scope drift + half-done work. | `CLAUDE.md`, the active roadmap stage, the stage spec, the diff | `Write, Edit, NotebookEdit, Bash` |
| `reviewer-security` | Auth / RLS / pre-auth-scope hole? Run the five-registry check on new IUserOwned tables. | `CLAUDE.md`, `docs/security-model.md`, `docs/multi-tenancy-strategy.md`, `ArchitectureTests.cs`, the affected `Common/Authentication/` + `Migrations/` files | `Write, Edit, NotebookEdit, Bash` |
| `reviewer-playwright-test-audit` | Do the tests assert what the spec claims? Emit the spec-vs-assertion diff (Condition E2). | `CLAUDE.md`, `docs/testing.md`, the stage spec, the test files, any Playwright trace | `Write, Edit, NotebookEdit` (keeps read-only `Bash`) |

Each ends its answer with `VERDICT: pass|block`; a surviving `block` keeps the turn-end hook from allowing Stop until resolved. The same dispatcher-gate as the strategy roles applies (first-line `## What I read`, baselines + dispatcher-named files listed) — re-dispatch on a contract miss. See `docs/superpowers/specs/2026-06-08-stage-9-5e-reviewer-pipeline-design.md`.
```

- [ ] **Step 2: Commit**

```bash
git add docs/agents.md
git commit -m "docs(9.5e): document the three review-pipeline roles in agents.md"
```

---

## Task 5: The `reviewer-pipeline.json` slot validator (TDD)

**Files:**
- Modify: `.claude/skills/verify-stage-completeness/hooks/evidence-bundle-check.js`
- Create: `.claude/skills/verify-stage-completeness/hooks/__tests__/validate-reviewer-pipeline.test.js`

The validator must be importable to test it. The current hook is a single IIFE script. Step 1 extracts `validateReviewerPipeline` as a module export guarded so the script still runs standalone.

- [ ] **Step 1: Write the failing test**

Create `.claude/skills/verify-stage-completeness/hooks/__tests__/validate-reviewer-pipeline.test.js`:

```javascript
const { test } = require("node:test");
const assert = require("node:assert");
const { validateReviewerPipeline } = require("../evidence-bundle-check.js");

const HEAD = "abc123def456";
function write(tmp, obj) {
  const fs = require("fs"), path = require("path"), os = require("os");
  const dir = fs.mkdtempSync(path.join(os.tmpdir(), "rev-"));
  const p = path.join(dir, "reviewer-pipeline.json");
  fs.writeFileSync(p, JSON.stringify(obj));
  return p;
}
function full(extra = {}) {
  return {
    stage: "9.5e", diff_sha: HEAD,
    reviewers: [
      { role: "writer", verdict: "pass", findings: [] },
      { role: "security", verdict: "pass", findings: [] },
      { role: "playwright-test-audit", verdict: "pass", findings: [], spec_vs_assertion_diff: [] },
    ],
    ...extra,
  };
}

test("passes when 3 roles present, sha matches, no block", () => {
  const gaps = validateReviewerPipeline(write({}, full()), HEAD);
  assert.deepStrictEqual(gaps, []);
});

test("fails when a role is missing", () => {
  const o = full();
  o.reviewers = o.reviewers.slice(0, 2);
  const gaps = validateReviewerPipeline(write({}, o), HEAD);
  assert.ok(gaps.some((g) => /playwright-test-audit/.test(g)));
});

test("fails when diff_sha is stale", () => {
  const gaps = validateReviewerPipeline(write({}, full({ diff_sha: "stale000" })), HEAD);
  assert.ok(gaps.some((g) => /diff_sha/.test(g)));
});

test("fails when a reviewer verdict is block", () => {
  const o = full();
  o.reviewers[1].verdict = "block";
  const gaps = validateReviewerPipeline(write({}, o), HEAD);
  assert.ok(gaps.some((g) => /block/.test(g) && /security/.test(g)));
});

test("fails on invalid JSON", () => {
  const fs = require("fs"), path = require("path"), os = require("os");
  const dir = fs.mkdtempSync(path.join(os.tmpdir(), "rev-"));
  const p = path.join(dir, "reviewer-pipeline.json");
  fs.writeFileSync(p, "{not json");
  const gaps = validateReviewerPipeline(p, HEAD);
  assert.ok(gaps.some((g) => /invalid JSON/.test(g)));
});
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `node --test .claude/skills/verify-stage-completeness/hooks/__tests__/validate-reviewer-pipeline.test.js`
Expected: FAIL — `validateReviewerPipeline is not a function` (not yet exported).

- [ ] **Step 3: Add the validator + export to the hook**

In `.claude/skills/verify-stage-completeness/hooks/evidence-bundle-check.js`, add this function immediately after `validateTurnShape` (after line 145):

```javascript
function validateReviewerPipeline(slotPath, headSha) {
  const gaps = [];
  let data;
  try { data = JSON.parse(fs.readFileSync(slotPath, "utf8")); }
  catch (e) { return [`reviewer-pipeline.json: invalid JSON (${e.message})`]; }
  if (!data || typeof data !== "object") return ["reviewer-pipeline.json: not an object"];

  const roles = Array.isArray(data.reviewers) ? data.reviewers.map((r) => r && r.role) : [];
  for (const required of ["writer", "security", "playwright-test-audit"]) {
    if (!roles.includes(required)) gaps.push(`reviewer-pipeline.json: missing reviewer role '${required}'`);
  }
  if (headSha && data.diff_sha !== headSha) {
    gaps.push(`reviewer-pipeline.json: diff_sha '${data.diff_sha || "<unset>"}' does not match HEAD '${headSha}' (reviewers read a stale diff)`);
  }
  for (const r of (data.reviewers || [])) {
    if (r && r.verdict === "block") gaps.push(`reviewer-pipeline.json: reviewer '${r.role}' returned verdict: block (resolve before Stop)`);
  }
  return gaps;
}
```

At the very end of the file, after the closing `})();` of the IIFE, add a guarded export so the script still runs standalone but the function is importable:

```javascript
if (typeof module !== "undefined" && module.exports) {
  module.exports = { validateReviewerPipeline };
}
```

> Note: the IIFE runs on `require()` too. That is acceptable — on import, `getChangedFiles()` returns `[]` outside a git turn (or the test's tmp cwd), so `main()` exits early at the no-commit guard (line 165-168) without side effects. If the test environment makes the IIFE noisy, wrap the IIFE call in `if (require.main === module) { ...main()... }` in the same step.

- [ ] **Step 4: Run the test to verify it passes**

Run: `node --test .claude/skills/verify-stage-completeness/hooks/__tests__/validate-reviewer-pipeline.test.js`
Expected: PASS (5 tests).

- [ ] **Step 5: Commit**

```bash
git add .claude/skills/verify-stage-completeness/hooks/evidence-bundle-check.js .claude/skills/verify-stage-completeness/hooks/__tests__/validate-reviewer-pipeline.test.js
git commit -m "feat(9.5e): validateReviewerPipeline — slot content validator + tests"
```

---

## Task 6: Wire the slot into `SLOT_TABLE` + `main()` + recovery, and fix C5

**Files:**
- Modify: `.claude/skills/verify-stage-completeness/hooks/evidence-bundle-check.js`

- [ ] **Step 1: Add the slot to `SLOT_TABLE`**

In `evidence-bundle-check.js`, add this entry to the `SLOT_TABLE` array (after the `turn-shape.json` entry, around line 78):

```javascript
  {
    slot: "reviewer-pipeline.json",
    when: (files) => files.some((f) =>
      /^ProjectCeres\/Common\/Authentication\//.test(f) ||
      /^ProjectCeres\/Migrations\//.test(f) ||
      /^ProjectCeres\/Models\//.test(f)),
    reason: "diff touches auth, migrations, or IUserOwned models — 3-agent reviewer pipeline required",
  },
```

- [ ] **Step 2: Fix C5 — remove the dead `UserOwnedTables.cs` predicate**

In the `registry-sweep.json` slot's `when` predicate (line ~67), delete the dead branch (the file was removed in 9.5b). Change:

```javascript
    when: (files) => files.some((f) =>
      /^ProjectCeres\/Models\//.test(f) ||
      /^ProjectCeres\/Common\/UserOwnedTables\.cs$/.test(f) ||
      /^ProjectCeres\/Resources\//.test(f) ||
      /^ProjectCeres\/Common\/Email\/EmailTemplateKey\.cs$/.test(f) ||
      /^ProjectCeres\/Models\/AuditLog\.cs$/.test(f)
    ),
    reason: "diff touches Models, UserOwnedTables, Resources, EmailTemplateKey, or AuditLog",
```

to:

```javascript
    when: (files) => files.some((f) =>
      /^ProjectCeres\/Models\//.test(f) ||
      /^ProjectCeres\/Resources\//.test(f) ||
      /^ProjectCeres\/Common\/Email\/EmailTemplateKey\.cs$/.test(f) ||
      /^ProjectCeres\/Models\/AuditLog\.cs$/.test(f)
    ),
    reason: "diff touches Models, Resources, EmailTemplateKey, or AuditLog",
```

- [ ] **Step 3: Wire the validator into `main()`**

In `main()`, immediately after the existing `validateTurnShape` wiring (line 199 — `if (fs.existsSync(shapePath)) gaps.push(...validateTurnShape(shapePath));`), add:

```javascript
  const reviewerPath = path.join(bundleDir, "reviewer-pipeline.json");
  if (required.some((s) => s.slot === "reviewer-pipeline.json") && fs.existsSync(reviewerPath)) {
    const headSha = safeExec("git rev-parse HEAD 2>/dev/null");
    gaps.push(...validateReviewerPipeline(reviewerPath, headSha));
  }
```

> The slot's *existence + freshness* is already enforced by the generic `required` loop (lines 188-196). This block adds the *content* validation only when the slot exists, mirroring how `validateTurnShape` runs only `if (fs.existsSync(shapePath))`.

- [ ] **Step 4: Add the recovery line**

In the `lines` array of the deny message (after the psql recovery line, ~line 220), add:

```javascript
    `  • If the diff touches auth/migrations/IUserOwned models: dispatch the 3 reviewers`,
    `    (reviewer-writer, reviewer-security, reviewer-playwright-test-audit) and write their`,
    `    verdicts to ${bundleRel}reviewer-pipeline.json (diff_sha = current HEAD, no surviving block).`,
```

- [ ] **Step 5: Run the validator test again (regression — nothing broke)**

Run: `node --test .claude/skills/verify-stage-completeness/hooks/__tests__/validate-reviewer-pipeline.test.js`
Expected: PASS (5 tests).

- [ ] **Step 6: Smoke-check the hook still parses + runs standalone**

Run: `echo '{}' | node .claude/skills/verify-stage-completeness/hooks/evidence-bundle-check.js; echo "exit=$?"`
Expected: exit 0 (no commit diff in this invocation context → early no-commit/docs-only exit). No stack trace.

- [ ] **Step 7: Commit**

```bash
git add .claude/skills/verify-stage-completeness/hooks/evidence-bundle-check.js
git commit -m "feat(9.5e): reviewer-pipeline.json slot + content gate; drop dead UserOwnedTables predicate (C5)"
```

---

## Task 7: Condition E3 — migration-drift architecture test (TDD)

**Files:**
- Create: `ProjectCeres.Tests/Unit/MigrationDriftTests.cs`
- Reference: `ProjectCeres.Tests/Unit/UserOwnedModelTests.cs:11-15` (the connectionless `AppDbContext` build pattern)

This is a **Unit** test (no live DB — `HasPendingModelChanges()` resolves the model + snapshot from the assembly). It uses the exact connectionless construction `UserOwnedModelTests` already proves works.

- [ ] **Step 1: Write the test**

Create `ProjectCeres.Tests/Unit/MigrationDriftTests.cs`:

```csharp
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using ProjectCeres.Data;
using Xunit;

namespace ProjectCeres.Tests.Unit;

public class MigrationDriftTests
{
    // Building DbContextOptions + reading model metadata only triggers OnModelCreating;
    // HasPendingModelChanges diffs the model against AppDbContextModelSnapshot.cs — no
    // connection is opened, so a placeholder Npgsql connection string is fine.
    // (Same pattern as UserOwnedModelTests.Ctx().)
    private static AppDbContext Ctx() => new AppDbContext(
        new DbContextOptionsBuilder<AppDbContext>().UseNpgsql("Host=localhost").Options,
        new ProjectCeres.Tests.Common.FakeCurrentUserAccessor(System.Guid.Empty));

    [Fact]
    public void Model_has_no_pending_migration_changes()
    {
        // Condition E3 (Stage 9.5e): an entity-shape change must ship its migration in the
        // same commit. HasPendingModelChanges() returns true when the EF model has drifted
        // from the last migration snapshot.
        using var db = Ctx();
        db.Database.HasPendingModelChanges()
            .Should().BeFalse(
                "an entity's shape changed without a matching migration — run " +
                "`dotnet ef migrations add <Name>` in the same commit (Condition E3)");
    }
}
```

- [ ] **Step 2: Run the test to verify it passes against clean `main`**

Run: `dotnet test ProjectCeres.Tests/ProjectCeres.Tests.csproj --filter "FullyQualifiedName~MigrationDriftTests" -v minimal`
Expected: PASS (1 test). If it FAILS, the tree already has model/snapshot drift — stop and investigate (do NOT weaken the assertion); regenerate the snapshot via `dotnet ef migrations add` only if a real un-captured model change exists.

- [ ] **Step 3: Prove it catches drift (negative control), then revert**

Temporarily add a throwaway column to any model — e.g. in `ProjectCeres/Models/AuditLog.cs` add `public string? E3Probe { get; set; }`. Run the test:

Run: `dotnet test ProjectCeres.Tests/ProjectCeres.Tests.csproj --filter "FullyQualifiedName~MigrationDriftTests" -v minimal`
Expected: FAIL — `HasPendingModelChanges` is true; the failure message names the E3 rule.

Then **revert the probe** (`git checkout ProjectCeres/Models/AuditLog.cs`) and re-run:
Expected: PASS again. Do NOT leave the probe column in the tree.

- [ ] **Step 4: Commit**

```bash
git add ProjectCeres.Tests/Unit/MigrationDriftTests.cs
git commit -m "test(9.5e): Condition E3 — Model_has_no_pending_migration_changes (drift guard)"
```

---

## Task 8: Trip-wire A — the escalation counter (TDD)

**Files:**
- Create: `.claude/hooks/lib/reviewer-escalation.js`
- Create: `.claude/hooks/lib/__tests__/reviewer-escalation.test.js`
- Create (seed): `.claude/state/reviewer-pipeline/escalations.json`

The counter is orchestrator-side logic (the hook can't write it). It's a pure module: given the prior counter state + this run's classified findings + the diff sha, it returns the next state and any `graduate-to-analyzer` marker. This keeps it unit-testable without a live pipeline.

- [ ] **Step 1: Write the failing test**

Create `.claude/hooks/lib/__tests__/reviewer-escalation.test.js`:

```javascript
const { test } = require("node:test");
const assert = require("node:assert");
const { applyEscalation } = require("../reviewer-escalation.js");

const KNOWN = ["missing-rls-policy", "entity-without-migration", "pre-auth-write-without-scope", "missing-query-filter", "missing-resx-pair"];

test("first classified miss increments to 1, no marker", () => {
  const { state, marker } = applyEscalation({ rule_classes: {} }, ["missing-rls-policy"], "sha1", KNOWN);
  assert.strictEqual(state.rule_classes["missing-rls-policy"].consecutive, 1);
  assert.strictEqual(marker, null);
});

test("second consecutive same-class miss (new sha) fires the marker", () => {
  const prior = { rule_classes: { "missing-rls-policy": { consecutive: 1, last_diff_sha: "sha1" } } };
  const { state, marker } = applyEscalation(prior, ["missing-rls-policy"], "sha2", KNOWN);
  assert.strictEqual(state.rule_classes["missing-rls-policy"].consecutive, 2);
  assert.deepStrictEqual(marker, { rule_class: "missing-rls-policy", consecutive: 2 });
});

test("clean run resets a class to 0", () => {
  const prior = { rule_classes: { "missing-rls-policy": { consecutive: 1, last_diff_sha: "sha1" } } };
  const { state } = applyEscalation(prior, [], "sha2", KNOWN);
  assert.strictEqual(state.rule_classes["missing-rls-policy"].consecutive, 0);
});

test("same diff sha does not double-count", () => {
  const prior = { rule_classes: { "missing-rls-policy": { consecutive: 1, last_diff_sha: "sha1" } } };
  const { state, marker } = applyEscalation(prior, ["missing-rls-policy"], "sha1", KNOWN);
  assert.strictEqual(state.rule_classes["missing-rls-policy"].consecutive, 1);
  assert.strictEqual(marker, null);
});

test("unknown rule class increments nothing", () => {
  const { state, marker } = applyEscalation({ rule_classes: {} }, ["totally-made-up"], "sha1", KNOWN);
  assert.deepStrictEqual(state.rule_classes, {});
  assert.strictEqual(marker, null);
});
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `node --test .claude/hooks/lib/__tests__/reviewer-escalation.test.js`
Expected: FAIL — cannot find module `../reviewer-escalation.js`.

- [ ] **Step 3: Write the module**

Create `.claude/hooks/lib/reviewer-escalation.js`:

```javascript
// Trip-wire A (Stage 9.5e): the repeat-finding escalation counter.
// Pure logic — orchestrator-side. Given prior counter state + this run's classified
// findings (rule-class strings the 3rd reviewer caught that the first two passed) + the
// diff sha, returns the next state and any graduate-to-analyzer marker.
//
// "Fires" at consecutive == 2 for a class → the marker is the L5 consequence
// (a human-reviewed work-order to promote the rule to a CER0xx analyzer / pre-commit hook).

const FIRE_AT = 2;

function applyEscalation(prior, classifiedFindings, diffSha, knownClasses) {
  const state = { rule_classes: { ...(prior && prior.rule_classes ? prior.rule_classes : {}) } };
  const hit = new Set((classifiedFindings || []).filter((c) => knownClasses.includes(c)));
  let marker = null;

  // Increment classes hit this run (unless this exact diff already counted).
  for (const cls of hit) {
    const cur = state.rule_classes[cls] || { consecutive: 0, last_diff_sha: null };
    if (cur.last_diff_sha === diffSha) { state.rule_classes[cls] = cur; continue; }
    const next = { consecutive: cur.consecutive + 1, last_diff_sha: diffSha };
    state.rule_classes[cls] = next;
    if (next.consecutive >= FIRE_AT && !marker) marker = { rule_class: cls, consecutive: next.consecutive };
  }

  // Reset classes NOT hit this run (a clean read for them).
  for (const cls of Object.keys(state.rule_classes)) {
    if (!hit.has(cls)) state.rule_classes[cls] = { consecutive: 0, last_diff_sha: diffSha };
  }

  return { state, marker };
}

module.exports = { applyEscalation, FIRE_AT };
```

- [ ] **Step 4: Run the test to verify it passes**

Run: `node --test .claude/hooks/lib/__tests__/reviewer-escalation.test.js`
Expected: PASS (5 tests).

- [ ] **Step 5: Seed the committed counter state**

Create `.claude/state/reviewer-pipeline/escalations.json` with an empty seed:

```json
{
  "rule_classes": {}
}
```

> Check `.gitignore`: `.claude/state/` may be gitignored (the playbook state is). If so, force-add this one seed file (`git add -f`) so the counter has a committed starting point, OR document that the orchestrator creates it on first run. Decide at implementation: prefer `git add -f` for a deterministic seed.

- [ ] **Step 6: Commit**

```bash
git add .claude/hooks/lib/reviewer-escalation.js .claude/hooks/lib/__tests__/reviewer-escalation.test.js
git add -f .claude/state/reviewer-pipeline/escalations.json
git commit -m "feat(9.5e): Trip-wire A escalation counter + graduate-to-analyzer marker logic + tests"
```

---

## Task 9: Doc cleanup C1–C4, C6 (retire dangling refs)

**Files:**
- Modify: `docs/roadmap-phase-three.md` (C1, C2, C3)
- Modify: `docs/superpowers/plans/2026-05-28-stage-9-5k-codified-subagents-impl.md` (C4)
- Modify: `.claude/agents/ceres-cto.md` (C6)

- [ ] **Step 1: C1 — fix the sequencing rule (roadmap:1232)**

Change the line:

> **Sequencing rule:** 9.5h closes before Phase 2 feature stages resume planning. Sub-stages run sequentially per the L1 order; any sub-stage's Trip-wire A/B/C (per L4) reverts the active autonomy level immediately.

to:

> **Sequencing rule:** 9.5h closes before Phase 2 feature stages resume planning. Sub-stages run sequentially per the L1 order; Trip-wire C (Stop-event hooks ≤10) and Trip-wire A (the 9.5e repeat-finding counter — see the 9.5e spec) gate the batch. Trip-wire A firing writes a `graduate-to-analyzer` marker promoting the repeat rule to a CER0xx analyzer/pre-commit-hook candidate (per L5); it does not "revert an autonomy level" — that earlier framing referenced a system that was never defined and is retired in 9.5e.

- [ ] **Step 2: C2 — fix the batch close-out (roadmap:1261)**

Change `no Trip-wire A/B fired during the batch` to `no Trip-wire A fired during the batch (no graduate-to-analyzer marker written)`. Leave the `Stop-event hooks ≤10 (Trip-wire C; currently 3)` clause unchanged.

- [ ] **Step 3: C3 — fix the 9.5e sub-stage row + checklist pointers (roadmap:1243, :1256)**

At roadmap:1243, change `Trip-wire A fires if the 3rd reviewer catches what the first 2 missed twice in a row (per L4).` to `Trip-wire A fires if the 3rd reviewer catches a mechanical miss the first 2 passed, twice consecutively for the same rule-class (defined in the 9.5e spec §5.4).` Leave the `(per L5)` migrate-to-analyzer pointer — it is correct.

At roadmap:1256, no wording change to E1/E2/E3 is needed, but append to the line: ` Spec: docs/superpowers/specs/2026-06-08-stage-9-5e-reviewer-pipeline-design.md.`

- [ ] **Step 4: C4 — correct the false 9.5k-plan claim**

In `docs/superpowers/plans/2026-05-28-stage-9-5k-codified-subagents-impl.md`, find the line (≈236) listing `.claude/skills/playbook/references/constitution.md — the eight-phase routing matrix, HARD vs advisory gates, Trip-wire A/B/C definitions`. Change `Trip-wire A/B/C definitions` to `Trip-wire C (Stop-hook cap). [Correction 2026-06-08: the constitution does NOT define Trip-wire A/B or an autonomy ladder; Trip-wire A is defined in the 9.5e spec §5.4, and Trip-wire B + the autonomy-level language were retired in 9.5e. This line originally over-claimed.]`

- [ ] **Step 5: C6 — sweep the ceres-cto autonomy wording**

In `.claude/agents/ceres-cto.md`:
- Line 3 (`description`): `autonomy posture, trip-wires` — "autonomy posture" as a general stance MAY stay; leave unless it reads as the retired concept. No change required if it's generic.
- Line 8 (body): change `the autonomy-revert trip-wires` to `the L5 graduate-to-analyzer trip-wire and the Stop-hook cap`.
- Line 14 (read-list note): `autonomy posture` is generic; leave.

Apply the same line-8 change to the mirror string in `docs/superpowers/plans/2026-05-28-stage-9-5k-codified-subagents-impl.md:229` (the role-body copy embedded in the 9.5k plan).

> The `ceres-cto.md` edit requires a session restart to take effect (agent files load at session start) — note this in the commit; it does not affect this stage's tests.

- [ ] **Step 6: Commit**

```bash
git add docs/roadmap-phase-three.md docs/superpowers/plans/2026-05-28-stage-9-5k-codified-subagents-impl.md .claude/agents/ceres-cto.md
git commit -m "docs(9.5e): retire Trip-wire B + autonomy-level refs; fix per-L4 pointers + false constitution claim (C1-C4,C6)"
```

---

## Task 10: Smoke-test the three reviewer roles

**Files:** none (dispatch + capture). Requires a session restart to load the new agent files (agent files load at session start; the `/agents` UI takes effect immediately).

- [ ] **Step 1: Restart the session (or open `/agents`) so the three new roles load**

Confirm via `/agents` that `reviewer-writer`, `reviewer-security`, `reviewer-playwright-test-audit` appear with `disallowedTools` set (no typo silently granting all tools).

- [ ] **Step 2: Dispatch each role against this very stage's diff**

For each role, dispatch with `subagent_type` = the role name and a prompt naming: the diff (`git diff main...stage-9.5e-reviewer-pipeline`), the spec (`docs/superpowers/specs/2026-06-08-stage-9-5e-reviewer-pipeline-design.md`), and the roadmap stage (9.5e). 

- [ ] **Step 3: Apply the dispatcher gate to each response**

Assert each response's first non-whitespace line is `## What I read`, then `## Conflicts found`, then the verdict, and the read-list includes the role's floor + the named files. Re-dispatch any role that buries the preamble. Capture the three transcripts for the close-out commit message.

- [ ] **Step 4: Record the smoke-test result**

Note in the close-out (Task 11) that 3/3 roles passed the dispatcher gate (or which were re-dispatched and why).

---

## Task 11: Stage close-out

**Files:**
- Modify: `docs/roadmap-phase-three.md` (tick the 9.5e `[ ]`)
- Run: `sync-docs`, `changelog-sync` (Phase E requirements)

- [ ] **Step 1: Full build + test gate (Definition of Done)**

Run each; all must exit 0:
```bash
dotnet build ProjectCeres/ProjectCeres.csproj
dotnet test ProjectCeres.Tests/ProjectCeres.Tests.csproj --filter "FullyQualifiedName~MigrationDriftTests"
node --test .claude/skills/verify-stage-completeness/hooks/__tests__/validate-reviewer-pipeline.test.js
node --test .claude/hooks/lib/__tests__/reviewer-escalation.test.js
pnpm --dir ProjectCeres.Client build
pnpm --dir ProjectCeres.Client test --run
```
> The full `dotnet test` suite also runs via the Stop hook (Tier 2 — this stage writes a test file). Confirm green.

- [ ] **Step 2: Confirm Trip-wire C still green**

Run: `grep -c '"Stop"' .claude/settings.json` and confirm the Stop-event hook count is unchanged (still 3 — 9.5e added zero Stop hooks). Confirm no `graduate-to-analyzer` marker exists under `.claude/state/reviewer-pipeline/` (Trip-wire A did not fire during the batch).

- [ ] **Step 2b: Run verify-stage-completeness**

Invoke the `verify-stage-completeness` skill — 9.5e adds no new IUserOwned entity / service / resx / enum (it adds agent files, a hook validator, a unit test, a JS lib), so the audit should report no new registry obligations. Confirm.

- [ ] **Step 3: sync-docs + changelog-sync**

Invoke `sync-docs` against the branch diff (routes the spec/plan + the agents.md + roadmap edits to the right docs). Invoke `changelog-sync` to add the 9.5e entry to `[Unreleased]`.

- [ ] **Step 4: Tick the 9.5e checklist line**

In `docs/roadmap-phase-three.md`, change the `- [ ] 9.5e — 3-agent reviewer pipeline ...` line (≈1256) to `- [x]` with a close-out note: shipped artifacts (3 roles, the slot + validator, the E3 test, the Trip-wire A counter), the 3/3 smoke-test result, and the C1–C6 cleanup. Confirm zero `[ ]` remain under the `## Stage 9.5h` heading that belong to 9.5e (per `feedback_finished_stages_have_no_unchecked_items`).

- [ ] **Step 5: Final close-out commit**

```bash
git add docs/roadmap-phase-three.md docs/CHANGELOG.md docs/  # whatever sync-docs touched
git commit -m "docs(9.5e): close out 3-agent reviewer pipeline; tick 9.5e; smoke-test 3/3"
```

---

## Self-review (run before handing off)

**1. Spec coverage** — every §2 lock maps to a task:
- E-L1 (turn-end slot only, no new hook) → Tasks 5+6. ✓
- E-L2 (orchestrator-driven dispatch) → Tasks 1-3 (roles) + Task 10 (dispatch). ✓
- E-L3 (three diff-focused roles, 9.5k conventions) → Tasks 1-4. ✓
- E-L4 (E3 as build test via `HasPendingModelChanges()`) → Task 7. ✓
- E-L5 (Trip-wire A counter + marker) → Task 8. ✓
- E-L6 (retire B + autonomy; fix pointers + false claim) → Task 9. ✓
- Cleanup C5 (dead predicate) → Task 6 Step 2. ✓
- §7 testing (validator, trigger, counter, E3 negative control, smoke-test) → Tasks 5, 7, 8, 10. ✓

**2. Placeholder scan** — no "TBD"/"TODO". The two implementation decisions flagged inline (the IIFE-on-require guard in Task 5 Step 3; the `.gitignore` force-add in Task 8 Step 5) carry a stated default, not a blank. ✓

**3. Type/name consistency** — role names (`reviewer-writer`, `reviewer-security`, `reviewer-playwright-test-audit`) match across Tasks 1-4, 10, and the `docs/agents.md` table. `validateReviewerPipeline(slotPath, headSha)` signature matches between Task 5 (def) and Task 6 (call). `applyEscalation(prior, classifiedFindings, diffSha, knownClasses)` matches between Task 8's test and module. The slot file name `reviewer-pipeline.json` is identical in the `SLOT_TABLE` entry, the validator wiring, and the recovery text. ✓
