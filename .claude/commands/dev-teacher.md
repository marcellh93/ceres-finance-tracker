---
name: dev-teacher
description: After a coding session, extracts the theory behind what was built and writes it to learning-journal.md. Use this skill when the user asks to document, journal, or write up what was learned or explained in a session — NOT for building or explaining code live. Triggers on phrases like "write up what we covered", "update the learning journal", "document what we built", "summarise the session for my notes".
---

This skill runs _after_ a coding session. It does not build or run code. Its job is to extract the theory from what happened and write it to `learning-journal.md`, including key code produced during the session with the reasoning attached.

## Steps

1. **Read back through the session silently.** Identify: what was built or changed, every concept/term/pattern that appeared, every decision and why it was made, key code produced, every command run in the terminal, anything flagged for an ADR or doc update, and what should come next.

2. **Append a dated entry to `learning-journal.md`** using the template below. If the file doesn't exist, create it with `# Learning Journal` as the first line.

```markdown
## [Date] — [Component or feature name]

### What was built

[One paragraph describing what the session produced, in plain English.]

### Concepts covered

**[Term]** — [One plain-English sentence defining what this is and why it matters here. Define every term that appeared — nothing is too basic to include.]

### Key code produced

[Snippet from the session, with an explanation of why it was written this way attached.]

### Commands run

- `[command]` — [What this command does. For every flag used, explain what it means and why it was included.]

### Decisions made

- **[Decision]**: [What was decided and why. Reference the relevant doc or ADR if one exists.]

### Logic explained

[Only include if a non-obvious process was walked through — e.g. a migration, a query pipeline, an MVC lifecycle. Write it as a numbered plain-English sequence.]

### Open items

- [Anything flagged for an ADR, doc update, or future session]

### What to cover next

[One or two sentences on the natural next step based on this session.]
```

3. **Reply with only:** `✅ Learning journal updated — [component name], [date]. [N] concepts documented.`

## Rules

- Never build or run code — only document what was already produced in the session
- Always append — never overwrite existing entries
