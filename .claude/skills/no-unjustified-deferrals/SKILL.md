---
name: no-unjustified-deferrals
description: Fires when about to propose deferring a discovered bug/fix. Forces the proposal through a two-reason gate; if neither reason holds, the deferral is rejected and the work goes into the active batch stage instead.
---

# no-unjustified-deferrals

## The problem this skill exists to prevent

Past sessions have ended with deferral paragraphs like:

> The five items above are real findings BUT they don't change the structural decisions Phase 2 will build on. The right move is to proceed with the originally planned next step and batch these UX-adjacent findings as a "Phase 1 polish + bugfix" follow-up.

**"Doesn't change downstream structural decisions" is not a valid reason to defer a known bug.** The bug is still real, still costs the user every day, and still has to be fixed. Deferring it pushes the tracking burden onto the user, who then has to find the deferred item from memory and bring it back. This skill exists because that handoff has failed repeatedly.

## When this skill fires

- **Auto-trigger (pre-write hook)** when I'm about to write deferral language into a `docs/` file. Phrases watched live in `references/deferral-phrases.md`.
- **Auto-trigger (post-submit hook)** when the user pushes back with "you deferred X again" / "you forgot Y" / "we agreed to fix Z" / "still broken from last session" — corrective trigger after a deferral already shipped.
- **Manual** when I'm about to propose deferral in any user-facing text — even before writing it. The rule is mine to follow; the hook is the safety net.

The skill does NOT fire when the USER tells me to defer something. The user's "defer this" is authorization; my "let's defer this" is the failure mode.

## The rigid procedure

When the trigger fires, STOP. No further edits, commits, or proposal text until all steps complete.

### Step 1 — Name the bug

One sentence, three parts:
- **What's broken** (user-observable symptom)
- **Where** (file path or feature name)
- **Cost to the user** (every-time cost, or specific scenarios)

Example: "Light-mode background tokens render unreadable text on the Categories index (`ProjectCeres.Client/src/routes/categories/...`); every light-mode user sees illegible labels."

### Step 2 — State the two valid reasons verbatim

Copy these word-for-word into the diagnosis. Do not paraphrase.

> **Valid reason 1 — Tooling gap.** I cannot implement or test this correctly at this moment because a specific tool, dependency, infrastructure, or framework feature I need is unavailable.
>
> **Valid reason 2 — Already-scheduled.** The active batch stage (or the current sprint's existing tasks) will touch the affected code and would address this bug as part of its declared scope anyway.

No other reasons are valid. Specifically rejected (see `references/valid-reasons.md` for the full rejected list):

- "doesn't change structural decisions"
- "doesn't block the next phase"
- "Phase X polish"
- "follow-up"
- "low priority"
- "small / cosmetic / UX-only"
- "we can batch it later"
- "out of scope for this stage" (when applied to a discovered bug, not a phase-line item)

### Step 3 — Check each reason

Write the check explicitly. No hand-waving.

```
Tooling gap: [yes / no]
  - Missing tool/dep/infra: [name it, or "n/a"]
  - Evidence it's unavailable: [link, error, version pin, or "n/a"]

Already-scheduled: [yes / no]
  - Active batch stage: [stage name or "none open"]
  - Task in that stage that covers this bug: [task ID or scope-line citation, or "n/a"]
```

### Step 4 — Branch on the result

**If both are "no" → no deferral.** Two options, both fix-now:
1. Fix it in the current commit / current stage's next commit.
2. Open or extend the active **batch stage** (the user agreed to the queue-and-flush pattern). Add the bug as a `[ ]` line in the batch stage's checklist. Work on the batch stage runs to completion before the next phase's planning resumes — no new phase work until the batch is empty.

The skill DOES NOT allow "log it for later" without a receiving checkbox in an OPEN stage. Open means "stage section exists in the roadmap doc, has at least one `[ ]` line, and is the current or next active stage". A stage that is "Phase 5 polish batch" that nobody is working on is not open.

**If yes to either → deferral allowed, with three requirements:**
1. **Cite the valid reason** in the deferral entry. Not "deferred" — "deferred because [reason 1 or 2, with the specific tool/task named]".
2. **Add the receiving stage's checkbox in the same commit** as the deferral. Per `[[feedback_deferral_requires_receiving_stage_checkbox]]`, the source-stage annotation is necessary but NOT sufficient — the receiving stage must have the matching `[ ]`.
3. **Add a mechanical tripwire** — a failing test, an architecture-test assertion, a CI check, or a TODO-comment with a `// FIXME: re-surface in Stage X` marker. Something that fires automatically when the receiving stage opens. Without this, the deferral relies on user memory, which is the failure mode this skill exists to prevent.

### Step 5 — Write the entry

Use the template in `references/deferral-entry-template.md`. The template enforces all three requirements as required fields. If you can't fill a field, you can't defer.

## What this skill does NOT do

- It does not override an explicit user "defer this". The user's call always wins.
- It does not catch deferrals that ship without any of the watched phrases. The hook is regex-based and will miss creative phrasings. The skill rule covers what the hook misses — invoke it any time you're about to propose deferral, regardless of whether the hook fired.
- It does not adjudicate priority. "Defer or fix now" is binary; "fix in commit 1 vs commit 5 of the batch stage" is separate scheduling and the skill doesn't speak to it.

## How "batch stage" works (the user's preferred model)

The user chose **queue-and-flush over immediate-interleaved**:

- Discovered bugs queue into a single named batch stage (e.g. "Phase 1 polish + bugfix"). The batch stage is a real stage in `roadmap-phase-X.md`, with `[ ]` lines like any other stage.
- The batch stage must be **opened immediately** when the first bug queues into it. Not "we'll open one if we accumulate enough" — open it on first bug.
- The batch stage **must run to completion before the next phase's planning resumes.** Phase N+1 cannot start its planning while Phase N's batch is non-empty.
- If a bug arrives mid-phase, it goes into the active batch stage's checklist. If no batch stage is open, the act of finding the bug opens one.

This is "fix-now in a holding stage", not "fix-eventually in an open queue".

## Required reply shape when this skill fires

```
🛑 Deferral check — [bug-name in one line]

Bug:
  - What: ...
  - Where: ...
  - Cost: ...

Tooling gap: [yes/no, with named tool]
Already-scheduled: [yes/no, with task ID]

Result: [no deferral / deferral allowed]

[If no deferral:]
  Action: fix in [commit / batch stage line], not deferring.

[If deferral allowed:]
  Reason: [reason 1 or 2, cited]
  Receiving stage: [name, with `[ ]` line added this commit]
  Tripwire: [test name / assertion / FIXME marker]
```

## Linked memory

- `[[feedback_deferral_requires_receiving_stage_checkbox]]` — the receiving-stage `[ ]` requirement.
- `[[feedback_defer_work_to_all_three_docs]]` — the three-layer documentation rule for Phase 3 deferrals.
- `[[feedback_persist_deferred_decisions]]` — never persist deferrals in stage-scoped specs.
- `[[feedback_finished_stages_have_no_unchecked_items]]` — closing rule.
- `[[feedback_clean_dead_code_immediately]]` — sibling pattern: don't defer cleanup that belongs in the current fix-up.
- `[[feedback_test_edge_cases_as_ship_gate]]` — tests as the mechanical tripwire format.
