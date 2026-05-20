---
name: fix-interaction
description: Fires when the assistant mentions a fix WITHOUT making the corresponding Edit/Write in the same turn. Forces an explicit fix-or-document interaction before the next subject is allowed to take over the thread. Five-state machine with five dead-lock defenses. Manual override via `CERES_SKIP_FIX_INTERACTION_HOOK=1` or `/fix-interaction-reset`.
---

# fix-interaction

## 1 — Why this skill exists

Pattern under treatment: the assistant identifies a real bug, describes the fix, and ends the turn without (a) doing the fix in-line OR (b) opening a tracking checkbox. The next user message is on a different topic; the bug rides the conversation history into the scrolled-past and never lands as work.

Measured 2026-05-19 in a 10-scenario simulation: **0/10 passes.** Every response said "doing it now" or "let me grep" or "want me to fix this or queue it?"; zero responses converted the verb into a tool call OR opened a real `[ ]` line. The user's verbatim framing: *"the important part is not pointing the fix or the issue out, is acting proactively on it, because if you just reply that, and I get tangled into moving forward with instructions you gave then what happens to that bug/fix?"*

The memory entries `feedback_no_flag_without_action`, `feedback_doing_now_requires_tool_call`, and `feedback_want_me_to_framing_on_bugs` describe this rule. They are insufficient on their own — the simulation showed memory alone scored 0/10. This skill is the mechanical enforcement layer.

## 2 — How it works

Three hooks coordinate a five-state machine via a per-session state file at `.claude/state/fix-interaction/<session_id>.json`.

### State machine

```
none  →  fix-or-document  →  where         →  documenting  →  resume  →  none
                          \                                  /
                           →  fixing  ────────────────────→ resume
                          \
                           →  (released by topic-shift / TTL / bypass) → none
```

- **none** — idle. No interaction in flight.
- **fix-or-document** — the assistant mentioned a fix without acting; the user must answer *fix now* or *document*.
- **where** — the user chose document; the user must name the destination doc + stage.
- **fixing** — the user chose fix; the assistant must land the `Edit` / `Write` next.
- **documenting** — the user named a destination; the assistant must land the `[ ]` line next.
- **resume** — terminal-transient. Emits `additionalContext` with the verbatim prior user message, then auto-transitions to `none`.

### Hooks

1. **`hooks/detect-fix-mention.js`** (Stop event) — scans the assistant's final message for fix-mention phrases. Guards:
   - **FIX-CONTEXT guard** — if the same response contains an `Edit` / `Write` / `MultiEdit` tool call, skip. The fix already happened; no prompt needed.
   - **STATE-NOT-NONE guard** — if `state.awaiting !== "none"`, skip. Already in flight; let the other hooks resolve it.
   - On match + both guards pass: writes `state.awaiting = "fix-or-document"`, captures the user's verbatim prior message + the matched passage, blocks the Stop with a deny reason instructing the assistant to emit the "fix now or document?" question.

2. **`hooks/await-answer.js`** (UserPromptSubmit) — reads the user's reply. Behavior depends on current state:
   - `awaiting === "fix-or-document"`: parse `fix` / `now` / `do it` → transition to `fixing`. Parse `document` / `doc it` / `log it` / `queue it` / `open a line` → transition to `where`. Parse topic-shift markers (see §4) → release to `none`.
   - `awaiting === "where"`: capture the destination (doc path + stage name) → transition to `documenting`. Topic-shift markers → release to `none`.
   - All transitions are idempotent (compare-and-set) to tolerate hook re-fire.

3. **`hooks/check-resolution.js`** (Stop event) — runs when `awaiting === "fixing"` OR `awaiting === "documenting"`. Checks whether the expected tool call landed in this turn:
   - `fixing`: any `Edit` / `Write` / `MultiEdit` tool call → transition to `resume`.
   - `documenting`: any `Edit` / `Write` / `MultiEdit` tool call to `docs/**` → transition to `resume`.
   - `resume` state: emits `additionalContext` with the verbatim prior user message + a "continue from there" line, then transitions to `none`.

## 3 — The five named states (reference)

| State | Set by | Cleared by | TTL |
|---|---|---|---|
| `none` | initial / terminal | n/a | n/a |
| `fix-or-document` | detect-fix-mention | await-answer | 24h |
| `where` | await-answer | await-answer | 24h |
| `fixing` | await-answer | check-resolution | 24h |
| `documenting` | await-answer | check-resolution | 24h |
| `resume` | check-resolution | check-resolution (next Stop) | 24h |

All non-`none` states carry `started_at`. Any hook that observes `now - started_at > 24h` auto-releases to `none` with a log entry — never silently strands the conversation. See `references/dead-lock-defenses.md` §1.

## 4 — The five dead-lock defenses

Distilled from primary-source workflow-orchestration literature (AWS Step Functions, Microsoft Learn Compensating Transaction pattern, Temporal). Verbatim quotes + URLs in `references/dead-lock-defenses.md`.

1. **Per-state TTL (24h).** No state waits indefinitely. Stale states auto-release.
2. **Atomic state writes.** Every state write goes to `<session>.json.tmp`, then `rename()` to the final path. POSIX `rename` is atomic. Corrupt-read fallback: treat as fresh session, never deny-on-parse-failure.
3. **Fail-open on hook error.** If the hook itself throws, exit 0 with a stderr error log. The session continues; the operator sees the error; the gate doesn't strand the workflow.
4. **Topic-shift detection guard.** Before blocking on `await-answer`, check whether the user's message engages with the prompted question. Lexical signals:
   - Engagement keywords: `fix`, `document`, `doc`, `now`, `later`, `where`, the named doc path, any of the stage references.
   - Topic-shift markers: `actually`, `let's`, `move on`, `different question`, `forget that`, `nevermind`, `drop it`, `skip that`.
   - If topic-shift AND no engagement → release the gate to `none`, log the topic-shift, let the user's new prompt through unblocked.
5. **Three-way escape, escalating cost:**
   - (a) `CERES_SKIP_FIX_INTERACTION_HOOK=1` — per-session bypass via env var
   - (b) Delete `.claude/state/fix-interaction/<session>.json` — manual file delete
   - (c) `/fix-interaction-reset` — slash command (calls (b) plus logs the reset)

## 5 — When this skill fires

- **Auto via hooks** — three hooks wired in `settings.json`. The assistant doesn't invoke this skill directly; the hooks read its state and enforce the gate.
- **Manual** — `Skill name=fix-interaction` reads the current state and prints a status table (similar to `playbook`'s §5 output).
- **Slash command** — `/fix-interaction-reset` clears the state file.

## 6 — Fix-mention phrase list

Watched in `detect-fix-mention.js`. Tight subset — every phrase pairs a "fix" semantic with a fix-shaped object:

```
/\bthe fix is\b/i
/\bthe right fix\b/i
/\bI'?d fix this by\b/i
/\bI would fix this\b/i
/\bI'?ll fix this\b/i
/\beasy fix:?\b/i
/\bthat'?s a bug\b/i
/\bthe bug is\b/i
/\bthis is broken because\b/i
/\bwe should fix\b/i
/\bwe need to fix\b/i
/\bwe need to add\b/i
/\bone-line fix\b/i
/\bquick fix:?\b/i
/\bthe right move is\b/i
/\bthe (correct|proper) approach is\b/i
```

Calibration is conservative: false positives prompt a "fix or document?" question that's easy to release via topic-shift; false negatives are silent and the simulation showed those are the costly direction.

## 7 — Anti-patterns this skill explicitly prevents

- **"Want me to fix this or queue it?"** — the framing-side anti-pattern. The hook fires when the assistant mentions a fix without acting; the user's "fix or document?" answer is the explicit handoff the framing-side anti-pattern silently transferred. See `feedback_want_me_to_framing_on_bugs`.
- **"Doing it now"** without a same-response tool call — verb-side anti-pattern. The hook's FIX-CONTEXT guard inverts this: only skip when the tool call is actually present in the same response. See `feedback_doing_now_requires_tool_call`.
- **End-of-turn flag without action** — noun-side anti-pattern. The hook catches the lexical surface even when the assistant doesn't say "doing it now" — any of the §6 fix-mention phrases trigger the gate. See `feedback_no_flag_without_action`.

## 8 — Linked memory

- `feedback_no_flag_without_action` — surface this skill exists to enforce.
- `feedback_doing_now_requires_tool_call` — verb-side rule, enforced via FIX-CONTEXT guard.
- `feedback_want_me_to_framing_on_bugs` — framing-side rule, enforced via the gate itself.
- `feedback_scoping_dodge_is_deferral` — sibling at the multi-issue surface.
- `reference_playbook_skill` — parent orchestrator. This skill is mid-build-phase enforcement; playbook covers cross-cutting cohesion.

## 9 — When NOT to invoke

- The fix happened in the same turn — FIX-CONTEXT guard handles this automatically.
- The "fix" is in fact a clarifying question to the user, not a proposed action — phrase calibration should suppress these, but if not, the topic-shift release handles them.
- The user has explicitly bypassed via env var or `/fix-interaction-reset`.

## 10 — Slash command

`/fix-interaction-reset` mirrors `/deep-fix`, `/plain`, `/no-defer`, `/verify`, `/playbook` symmetry. The command file lives at `.claude/commands/fix-interaction-reset.md`. It deletes the per-session state file with a confirmation log entry.
