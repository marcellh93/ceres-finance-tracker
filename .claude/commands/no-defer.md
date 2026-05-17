Invoke the `no-unjustified-deferrals` skill via the Skill tool, then apply its six-step procedure to the bug/fix the user just described.

If the user's argument names a specific bug or area, scope the check to that. If empty, scope it to the most recently discussed bug in this conversation (the one that the user is implicitly telling you not to defer).

The procedure (from the skill):

1. **Name the bug** — one sentence. What's broken, where, what cost.
2. **State the two valid reasons verbatim** — Reason 1 (Tooling gap), Reason 2 (Already-scheduled). Copy from `.claude/skills/no-unjustified-deferrals/SKILL.md`.
3. **Check each** — write `Tooling gap: yes/no` and `Already-scheduled: yes/no` with the specific tool/task named.
4. **Branch:**
   - If both no → no deferral. Either fix in current commit, or add a `[ ]` line to the active batch stage (open one if none exists).
   - If yes to either → deferral allowed, write the entry using `.claude/skills/no-unjustified-deferrals/references/deferral-entry-template.md`.
5. **Required fields** when deferring: cited reason, receiving-stage `[ ]` line added in the same commit, mechanical tripwire (failing test / architecture assertion / FIXME marker / CI check).
6. **Output shape** per the skill's "Required reply shape when this skill fires" section.

The user's optional argument follows: $ARGUMENTS

If the user is asking the skill to re-evaluate a deferral they already see in the code/docs (rather than one you're proposing), find the deferral entry, run the same gate against it, and report which requirements pass/fail. If any requirement fails, the deferral is invalid — propose the fix-now plan.
