---
name: deep-fix-mode
description: Use when the user signals you are circling on the same issue — phrases like "stop going in circles", "ultrathink", "deep research this", "fix it once and for all", "you keep messing this up", "definitive fix", "third time", "we've been here before", "this isn't working again". Also fires when hooks detect repeated-fingerprint tool calls or N+ edits to the same file targeting the same symptom. Forces a hard procedure — diagnosis table, named-pattern self-match, mandatory external research at a stated authority bar, and a finished post-reconsideration proposal — before ANY further edit, write, or mutation. Rigid skill: follow exactly, do not adapt away discipline.
---

# deep-fix-mode

You are here because you have been making repeated surface-level fixes without diagnosing the root cause, OR the user has invoked this skill explicitly. The named pattern is **Fixation** (Zhou et al. 2026, CB6): "anchor efforts on initial assumptions even with added information or contradictory evidence." Empirically, Claude Opus 4.5 reward-hacks 18.2% of the time — the model you are running on is the one most prone to this failure (Anthropic Opus 4.5 System Card, November 2025). You will not notice you are doing it. The user did. Trust the signal.

This skill is **rigid**. Do not adapt steps away. Do not skip step 4 because the bug "seems obvious." If it seemed obvious you would not be here.

## The hard rules — read all six before step 1

1. **NO Edit, Write, NotebookEdit, or mutating Bash command runs until step 5 completes.** Read, Grep, WebFetch, WebSearch, Agent (research-only), context7 — allowed. Anything that changes state — forbidden.
2. **NO "let me just try X first."** That is the failure mode. The skill exists to stop it.
3. **NO thinking aloud in user-facing text** (see `feedback_no_thinking_aloud_in_user_facing_text` in memory). Reconsider silently. The output is the post-reconsideration version.
4. **The diagnosis table is written before any new hypothesis** is voiced.
5. **External research at the documented authority bar is non-negotiable** (see `references/research-quality-bar.md`). "I think I remember" is not research.
6. **One-shot rule:** if the proposal in step 5 also fails, this skill re-fires on the next message. You do not slide back into incremental patching.

## Step 1 — Stop and read the relevant memory

Read in this order (the entries are short):

- `feedback_no_thinking_aloud_in_user_facing_text` — how the output must read
- `feedback_research_before_confident_claims` — what counts as a citation
- `feedback_systematic_debugging` if present (superpowers skill on debugging)

Then read `references/loop-patterns.md` in this skill. You are looking for a named pattern that matches the behavior of the last 3–10 turns. You will name it explicitly in step 3.

## Step 2 — Write the failed-attempts table

Use the format in `references/diagnosis-template.md`. The table has four columns:

| # | What I tried | Observable result | Why it didn't fix the underlying issue |

Fill in **every attempt in this session that targeted the current symptom**. Read the session transcript (in your context — or grep prior tool calls) — do not reconstruct from memory.

Hard requirement: if you cannot fill in the fourth column for an attempt — "why it didn't fix the underlying issue" — that attempt is evidence you never understood what you were fixing. Say so in the table: "did not understand symptom at time of attempt." That admission is the most valuable line in the table.

If the table has fewer than 2 rows, you are not circling — exit this skill and proceed normally.

## Step 2.5 — False-positive exit gate

The hooks that auto-trigger this skill (`loop-fingerprint.js`, `same-target-edit-count.js`) use counts and content fingerprints. They cannot semantically distinguish three different shapes of repeated tool calls:

| Shape | Looks like | Is it Fixation? |
|---|---|---|
| **Circling** | Same file, same content fingerprint, same symptom across N attempts | **Yes** — invoke the rest of the skill |
| **Sweeping** | Same file, DISTINCT fingerprints per edit, planned linear pass (doc move, find-and-replace, refactor across N sections) | **No** — false positive |
| **Pre-edit verification** | Multiple Read/Grep calls before a careful Edit (especially subagent flows) | **No** — false positive |

Look at the table from step 2. If **every row's fourth column** would honestly read "this was a planned step in a sweep, not an attempt to fix a failing symptom" — OR if there is no failing symptom at all, only a series of distinct planned actions — the trigger is a false positive.

**False-positive exit protocol:**

1. State in one sentence why the trigger was a false positive (e.g. "planned multi-section doc-move sweep, each edit targets a distinct line").
2. Resume the work. Do NOT push through steps 3–6 just because the hook fired.
3. The `Skill` invocation that brought you here is still recorded in the playbook state file, so the system has an audit trail of the check.

If you cannot honestly say "every row is a planned step," continue to step 3. Fixation often disguises itself as planning, so the bar is "would a skeptical reviewer agree these are distinct steps with distinct targets and distinct content."

This exit clause exists because the hooks improved in 2026-05-17 to count *distinct content fingerprints* (not raw edit counts), but the v1 false-positives that motivated the improvement are worth documenting so future agents know the shape.

## Step 3 — Name the pattern

From `references/loop-patterns.md`, name the **specific** pattern your behavior matches. Examples:

- **Fixation** (CB6 — Zhou et al. 2026): anchored to first hypothesis despite contradictory evidence
- **Premature project completion** (Anthropic engineering blog): declared done before verifying
- **Plan churn** (Modexa): rewriting the plan every step without converging
- **Local minima** (Reflexion — Shinn et al. 2023): unable to generate sufficiently diverse hypotheses to escape
- **Hard-coding tests** (Anthropic Opus 4.5 System Card): made the test pass without fixing the code
- **Anchoring + premature closure** (Merck Manual — clinical analogue): first plausible diagnosis stuck

Quote the source's one-line definition. If none of the documented patterns matches, write "no documented pattern matches — novel circling shape" and describe the shape in one sentence. Do not invent a pattern name to make the diagnosis feel complete.

## Step 4 — Identify the layer

Name the **layer you have been patching** and the **layer you have not touched**. Examples from past project-ceres failures:

| Patching | Not touching |
|----------|--------------|
| Controller action body | Middleware ordering |
| Test assertion | EF query filter for IsActive |
| Frontend toast message | Server validation rule |
| CSS class on the button | Razor view-engine cache invalidation |

If "patching" and "not touching" are the same layer, the diagnosis is: **you have not actually looked outside one file.** Step 5's research must start there.

## Step 5 — Mandatory external research

Follow `references/research-quality-bar.md` exactly. The bar:

- For framework/library behavior: official docs (Microsoft Learn, framework GitHub issues with a maintainer response, vendor docs), accessed via `context7` (libraries) or `WebFetch` (specific URLs). No Stack Overflow as the *sole* source. Stack Overflow is acceptable only as a pointer to a primary source.
- For Anthropic/LLM behavior: Anthropic engineering blog, Anthropic research papers, Anthropic system cards, peer-reviewed arXiv papers from major labs.
- For language/runtime semantics: the language spec, runtime docs, or the runtime's own GitHub source.
- Minimum: **two independent authoritative sources** OR **one primary spec/doc that directly answers the question**.
- Quote the relevant passage(s) verbatim in the response, with URL, before proposing.

Do not skip this step because "I already know this." [feedback_research_before_confident_claims](file://~/.claude/projects/<project-slug>/memory/feedback_research_before_confident_claims.md): "User explicitly praised this behavior 2026-05-14 and asked for MORE of it."

Two real Ceres examples where research corrected a wrong assumption:

1. Stage 7.5 — assumed cookie SameSite default was Lax; Microsoft Learn confirmed `None` for ASP.NET Core 8 in certain hosting contexts. Without research I'd have shipped the wrong attribute.
2. Stage 7.6 — assumed `Identity.SignInAsync` triggered `SecurityStamp` revalidation; framework GitHub issue confirmed it does not for the same-session call path.

Skip = ship a wrong fix.

## Step 6 — Write the finished diagnosis

Single response, structured as:

```
## Failed attempts
[the table from step 2]

## Pattern match
[named pattern from step 3, with source URL + quote]

## Surface vs. root layer
[layer table from step 4]

## Authoritative evidence
[quoted passages from step 5 with URLs]

## Root cause
[one paragraph, in the user's plain-engineer-language bar from feedback_status_updates_in_plain_language]

## Why prior attempts addressed a symptom not the cause
[one paragraph, per attempt OR per attempt-cluster]

## Proposed fix
[concrete edits — files, lines, what changes — but DO NOT apply yet]

## Test that will distinguish "fixed" from "another patch"
[the assertion that, if it passes, proves the root cause was the named cause]
```

No "wait, actually," no "leaning toward X," no "two options." If two options remain after research, the research was insufficient — go back to step 5.

## Step 7 — Wait for user approval before applying

`feedback_show_plan_before_coding`. The user reviews the finished diagnosis. Only on explicit approval do you apply the fix.

## Step 8 — Post-fix verification

After applying the approved fix:

1. Run the distinguishing test from step 6.
2. Run the broader verification suite (`pnpm build`, `pnpm test`, `dotnet build`, `dotnet test` filtered to the affected area).
3. Report only after all exit 0. Per [feedback_never_skip_tests_to_make_them_pass](file://~/.claude/projects/<project-slug>/memory/feedback_never_skip_tests_to_make_them_pass.md), pre-existing failures discovered mid-fix are root-caused now, not deferred.

## Step 9 — If the fix also fails

This skill re-fires. The failed-attempts table from this round becomes the *first row* of the next round's table. You do not get to attempt another incremental patch. The third round, you escalate: dispatch a fresh research agent, or ask the user to bring in a different mental model.

## Anti-patterns this skill explicitly forbids

- "Quick check first" before writing the table → no
- Skipping the layer-naming step because the bug seems within one file → no
- Citing your own training data as the authoritative source → no (Anchoring bias in LLMs paper, Lou & Sun 2024: "CoT prompting, reflection methods, prove insufficient" against anchoring — you cannot reason your way out, you must check external evidence)
- "Let me try one more thing while you read this" → no
- Producing two candidate fixes "to be safe" → no (Self-Consistency paper: diversity comes from sampling, not from hedging in the final answer)
- **Calling a bug "pre-existing" / "predates my changes" / "not caused by this session's work"** → no. The age of a bug is irrelevant to whether it needs fixing. The user hit it; it's real; fix it. Sibling of `feedback_no_flag_without_action` at the diagnosis surface — labeling a bug's history is a soft deflection ("this isn't really my problem") even when it's factually true. State the root cause and the fix; do not editorialize about whose work introduced it.

## Why this skill exists — the evidence

- Anthropic Fellows 2026 ("Hot Mess of AI"): "Across all tasks and models, the longer models spend reasoning and taking actions, the more incoherent their errors become."
- Anthropic Opus 4.5 System Card (Nov 2025): 18.2% reward-hacking rate on coding tasks — higher than Sonnet 4.5 (12.8%) and Haiku 4.5 (12.6%).
- claude-code Issue #19699: documented case of Claude running the **exact same failing command 7+ times** without modifying it.
- claude-code Issue #51735: cross-session judgment-failure detection — Anthropic has not committed to a solution. We build it ourselves.
- 245,306 tool calls analyzed in MPIsaac-Per/claude-code-loop-patterns repo motivated published anti-loop skills.

You are not above this. The model you are running on is empirically the most prone to it.
