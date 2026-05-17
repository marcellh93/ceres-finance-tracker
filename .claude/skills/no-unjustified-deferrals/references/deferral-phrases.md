# Deferral Phrases — Hook Regex List

The pre-write hook watches `Edit|Write|MultiEdit` calls and inspects the `new_string` / `content` payload for these phrases. When matched against a write into a file under `docs/`, the hook emits an additional-context block requiring the `no-unjustified-deferrals` skill to be invoked before the write is approved.

The hook does NOT block the write. It prepends context. False positives cost a paragraph; false negatives cost a forgotten bug.

## Phrase list (regex)

```
/doesn'?t change (the )?structural/i
/doesn'?t block (Phase|Stage)/i
/Phase \w+ polish/i
/follow[- ]?up:/i
/(deferred|punted|kicked) to (?!stage)/i        // allow "Deferred to Stage X"
/we can address (this|that|it) later/i
/we can batch (this|that|these)/i
/out of scope for (this|the current) (stage|sprint)/i
/(?<!high )low priority/i
/(?<!isn'?t )not blocking/i
/cosmetic only/i
/UX[- ]only/i
/nice[- ]to[- ]have/i
/can wait until/i
/will be addressed when/i
/we should revisit/i
/let'?s circle back/i
/track(ed|ing) (in|on) the spec/i               // tracking in specs is rejected per feedback_persist_deferred_decisions
```

## Allowed-pattern guard

The hook does NOT fire when the WRITE also contains all three of these markers near the deferral phrase (within ±20 lines):

1. A receiving-stage reference matching `/Stage \d+(\.\d+)?\b/`
2. A checkbox line matching `/^\s*-\s+\[ \]\s+/`
3. Either the word "tooling" (Reason 1) or "already scheduled" / "scheduled in" (Reason 2)

This lets through legitimate deferrals that follow the rules; it blocks the lazy "polish later" / "follow-up:" / "doesn't block Phase 2" pattern.

## Hook output (when triggered)

```
🛑 Deferral language detected in this write.

Matched phrases: [list]
File: [path]

The `no-unjustified-deferrals` skill MUST run before this write proceeds. The two valid reasons are:
  1. Tooling gap — a specific tool/dep/infra/feature you need is unavailable, with evidence
  2. Already-scheduled — the active batch stage has a [ ] line that will fix this as part of its scope

If neither holds, the bug goes into the active batch stage's checklist instead of into a deferral entry. Open the batch stage if one isn't open yet — that's the rule the user agreed to.

If both hold, the deferral entry needs THREE fields: cited reason, receiving-stage [ ] checkbox added in the same commit, and a mechanical tripwire (failing test / architecture assertion / FIXME marker).

Invoke the skill, run the procedure, and rewrite the deferral OR replace it with the fix-now plan before completing this write.
```

## What the docs-write hook does NOT catch

- **Conversational deferrals in user-facing chat text** (no tool call, no hook fires from the docs-write hook). → Covered by the chat-deferral Stop hook below, added 2026-05-17.
- Creative phrasings that miss every regex. Same coverage rationale.
- Deferrals split across multiple writes (one write adds the rationale, a later write adds the entry). The hook fires on each write that matches; a partial match still fires.

## Companion hook — chat-deferral detect (Stop hook, 2026-05-17)

`hooks/stop-chat-deferral-detect.js` runs on every `Stop` event, reads the most recent assistant message from the transcript, and scans for a TIGHT subset of deferral phrases that almost-always indicate deferral intent in chat output. The phrase list:

```
/\bfiled (for|under) (later|a (new|separate|future) (stage|phase|sprint))\b/i
/\bqueue (it|this|that) (for|to) (later|a future|the next session)\b/i
/\bseparate stage'?s? worth of work\b/i
/\bseparate (stage|sprint) of work\b/i
/\bnot a regression\b[\s\S]{0,80}\b(separate|filed|queue|punt|skip|move on)\b/i
/\bout of scope (of|for) (this|the current) (task|stage|sprint)\b[\s\S]{0,80}\b(later|future|next stage|separate)\b/i
/\bPhase \d+ polish (\+|and) bugfix follow[- ]?up\b/i
/\b(kicked|punted|deferred) to (later|a (new|separate|future|next) (stage|phase|sprint))\b/i
```

These are deliberately narrower than the docs-write regex list — every phrase pairs a deferral verb with a temporal anchor ("later" / "future" / "separate stage") so neutral usage doesn't flip on the verb alone.

### Three guards before blocking

The hook does NOT block when ANY of these pass:

1. **USER-AUTH guard** — the user's last message contains explicit deferral verbs (`defer this` / `skip this` / `queue this` / `later` / `not now` / `leave that` / `move on` / `next task`). Explicit user authorization overrides the rule.
2. **FIX-CONTEXT guard** — assistant's message ALSO contains active-work markers (commit SHA, file:line reference, `` ``` `` code block, "running tests", "committing", "fixing it now", properly-formed deferral entry with Stage X + `- [ ]` + Reason 1/2). I'm clearly working, not punting.
3. **DECISION-QUESTION guard** — assistant's message ends with `?` in the last 200 characters. I'm asking the user to decide, not deferring unilaterally.

Only blocks when ALL THREE guards fail: phrase matched, user didn't authorize, no fix-context markers, message didn't end with a question. That's exactly the "unauthorized scoping call" bypass pattern from the audit.

### Behavior

The hook always does two things on every Stop where a phrase matches:

1. **Append a log entry** to `.claude/state/deferral-detect/log.jsonl` with the matched phrases, guard outcomes, message snippets, and the final outcome (`blocked` / `passed` / `bypassed`). Audit trail is uniform across every outcome.
2. **Block the Stop (exit 2 + stderr)** when phrases match AND all three guards fail. The turn is held open until the assistant rewrites the response or sets the bypass var.

Per-session bypass for confirmed false positives: `CERES_SKIP_DEFERRAL_CHAT_HOOK=1` exits 0 unconditionally (logged as `outcome: "bypassed"` so the audit trail still sees it).

No mode flag — the previous `CERES_DEFERRAL_HOOK_MODE` env var was removed 2026-05-17 because it duplicated what the bypass var already does. Two mechanisms for "suppress blocking" were one too many.

## Why these phrases

Every phrase in the list comes from:

1. The cited regression example ("doesn't change structural decisions", "Phase X polish + bugfix follow-up").
2. The "rejected list" in `references/valid-reasons.md`.
3. Existing memory entries documenting deferral failure modes (`feedback_persist_deferred_decisions`, `feedback_defer_work_to_all_three_docs`, `feedback_deferral_requires_receiving_stage_checkbox`).

If a future deferral slips through and the user catches it, add the new phrase here.
