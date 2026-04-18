---
name: sync-docs
description: >
  Sync project documentation after a coding session by reviewing git diffs and updating the correct doc files. Covers planning.md, models.md, architecture.md, security-model.md, api-contract.md, multi-tenancy-strategy.md, testing.md, docs/guide/, and ADRs in docs/decisions/. Use this skill at the end of any session where code was written or a design decision was made. Also trigger when the user says "sync docs", "update the docs", "document what we did", or "write up what changed".
---

# sync-docs

Review what changed in this session and update documentation accordingly.

## Step 1 — Read the routing rules

Before touching anything, read `doc-agent-instructions.md` (bundled with this skill) for the full routing table and behavior rules. That file is the source of truth for where each type of change goes. Do not rely on memory or any abbreviated summary — always read it fresh.

Path: `references/doc-agent-instructions.md`

## Step 2 — Get the diff

Run both of these commands:

```bash
git diff --name-only   # shows which files changed
git diff               # shows the actual content of changes
```

**If `git diff` returns nothing** (empty output), it likely means changes were already committed. In that case, try:

```bash
git diff HEAD~1        # compares last commit to the one before it
```

If that also returns nothing (e.g. there's only one commit, or git isn't initialized), ask the user to describe what changed this session and proceed from their description.

## Step 3 — Decide what to update

Apply the routing table from `doc-agent-instructions.md` to each changed file.
Ask: does this change affect schema, behavior, architecture, security, API shape,testing strategy, or a significant decision?

Do **not** update documentation for:

- Typo or formatting fixes
- Bug fixes that don't change documented behavior
- Test additions that reveal no new decisions
- Comment changes

## Step 4 — Apply the updates

Edit the relevant doc files directly. Follow the behavior rules in `doc-agent-instructions.md`, especially:

- Never invent answers — if something is unclear, add it to Open Questions
- Never silently overwrite a prior architectural decision — surface the conflict and wait for confirmation
- legal.md is read-only without explicit human confirmation — flag and stop

## Step 5 — Output a summary

List every doc file you updated and one sentence explaining why. If you created a new ADR, include its number and title. If nothing needed updating, say so in one line.
