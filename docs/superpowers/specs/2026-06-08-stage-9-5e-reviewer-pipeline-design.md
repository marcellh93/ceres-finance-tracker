# Stage 9.5e — 3-agent reviewer pipeline (writer / security / playwright-test-audit)

> **Diataxis type:** Reference + explanation — design spec for sub-stage 9.5e under `## Stage 9.5h — Phase 1 hardening container` in `docs/roadmap-phase-three.md`.
>
> **Status:** Draft — design approved 2026-06-08. Awaiting implementation plan + user review of this spec.
>
> **Predecessor:** 9.5d (AppRole test suite, shipped 2026-06-07). **Prerequisite:** 9.5k (codified subagents, shipped 2026-05-30) — 9.5e's three review roles inherit 9.5k's `disallowedTools:` deny-list + first-line `## What I read` contract.
>
> **Shorthand correction (carried from the 9.5d precedent, spec line 3).** The roadmap line (`docs/roadmap-phase-three.md:1256`) says the pipeline "Fires on Edit/Write touching `<paths>`." Read literally that describes a hook that *runs* three agents — which cannot exist (see §3). The honest description of what ships: **the turn-end evidence-bundle hook DETECTS the diff shape and DENIES the Stop until a reviewer-pipeline proof artifact exists; the orchestrator (the main session) DISPATCHES the three reviewers.** Detection + enforcement is tool-grounded; dispatch is orchestrator-driven. This spec uses that framing throughout.

## 1. Problem

Single-agent self-review degrades on reasoning tasks (Huang et al., arXiv:2310.01798 — the same citation behind the 9.5a deletion of 10 prose-reading Stop hooks). Diffs that touch the three highest-risk surfaces in the codebase — pre-auth authentication code, EF migrations, and user-owned (`IUserOwned`) entity models — are exactly where a single reasoning pass is most likely to miss a class-of-bug regression:

- **The 9.3 `EmailConfirmationTokens` RLS gap** shipped because a new `IUserOwned` table reached production without its `user_isolation` RLS policy. The integration suite couldn't catch it (it routed through `ceres_admin`, BYPASSRLS — closed by 9.5b/9.5d). A second, security-focused independent read of that diff would have asked "where is this table's RLS policy?"
- **Scope drift** — a diff that claims to implement a spec but quietly does less (the 6b.1 TOTP-counter half-done work) survives a single self-review that anchors on the author's own intent.
- **Vacuous tests** — a test that passes without asserting what the spec promised (a `[Fact]` with no meaningful assertion) reads as green coverage. A read dedicated to *spec-promise → test-assertion* mapping catches it.

Three independent reads — one per failure mode — beat one self-review. 9.5e codifies those three reads as named review roles, makes running them mandatory for the three watched path families via a tool-grounded turn-end gate, and adds a counter that escalates a twice-caught mechanical miss into a build-enforced rule (the roadmap's L5 staging-ground rule made concrete).

## 2. Locked decisions

| Lock | Decision | Source |
|---|---|---|
| E-L1 | **Enforcement = turn-end proof-slot only.** One `reviewer-pipeline.json` slot added to the existing `evidence-bundle-check.js` `SLOT_TABLE`. NO new Stop hook (respects Trip-wire C — Stop-event hooks ≤10, currently 3). NO commit-time PreToolUse gate in 9.5e (deferred unless turn-end proves too late — documented in §12, not opened as a `[ ]`). | User Q (2026-06-08 brainstorm) + ceres-architect dispatch |
| E-L2 | **Orchestrator-driven dispatch.** The three reviewers are dispatched by the main session via the Agent tool, NOT by any hook (hooks are read-only; they cannot call the Agent/Skill tool) and NOT by a Workflow (same recursion limit + no enforcement teeth). The hook checks the *proof artifact*; the orchestrator produces it. | ceres-architect dispatch + `docs/agents.md:49` ("they do not spawn other subagents") |
| E-L3 | **Three review roles, diff-focused read-list.** `writer` / `security` / `playwright-test-audit`, each cloned from 9.5k's template (`disallowedTools:` deny-list, `## What I read` → `## Conflicts found` → answer contract). Read-list = the diff + the spec it claims to implement + role-specific invariant docs (NOT the strategy roles' fixed-doc baseline). | User Q (2026-06-08 brainstorm) + 9.5k spec §12 |
| E-L4 | **Condition E3 is a build-enforced architecture test, NOT a reviewer job.** A `[Fact]` using EF Core's `DbContext.Database.HasPendingModelChanges()` asserts the model has no un-migrated drift. E3 is the syntactically-locatable, fires-every-time case that the roadmap's own L5 rule says must live in a test/analyzer, never in an agent. | User Q (2026-06-08 brainstorm) + ceres-architect dispatch + roadmap:1243 |
| E-L5 | **Trip-wire A = a concrete repeat-finding counter.** `.claude/state/reviewer-pipeline/escalations.json` tracks, per enumerated rule-class, the count of consecutive runs where the third reviewer caught a mechanical miss the first two passed. At `consecutive == 2` it writes a `graduate-to-analyzer` marker (the L5 consequence). NO "autonomy level" semantics. | User Q (2026-06-08 brainstorm) + ceres-cto dispatch |
| E-L6 | **Retire Trip-wire B + the "active autonomy level" language.** Neither is defined anywhere in the repo. Drop `/B` from the sequencing rule + close-out gate; replace "reverts the active autonomy level" with the concrete `graduate-to-analyzer` consequence; fix the broken "per L4" cross-references; correct the false 9.5k-plan claim that the constitution holds the trip-wire definitions. All in the 9.5e commit chain (the receiving stage). | User Q (2026-06-08 brainstorm) + ceres-cto dispatch |

## 3. The platform constraint that shapes everything

**Claude Code hooks are read-only.** A PreToolUse / Stop / PostToolUse hook reads a JSON payload from stdin and returns one of: exit 0 (allow), exit 2 / `permissionDecision: "deny"` (block with a reason), or `additionalContext` (advisory text). A hook **cannot** call the Agent tool, the Skill tool, or otherwise dispatch a subagent. Verified across all ~14 hooks in `.claude/` (none dispatches an agent) and against `docs/agents.md:49` ("they do not spawn other subagents (platform does not support subagent recursion)").

Consequence: "a pipeline that fires on Edit/Write and runs three agents" is not buildable as a hook. The only actor that can call the Agent tool is the top-level orchestrator (the main session). So the architecture is necessarily **two-layered**:

1. **Dispatch layer (orchestrator).** When my own committed diff touches the watched paths, I dispatch the three reviewers via the Agent tool and serialize their combined output to `reviewer-pipeline.json`.
2. **Enforcement layer (hook).** The existing turn-end `evidence-bundle-check.js` Stop hook detects the watched diff shape and **blocks the Stop** unless `reviewer-pipeline.json` exists, is fresh, and validates. The hook never runs agents — it checks the artifact the orchestrator's dispatch produced.

This mirrors every other 9.5h mechanism: enforcement is a filesystem/tool fact, not a process you hope ran (9.5h Goal, roadmap:1230). A Workflow-tool variant was rejected (E-L2): a workflow is still a process the orchestrator chooses to run, so it adds dispatch indirection without adding an enforcement guarantee — the block still has to be the hook.

## 4. Pre-flight findings (verify-against-codebase + direct reads, 2026-06-08)

- `evidence-bundle-check.js` `SLOT_TABLE` confirmed at `.claude/skills/verify-stage-completeness/hooks/evidence-bundle-check.js:27-79`. Slot shape: `{ slot, when(files)→bool, reason }`. Required slots = `SLOT_TABLE.filter(s => s.when(codeFiles))`; each must exist under `.claude/state/evidence/stage-<id>/` and be fresh (mtime > turn-start). Only `turn-shape.json` gets content-validation today (`validateTurnShape`, lines 121-145).
- **`Common/Authentication/**` is covered by NO existing slot** — the reviewer slot is genuinely additive (no overlap with `rls-audit.psql`, which keys on `Models/` + `Migrations/`, or `curl-transcript.txt`, which keys on `Controllers/` + `Endpoints/`).
- **DEFECT FOUND (folded into 9.5e cleanup, §8):** the `registry-sweep.json` slot predicate at `evidence-bundle-check.js:67` still matches `ProjectCeres/Common/UserOwnedTables.cs` — a file **deleted in 9.5b** (the user-owned set is model-derived via `UserOwnedModel.RlsTables` since `136b8c7..ea7848c`). That regex branch is dead. 9.5e edits this exact file to add the reviewer slot, so the stale predicate is fixed in the same pass (per `feedback_clean_dead_code_immediately`).
- `ArchitectureTests.cs` confirmed at `ProjectCeres.Tests/Integration/Authentication/ArchitectureTests.cs`. `Every_user_owned_entity_carries_a_global_query_filter` (line 707) is the synchronous-`[Fact]` template the E3 test follows. No existing test enforces "entity-change requires migration" — E3 is a genuine gap, not a duplicate.
- EF Core **10.0.5** (`Microsoft.EntityFrameworkCore` + `.Relational` in `ProjectCeres.Tests.csproj`; `Microsoft.EntityFrameworkCore.Design` in `ProjectCeres.csproj`). `DbContext.Database.HasPendingModelChanges()` (CLI twin: `dotnet ef migrations has-pending-model-changes`) is the official drift-check API — confirmed against EF Core docs (context7 `/dotnet/efcore`). `AppDbContextModelSnapshot.cs` is present in `ProjectCeres/Migrations/` (the snapshot the API compares against).
- **DANGLING REFERENCES FOUND (folded into 9.5e cleanup, §8):** "autonomy level / active autonomy level / L0–L5 ladder," "Trip-wire A" (definition), and "Trip-wire B" (definition + firing condition) are **NOT FOUND** anywhere in `docs/`, `.claude/`, or the ADRs — confirmed by exhaustive grep. Only Trip-wire C ("Stop-event hooks ≤10") is concrete. The "per L4/L5" pointers at roadmap:1232/1243 resolve to the **9.5c analyzer scope-locks** (L4 = CER010 ticket format; L5 = resx parity) — unrelated to trip-wires; broken cross-references. The 9.5k impl plan (`...impl.md:236`) falsely claims the constitution "holds the Trip-wire A/B/C definitions"; the constitution holds none.
- The five `ceres-*` strategy roles + `docs/agents.md` conventions confirmed at `.claude/agents/` and `docs/agents.md`. The deny-list rationale (typo-grants-all footgun) and the dispatcher-gate rule are the authoring template for the three new review roles.

## 5. Architecture

Five artifacts + one cleanup pass. No new Stop hook, no new runtime service in `ProjectCeres/`.

### 5.1 The three review-role files (`.claude/agents/`)

Self-contained markdown, frontmatter + body per the 9.5k template. Frontmatter: `name`, `description`, `disallowedTools`, `model: inherit`. Body: stance (≤2 sentences) → "BEFORE YOU ANSWER — read first" (the diff-focused floor) → the `## What I read` / `## Conflicts found` / answer contract copied verbatim.

| Role (`subagent_type`) | Stance | Always-read floor | `disallowedTools` |
|---|---|---|---|
| `reviewer-writer` | Did the diff do what the stage spec said? Restate the spec's intent, check the code against it, flag scope drift / half-done work. | `CLAUDE.md`, the active roadmap stage section, the stage's spec under `docs/superpowers/specs/`, the diff (dispatcher-named files) | `Write, Edit, NotebookEdit, Bash` |
| `reviewer-security` | Auth / RLS / pre-auth-scope hole? Check the five-registry story for any new user-owned table, the RLS-policy-in-same-migration rule, pre-auth write scoping. | `CLAUDE.md`, `docs/security-model.md`, `docs/multi-tenancy-strategy.md`, `ProjectCeres.Tests/Integration/Authentication/ArchitectureTests.cs`, the affected `ProjectCeres/Common/Authentication/` files (dispatcher-named) | `Write, Edit, NotebookEdit, Bash` |
| `reviewer-playwright-test-audit` | Do the tests assert what the spec claims, or pass vacuously? Emit the structured **spec-vs-assertion diff** (Condition E2): each spec promise → the test that pins it, flagging any promise with no test. | `CLAUDE.md`, `docs/testing.md`, the stage spec, the test files in the diff, any Playwright trace under `.claude/state/evidence/stage-<id>/` | `Write, Edit, NotebookEdit` (keeps read-only `Bash` to list/read test artifacts — same exception as `ceres-tech-lead`) |

> **Naming note for the plan:** role `name:` values must be unique and kebab-case (duplicate names are silently discarded per the 9.5k research). The `reviewer-` prefix disambiguates from the five `ceres-*` strategy roles. Final names confirmed at implementation; this spec uses `reviewer-{writer,security,playwright-test-audit}`.

The third reviewer's `spec_vs_assertion_diff` output is the Trip-wire A counter's input (§5.4): a finding it surfaces in a recognized rule-class that the writer + security reviewers both passed is the "third reviewer caught what the first two missed" signal.

### 5.2 The turn-end proof-slot (`evidence-bundle-check.js`)

One new `SLOT_TABLE` entry:

```js
{
  slot: "reviewer-pipeline.json",
  when: (files) => files.some((f) =>
    /^ProjectCeres\/Common\/Authentication\//.test(f) ||
    /^ProjectCeres\/Migrations\//.test(f) ||
    /^ProjectCeres\/Models\//.test(f)),
  reason: "diff touches auth, migrations, or IUserOwned models — 3-agent reviewer pipeline required",
},
```

Plus a `validateReviewerPipeline(slotPath)` function paralleling the existing `validateTurnShape`, wired into `main()` the same way (lines 198-199). It asserts:

1. **All three roles present** — `reviewers[]` contains `writer`, `security`, `playwright-test-audit`.
2. **Diff freshness at the content level** — `diff_sha` matches `git rev-parse HEAD` (the reviewers read *this* committed diff, not a stale one). Mirrors how `turn-shape.json` checks `query_output` exists on disk.
3. **No surviving block** — no reviewer carries `verdict: "block"`. A reviewer's `block` must be resolved (and the slot regenerated) before the Stop is allowed. This is what gives a reviewer's veto teeth.

Slot content shape (written by the orchestrator's dispatch):

```json
{
  "stage": "9.5e",
  "diff_sha": "<HEAD sha the reviewers read>",
  "reviewers": [
    { "role": "writer",                "verdict": "pass|block", "findings": [ /* {rule_class?, detail} */ ] },
    { "role": "security",              "verdict": "pass|block", "findings": [ ... ] },
    { "role": "playwright-test-audit", "verdict": "pass|block", "findings": [ ... ],
      "spec_vs_assertion_diff": [ /* {spec_promise, test_ref|null} */ ] }
  ]
}
```

**Path-coverage subtleties (both follow existing precedent):**
- The roadmap qualifier "Models/** *for IUserOwned entities*" is softened at the hook layer to **any `Models/` change** — a stdin-only hook can't reflect the built assembly to test the marker interface. The slot over-triggers on `Models/`; the `reviewer-security` role does the "was this actually `IUserOwned`" narrowing. The existing `rls-audit.psql` slot already keys on `Models/` wholesale — same over-trigger-then-narrow pattern.
- The bypass is `CERES_SKIP_REVIEWER_PIPELINE=1`, mapped onto the existing `CERES_SKIP_EVIDENCE_BUNDLE_HOOK=1` convention (`evidence-bundle-check.js:158`). Decision for the plan: whether this is a *second* independent env var the hook checks, or whether the reviewer slot rides the existing single bypass. Default: a second, slot-specific env var so a reviewer-only bypass doesn't disable the whole evidence bundle.

**Recovery block:** the hook's stderr recovery message (lines 218-224, currently hard-coding the agent-walk + psql lines) gains a reviewer-pipeline line instructing the orchestrator to dispatch the three reviewers and write the slot.

### 5.3 Condition E3 — build-enforced architecture test

A synchronous `[Fact]` in `ArchitectureTests.cs`, modeled on `Every_user_owned_entity_carries_a_global_query_filter` (line 707):

```csharp
[Fact]
public void Model_has_no_pending_migration_changes()
{
    using var ctx = /* design-time AppDbContext build — no live DB connection (see plan) */;
    ctx.Database.HasPendingModelChanges()
        .Should().BeFalse(
            "an entity's shape changed without a matching migration — " +
            "run `dotnet ef migrations add <Name>` in the same commit (Condition E3)");
}
```

`HasPendingModelChanges()` builds the current EF model and diffs it against `AppDbContextModelSnapshot.cs`; `true` means an entity changed without a captured migration. Deterministic, framework-grounded, accurate where a path-matching commit-hook would mis-fire (a renamed private field or a `[NotMapped]` computed property changes `Models/*.cs` but correctly needs no migration → the API returns `false`).

> **The one wiring detail the plan must nail (not hand-wave):** the test must build the model at design time **without opening a Postgres connection**. The plan pins the exact fixture — reusing the existing test-context construction (the `DualContextWebApplicationFactory` / `RlsTestFixture` model-build path, or a bare `DbContextOptionsBuilder<AppDbContext>().UseNpgsql(<conn-string-not-opened>)`), confirming `HasPendingModelChanges()` resolves the model from the assembly without a live connection. This is the single spot most likely to need a probe during implementation.

### 5.4 Trip-wire A — the repeat-finding counter

State file `.claude/state/reviewer-pipeline/escalations.json`:

```json
{
  "rule_classes": {
    "missing-rls-policy":        { "consecutive": 1, "last_diff_sha": "abc123" },
    "entity-without-migration":  { "consecutive": 2, "last_diff_sha": "def456" }
  }
}
```

- **Rule-class registry.** A small enumerated set seeded from the known class-of-bug list (e.g. `missing-rls-policy`, `entity-without-migration`, `pre-auth-write-without-scope`, `missing-query-filter`, `missing-resx-pair`). A `playwright-test-audit` finding the reviewer can classify into one of these increments that class's counter **only if** the writer + security reviewers both returned clean for it (the "third caught what the first two missed" condition). A free-text finding with no rule-class is reported but increments nothing — it is not yet syntactically-locatable, so it cannot graduate to a build rule.
- **Increment / reset.** "Consecutive" = consecutive pipeline runs, keyed by `last_diff_sha` to avoid double-counting the same diff. A clean run for a rule-class resets it to 0.
- **Fires at `consecutive == 2`** → writes a `graduate-to-analyzer` marker file (under the same state dir) naming the rule-class. The marker IS the consequence — it's the roadmap's L5 rule made concrete ("any rule that fires twice and is syntactically locatable migrates to a Roslyn analyzer or pre-commit hook"). The marker is a human-reviewed work-order to open a CER0xx sub-stage; it does NOT auto-write an analyzer.
- **Where the increment logic runs.** In the orchestrator's dispatch step (the same code that writes `reviewer-pipeline.json`), NOT in the hook (the hook is read-only and can't write the counter). The hook only *reads* the marker's absence as part of the close-out check (§7).

### 5.5 Data flow

```
Orchestrator edits ProjectCeres/Common/Authentication|Migrations|Models  →  commits
   │
   ├─ Orchestrator dispatches 3 reviewers (Agent tool) against HEAD diff
   │     writer / security / playwright-test-audit  (each: read-first contract, verdict)
   │  └─ serializes → .claude/state/evidence/stage-9.5e/reviewer-pipeline.json
   │  └─ updates    → .claude/state/reviewer-pipeline/escalations.json
   │                  (+ writes graduate-to-analyzer marker if any class hits 2)
   │
   └─ Turn ends → evidence-bundle-check.js Stop hook
         reviewer-pipeline.json required (diff matched watched paths)?
            exists + fresh + validateReviewerPipeline passes (3 roles, sha match, no block)?
               → exit 0 (Stop allowed)
               → else exit 2 (Stop blocked, recovery message names the missing slot)
```

E3 runs orthogonally in the test suite (turn-end `dotnet test` gate), independent of the reviewer dispatch.

## 6. Error handling

- **Missing / stale / invalid `reviewer-pipeline.json`:** Stop blocked, recovery message instructs the dispatch (same shape as every other slot gap). Not silent.
- **A reviewer returns `verdict: "block"`:** `validateReviewerPipeline` fails (rule 3) → Stop blocked until the block is resolved and the slot regenerated. The reviewer's veto is enforced, not advisory.
- **Reviewer read-first contract skipped (no `## What I read` first line):** the orchestrator's dispatcher-gate (CLAUDE.md § Using subagents) re-dispatches with a stricter prompt — same gate as the strategy roles. The slot is not written from a contract-violating response.
- **Rule-class unrecognized:** finding reported in `findings[]`, no counter increment (can't graduate what isn't locatable). No error.
- **E3 false drift (snapshot legitimately ahead of model after a `migrations remove`):** the test fails loudly — which is correct; the snapshot and model must agree at commit. Resolution is to regenerate the snapshot, never to weaken the assertion.
- **Bypass `CERES_SKIP_REVIEWER_PIPELINE=1`:** logs as bypassed, doesn't block. For genuine pipeline-infra failures only — same posture as the evidence-bundle bypass.

## 7. Testing / verification

- **Reviewer-role smoke test (3 dispatches):** dispatch each role against a trivial real Project Ceres diff; assert each response opens with `## What I read` (listing its floor + the dispatcher-named diff files), then `## Conflicts found`, then the verdict. Capture transcripts in the close-out.
- **Slot-validator unit tests (`validateReviewerPipeline`):** (a) all-three-present + sha-match + no-block → passes; (b) missing a role → fails; (c) stale `diff_sha` → fails; (d) a surviving `verdict: "block"` → fails. Mirrors the `validateTurnShape` test pattern.
- **Slot-trigger test:** a diff touching `Common/Authentication/**` requires the slot; a docs-only diff does not; a `Models/**` diff requires it (over-trigger confirmed). Assert against `SLOT_TABLE.filter`.
- **E3 architecture test is itself the verification** — `Model_has_no_pending_migration_changes` runs in the suite. Add a negative-control note: a deliberate uncommitted model change makes it red (proven once during implementation, then reverted — do NOT leave a drift in the tree).
- **Trip-wire A counter unit tests:** feed two consecutive same-rule-class findings (with distinct `last_diff_sha`) → assert `graduate-to-analyzer` marker appears; a clean run resets the counter; an unclassified finding increments nothing; the same diff twice (same sha) does not double-count.
- **Stop-hook tier:** editing `.claude/` (hook + agent files) + `ProjectCeres.Tests/` (the E3 `[Fact]`) → Tier 2 (test-file write) → full suite. The E3 test must pass; `dotnet build` + `dotnet test` + `pnpm build` + `pnpm test` all exit 0 before close-out (Definition of Done).
- **No new Stop hook:** confirm `settings.json` Stop-event hook count is unchanged (still 3) — Trip-wire C stays green.

## 8. Cleanup folded into 9.5e (per `feedback_no_flag_without_action` + `feedback_clean_dead_code_immediately`)

All defects surfaced *by* this work, on files this work touches — fixed in the 9.5e commit chain (the receiving stage), not flagged for later.

| # | Edit | File:line | Rationale |
|---|---|---|---|
| C1 | Drop `/B` from "Trip-wire A/B/C"; replace "reverts the active autonomy level immediately" with the concrete `graduate-to-analyzer` consequence | `docs/roadmap-phase-three.md:1232` | Trip-wire B + autonomy level are undefined anywhere (§4). Per E-L6. |
| C2 | "no Trip-wire A/B fired" → "no Trip-wire A fired" | `docs/roadmap-phase-three.md:1261` (batch close-out) | B retired; A is now defined (§5.4). Leaving "no Trip-wire B fired" in a close-out gate is an unsatisfiable-by-construction prerequisite. |
| C3 | Fix the broken "per L4" pointers → cite the 9.5e spec's Trip-wire A definition | `docs/roadmap-phase-three.md:1232, :1243` | "per L4" pointed at the 9.5c analyzer scope-lock (CER010 ticket format), unrelated to trip-wires. The "per L5" migrate-to-analyzer pointer is correct and stays. |
| C4 | Correct the false claim that the constitution "holds the Trip-wire A/B/C definitions" → point at the 9.5e spec; note B/autonomy retired | `docs/superpowers/plans/2026-05-28-stage-9-5k-codified-subagents-impl.md:236` | The constitution holds no trip-wire definitions. This false claim is what sent a ceres-cto dispatch hunting for definitions that don't exist; leaving it live re-triggers the same wild-goose-chase. |
| C5 | Remove the dead `ProjectCeres/Common/UserOwnedTables\.cs` branch from the `registry-sweep.json` slot predicate | `.claude/skills/verify-stage-completeness/hooks/evidence-bundle-check.js:67` | `UserOwnedTables.cs` was deleted in 9.5b (model-derived set now). Dead regex on the exact file 9.5e edits. |
| C6 | Verify + (if needed) soften the "autonomy-revert trip-wires" stance wording — sweep ALL occurrences: `ceres-cto.md` lines 3 (`description`), 8 (body stance), 14 (read-list note "autonomy posture") | `.claude/agents/ceres-cto.md:3,8,14` (+ the mirror at `docs/superpowers/plans/2026-05-28-...-impl.md:229`, same body string) | The CTO role is told to reason about a retired concept ("autonomy-revert trip-wires"). Confirm exact wording before touching; "autonomy posture" as a general stance may stay, but "autonomy-revert trip-wires" specifically should become "the L5 graduate-to-analyzer trip-wire." Don't edit only line 8 and leave the same concept live on 3/14. |

> The roadmap close-out's "Trip-wire A armed" `[ ]` (line 1256) is discharged by 9.5e shipping §5.4's counter + its unit tests. No deferral, no receiving-stage checkbox needed — the work lands in this stage.

## 9. Rollout

Single branch `stage-9.5e-reviewer-pipeline`, commit chain (TDD per `docs/testing.md`):

1. Three review-role files at `.claude/agents/reviewer-{writer,security,playwright-test-audit}.md` + `docs/agents.md` "review-pipeline roles" subsection.
2. `validateReviewerPipeline` + the `reviewer-pipeline.json` `SLOT_TABLE` entry + recovery-line in `evidence-bundle-check.js`; the C5 dead-predicate fix in the same edit. Slot-validator + slot-trigger unit tests.
3. E3 `Model_has_no_pending_migration_changes` `[Fact]` in `ArchitectureTests.cs` (red→green: prove it catches a deliberate drift, then revert the drift).
4. Trip-wire A counter (the orchestrator-side increment logic + `graduate-to-analyzer` marker) + its unit tests.
5. Doc cleanup C1–C4, C6 (roadmap + 9.5k plan + ceres-cto stance).
6. Reviewer-role smoke test (requires session restart to load the new agent files, per 9.5k research) — transcripts captured.
7. Tick the 9.5e `[ ]` → `[x]`; `sync-docs` + `changelog-sync`; close-out per Phase E.

## 10. Out of 9.5e — scope-adjacent work routed elsewhere

| Item | Receiving structure | Why not in 9.5e |
|---|---|---|
| Commit-time PreToolUse gate (block `git commit` when watched files are staged without reviewer proof) | Documented option; NO `[ ]` opened | Per E-L1: the turn-end slot is the minimal enforcement. The commit gate catches the skip earlier but adds a second hook + moving parts. Opens only if turn-end proves too late in practice (ceres-architect: "start with the slot only"). Not opened as `[ ]` — no current bug it fixes. |
| Auto-writing the analyzer when Trip-wire A fires | The CER005/CER006/CER0xx analyzer sub-stages (9.5f/9.5g + future) | Graduation is a human-reviewed promotion. The `graduate-to-analyzer` marker is the work-order; authoring the analyzer is the receiving analyzer-family sub-stage's job (per the existing L1 lock that caps analyzers per sub-stage). |
| Defining a real autonomy-level ladder | Out of scope entirely; retired (C1/C2) | No upstream stage chartered one; nothing depends on one. Building an L0–L5 system to satisfy one un-backed roadmap word would be scope invention, not phase discipline (ceres-cto). |

## 11. Open questions

None remaining. All brainstorm decisions locked in §2. The one implementation probe (E3's connectionless model build, §5.3) is a plan-time detail, not a design open question.

## 12. Cross-references

- **Roadmap:** `docs/roadmap-phase-three.md` § Stage 9.5h sub-stage 9.5e (lines 1243, 1256) + the container sequencing/close-out lines edited by C1–C3.
- **Prerequisite spec:** `docs/superpowers/specs/2026-05-28-stage-9-5k-codified-subagents-design.md` — the role-file template + dispatcher-gate this stage inherits (esp. §12, which routed these three roles to 9.5e).
- **Authoring conventions:** `docs/agents.md` — deny-list rationale, `## What I read` contract, the gate codified in `CLAUDE.md` § Using subagents.
- **Enforcement host:** `.claude/skills/verify-stage-completeness/hooks/evidence-bundle-check.js` — the `SLOT_TABLE` + `validateTurnShape` the reviewer slot + validator extend.
- **E3 host + template:** `ProjectCeres.Tests/Integration/Authentication/ArchitectureTests.cs::Every_user_owned_entity_carries_a_global_query_filter` (line 707). EF API: `DbContext.Database.HasPendingModelChanges()` (EF Core 10.0.5).
- **Doctrine:** the 9.5h Goal (roadmap:1230) — tool-grounded enforcement, not prose-based detection over the agent's own output. ADR-0077 (the analyzers-for-locatable-invariants split E3 + the L5 graduation rule rest on).
- **Feedback memories:** `feedback_iuserowned_requires_five_registries` (the `reviewer-security` checklist), `feedback_no_flag_without_action` + `feedback_clean_dead_code_immediately` (§8 cleanup), `feedback_research_before_confident_claims` (the EF-API + dangling-reference verification), `feedback_test_edge_cases_as_ship_gate` (§7 negative-assertion tests).
