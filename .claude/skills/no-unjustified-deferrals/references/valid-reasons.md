# Valid Reasons to Defer — and the Rejected List

## The two valid reasons (verbatim)

These are the ONLY justifications that allow a deferral. Copy them word-for-word into the diagnosis when you cite them.

### Reason 1 — Tooling gap

> I cannot implement or test this correctly at this moment because a specific tool, dependency, infrastructure, or framework feature I need is unavailable.

**What counts:**
- The fix requires a package version not yet released.
- The fix requires a test fixture / harness / mock that doesn't exist and is itself stage-sized to build.
- The fix requires infrastructure not yet provisioned (e.g. a queue, a worker, a second database, a CDN).
- The fix requires a feature in the runtime/framework that we're blocked from upgrading to in the current sprint.

**What does NOT count:**
- "It would take time to learn the right pattern." → Not a tooling gap. Learn it.
- "There's no existing test for this area." → Not a tooling gap unless writing the harness itself is stage-sized. Otherwise, add the test.
- "We don't have a CI pipeline yet." → Tests still get written; they run locally.
- "The fix needs a new utility file." → Not a tooling gap. Add the file.

### Reason 2 — Already-scheduled

> The active batch stage (or the current sprint's existing tasks) will touch the affected code and would address this bug as part of its declared scope anyway.

**What counts:**
- The current batch stage has a `[ ]` line that, when executed, will fix this bug as a natural side-effect.
- A roadmap task in the active phase covers a refactor of the affected file and the bug disappears in that refactor.
- The fix is one line in a larger named refactor that's already scoped and starting this sprint.

**What does NOT count:**
- "Phase 5 will rewrite this anyway." → Not already-scheduled. Phase 5 is months away. Fix it now.
- "There's a `planning-future.md` entry that touches this area." → `planning-future.md` is not scheduled.
- "We'll probably touch this in the SPA migration." → "Probably" is not scheduled.
- "It would fit naturally in the next stage's scope." → Then add it to the next stage's checklist NOW (which makes it scheduled).

---

## The rejected list — phrases that look like reasons but aren't

Whenever I'm about to use one of these as the justification, the deferral is invalid.

| Phrase | Why it's rejected |
|---|---|
| "Doesn't change structural decisions" | A bug is a bug regardless of whether downstream architecture depends on it. The user still sees it. |
| "Doesn't block Phase N" | Not the bar. The bar is "is it broken for the user". |
| "Phase X polish" | A label, not a reason. The bug exists today. |
| "Follow-up" | "Follow-up" without a named open stage means "I'll forget". |
| "Low priority" | Not a reason to defer indefinitely. Low priority bugs go into the active batch stage's checklist. |
| "Small / cosmetic / UX-only" | The user still sees it. UX-only bugs in a personal finance app ARE the product. |
| "Batch it later" | "Later" without a named open stage = "I'll forget". |
| "Out of scope for this stage" | Misuse. "Out of scope for this stage" applies to phase-line items, not to discovered bugs. Discovered bugs belong in the batch stage. |
| "We can address this when we touch X" | If you can name when, schedule it now. If you can't, you can't defer. |
| "Tracking in the spec" | Specs are stage-scoped and get superseded. See `[[feedback_persist_deferred_decisions]]`. |

---

## Worked examples — valid vs invalid

### Example 1 — INVALID

**Situation:** During a code review I notice that the Categories archive button has no Reactivate counterpart. The user can archive but can't undo.

**My (wrong) proposal:** "This is UX-only and doesn't change Phase 2's structural decisions. I'll defer it as a Phase 1 polish item."

**Why rejected:** "UX-only" is on the rejected list. The user observes this bug every time they try to recover from a misclick. There's no tooling gap (Archive flow exists, Reactivate is symmetrical). There's no already-scheduled task that covers it.

**Correct action:** Open or extend the active batch stage, add `[ ]` Reactivate flow for archived Categories, fix it before Phase 2 planning resumes.

---

### Example 2 — VALID under Reason 1

**Situation:** During Stage 8 I notice that login error messages leak whether an email exists in the database (timing attack via Argon2id hashing latency).

**Valid deferral:** "Tooling gap — the fix requires `IFakeUserStore` to inject a fixed-cost hash for non-existent users, which doesn't exist in our test harness; building that harness is itself a multi-commit task with its own architecture-test requirements. Deferring to Stage 9.2, with `[ ]` line added, plus a failing architecture test in `ArchitectureTests.cs` that asserts the harness wrapper exists once Stage 9.2 opens."

**Why valid:** Tooling gap is named, the receiving stage has a `[ ]` line in the same commit, and there's a mechanical tripwire (the architecture test) that will fire when Stage 9.2 opens.

---

### Example 3 — VALID under Reason 2

**Situation:** I notice the `MovementsController.Edit` action uses `FirstOrDefault` instead of `FirstOrDefaultAsync`, blocking the request thread.

**Valid deferral:** "Already-scheduled — Stage 9.3's checklist has `[ ]` Audit all controller actions for sync-over-async patterns. The fix lands as a natural part of that audit. Adding a specific `[ ]` MovementsController.Edit sync-call line to Stage 9.3's checklist this commit, so the audit can't close without addressing this case."

**Why valid:** Already-scheduled is named with a specific stage and task, the receiving stage gets a more-specific `[ ]` line in the same commit, and the audit itself is the tripwire (it can't close while the line is unchecked).

---

### Example 4 — INVALID, the cited regression

**Situation (from a prior session):** Five UX-adjacent findings discovered during code review at the end of Phase 1.

**My (wrong) proposal:** "The five items above are real findings BUT they don't change the structural decisions Phase 2 will build on. The right move is to proceed with the originally planned next step (the CSRF ADR + deferral audit) and batch these UX-adjacent findings as a 'Phase 1 polish + bugfix' follow-up. That follow-up has to land before Phase 2's plan-writing completes."

**Why rejected:**
1. "Don't change structural decisions" is the canonical rejected reason.
2. "Polish + bugfix follow-up" is a label, not a named open stage.
3. No mechanical tripwire — relies on the user remembering.
4. The user's pushback confirmed it: they had to find the deferred items from memory.

**Correct action:** Immediately open the "Phase 1 polish + bugfix" batch stage in `roadmap-phase-one.md`, add all five items as `[ ]` lines, work the batch to completion before any Phase 2 planning. If any item needs a tooling gap deferral within the batch, apply this skill recursively.
