---
name: sync-docs
description: Review what changed this session and update planning.md, models.md, or create ADRs as needed.
---

Review what changed in this session and update documentation accordingly.

1. Run `git diff --name-only` to see which files changed
2. Run `git diff` to read the actual changes
3. Read doc-agent-instructions.md for routing rules
4. For each changed file, decide if documentation needs updating:
   - New entity or schema change → update docs/models.md
   - New feature or behavior change → update docs/planning.md
   - New architectural decision made this session → create a new ADR in docs/decisions/ following existing numbering
   - Any change touching legal obligations → flag it, do not edit docs/legal.md without my explicit confirmation
5. Apply the updates directly to the relevant files
6. Output a summary: which files were updated and why. If nothing needed updating, say so in one line.

Do not update documentation for: typo fixes, formatting changes, test additions that reveal no new decisions, comment changes.
