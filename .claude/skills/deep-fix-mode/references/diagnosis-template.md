# Diagnosis Template

The fixed-structure output format for `deep-fix-mode` step 6.

You will produce **one response** matching the structure below. Per `feedback_no_thinking_aloud_in_user_facing_text`, no mid-paragraph reconsideration, no "wait, actually," no hedging between two options. The user reads this as a finished diagnosis.

---

## Required structure (copy this scaffolding verbatim)

```markdown
## Failed attempts

| # | What I tried | Observable result | Why it didn't fix the underlying issue |
|---|--------------|-------------------|----------------------------------------|
| 1 | <action with file/line if applicable> | <exact error or wrong behavior> | <one sentence; "did not understand symptom at time of attempt" is acceptable> |
| 2 | ... | ... | ... |
| 3 | ... | ... | ... |

## Pattern match

**Named pattern:** <e.g. "Fixation (CB6 — Zhou et al. 2026)">

**Source quote:** "<verbatim quote>" — <URL>

**Why my behavior matches:** <one sentence connecting attempts 1–N to the pattern's definition>

## Surface vs. root layer

| Patching | Not touching |
|----------|--------------|
| <layer I've been editing> | <layer I have not investigated> |

## Authoritative evidence

> [<source title>](<URL>) accessed <date>: "<verbatim quote>"
>
> [<source title>](<URL>) accessed <date>: "<verbatim quote>"

<one sentence per source: why this passage settles the question>

## Root cause

<one paragraph, plain-engineer language per `feedback_status_updates_in_plain_language`. Lead with observable problem + impact. Then mechanism. Class names in backticks at the end.>

## Why prior attempts addressed a symptom not the cause

<one paragraph OR one bullet per attempt-cluster. Connects the failed-attempts table to the root cause — shows what each attempt fixed (or appeared to fix) and why that was downstream of the real issue.>

## Proposed fix

**Files and changes:**
- `<absolute path>` — <what changes, line range if known>
- `<absolute path>` — <what changes>

**Do not apply yet.** Awaiting approval.

## Distinguishing test

<one assertion or one specific manual check that — if it passes — proves the root cause was the named cause, not coincidence.>

If this test still fails after the fix, the diagnosis was wrong. Re-enter `deep-fix-mode`.
```

---

## Filling rules

### The failed-attempts table

- **Read the session transcript or your own prior tool calls.** Do not reconstruct from memory — context rot (Chroma 2025) means earlier turns are unreliable in long sessions.
- **Include attempts even if they "partially worked."** Partial fixes are often the strongest evidence of a symptom-vs-cause confusion.
- **The fourth column is the most important.** If you find yourself writing "did not understand symptom at time of attempt" for 2+ rows, the pattern is **Fixation** by default — the hypothesis space never grew.
- **Minimum 2 rows.** If you have fewer than 2 attempts on the current symptom, you are not circling. Exit the skill.

### The pattern match

- Pick from `references/loop-patterns.md`. If two patterns fit, name both — do not force a single pick.
- The source quote must be verbatim. If you can't quote it, you haven't read the source carefully enough — go re-read.
- If no documented pattern matches, write "no documented pattern matches — novel shape" and describe the shape in one sentence. Then proceed. **Do not invent a pattern name.**

### The layer table

- "Patching" = the file/layer/module you have been editing.
- "Not touching" = the upstream or sibling layer the bug most likely lives in.
- If "patching" and "not touching" name the same layer, write: "I have not looked outside this layer." That admission is the diagnosis.

Examples drawn from past Ceres failures (illustrative):

| Patching | Not touching |
|----------|--------------|
| `AccountsController.cs` action body | `Program.cs` middleware ordering |
| `RecurringLayout.test.tsx` assertions | `fetch-mock` global teardown in `setup.ts` |
| `Amount.razor` formatting binding | `NumberFormatActionFilter.cs` anonymous-request guard |
| `app.css` Tailwind utilities | Vite chunk size budget config |

### Authoritative evidence

- Each cited source needs URL + verbatim quote + access date.
- Per `research-quality-bar.md` — minimum two sources OR one primary spec.
- If only one source available and it's a primary spec, state explicitly: "Single primary source — first-party spec, no second source needed."

### Root cause

- **Plain-engineer language.** User is leading and delegating, not embedded. Bar is "brief the tech lead." Lead with observable problem + impact, then plain-English mechanism, then class names in backticks. One new term per paragraph, defined inline.
- **One paragraph.** Not bullets. Not multiple paragraphs. If it doesn't fit in one paragraph, the root cause is still too vague.

### Proposed fix

- **Concrete files and line ranges.** Not "update the controller" — `AccountsController.cs:42-58`.
- **No code mutations yet.** This is a proposal awaiting approval per `feedback_show_plan_before_coding`.
- **No alternative options.** If you find yourself listing A or B "to be safe," the diagnosis is incomplete — go back to step 5.

### Distinguishing test

- One specific assertion that distinguishes "root cause was X" from "I patched another symptom."
- Manual test is acceptable if no automated test exists — but it must be specific. "Click the button and see if it works" is not specific. "Click Submit on the Movement Edit form when `ExitWorktree` returns 200, confirm the toast shows 'Saved' within 1 second AND `Network` tab shows no 4xx" is specific.

---

## What this template forbids

- **No "Summary" section.** The structure above IS the summary.
- **No leading apology.** "I should have caught this earlier" / "you're right that I've been circling" — cut. Per `feedback_on_pushback_propose_dont_apologize`. Lead with diagnosis.
- **No closing "let me know if..."** The next action is the user's approval of the fix. Don't ask "should I proceed."
- **No emojis.** Per system instruction.
- **No marketing language.** "Robust", "comprehensive", "production-grade" — cut. State the fix.
