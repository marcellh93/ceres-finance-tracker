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

## What the hook does NOT catch

- Conversational deferrals in user-facing text (not a tool call → no hook fires). The skill rule covers these — invoke any time the proposal contains deferral intent.
- Creative phrasings that miss every regex. Same coverage rationale.
- Deferrals split across multiple writes (one write adds the rationale, a later write adds the entry). The hook fires on each write that matches; a partial match still fires.

## Why these phrases

Every phrase in the list comes from:

1. The cited regression example ("doesn't change structural decisions", "Phase X polish + bugfix follow-up").
2. The "rejected list" in `references/valid-reasons.md`.
3. Existing memory entries documenting deferral failure modes (`feedback_persist_deferred_decisions`, `feedback_defer_work_to_all_three_docs`, `feedback_deferral_requires_receiving_stage_checkbox`).

If a future deferral slips through and the user catches it, add the new phrase here.
