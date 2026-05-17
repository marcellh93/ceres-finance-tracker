Invoke the `playbook` skill via the Skill tool. Then read `.claude/state/playbook/<session_id>.json` and print the per-session chain-status table per `playbook/SKILL.md` §5.

If the user passed an argument naming a specific phase (e.g. `/playbook pre-stage-close`), drill into that phase only — show its required chain, what's fired so far, and what's missing. Otherwise print all seven phases.

The output format (mirrors `playbook/SKILL.md` §5):

```
Phase progress this session:
  ✓ stage-start (brainstorming fired @ tool_use_index=N)
  ✓ pre-spec-write (verify-against-codebase fired @ N)
  ✓ mid-build (deep-fix-mode auto-fired Kx @ N; no-unjustified-deferrals Mx)
  ✗ pre-stage-close — sync-docs not fired; changelog-sync not fired; K unchecked items in <stage>
  ✗ pre-commit — verify-against-codebase last fired @ N but last code Write @ M (re-verify needed)
  — pre-PR-review — disabled in Ceres
Open deferrals: K (<stage> in <file>, <tripwire> tripwire)
```

Symbols: `✓` (pass), `✗` (fail/missing), `—` (disabled). No emojis (per project Claude.md).

If the state file does not exist (no skill has fired yet this session), output:

```
Phase progress this session: (state file empty)
  — All seven phases are at their initial state. Invoke the relevant skill or wait for the auto-fire hooks.
```

The user's optional argument follows: $ARGUMENTS

If non-empty, scope the report to that phase. If empty, print the full table.
