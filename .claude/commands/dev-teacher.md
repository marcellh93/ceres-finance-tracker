---
name: dev-teacher
description: After a coding session, routes what was learned into the structured developer guide at docs/guide/. Use this skill when the user asks to document, journal, or write up what was learned or explained in a session — NOT for building or explaining code live. Triggers on phrases like "write up what we covered", "update the guide", "document what we built", "summarise the session for my notes".
---

This skill runs _after_ a coding session. It does not build or run code. Its job is to extract concepts, patterns, and decisions from the session and route them into the correct topic files in `docs/guide/`.

## Steps

1. **Review the session silently.** Identify every concept explained, every pattern used, every command run, and every decision made.

2. **Read `docs/guide/syllabus.md`** to find which module files are relevant. Identify the 1–3 topic files that best cover what was discussed.

3. **For each identified file:**
   - If the file **exists**: read it, then append new material under the correct section headings. Do not duplicate concepts already documented.
   - If the file **does not exist**: create it with the standard structure below, then update `syllabus.md` to mark it ✅.

4. **Write content using this structure** (include only sections that have material):

```markdown
# [Topic Title]

[One-sentence description of what this topic covers.]

---

## Concepts

### [Term or Concept Name]
[Plain-English definition. Why this concept matters in this stack. One or two paragraphs max.]

```csharp
// Annotated code example if the concept is best shown in code
```

## Key Patterns

### [Pattern name]
[What problem this pattern solves, and why it was used this way.]

```csharp
// Key code with inline comments explaining non-obvious choices
```

## Commands

| Command | What it does |
|---------|-------------|
| `command --flag` | What the command does. For every flag, explain what it means and why it was used. |

## Pitfalls

- **[Pitfall name]**: What goes wrong and why. How to avoid it.
```

5. **Reply with only:** `✅ Guide updated — [list of files changed], [date]. [N] concepts added.`

## Rules

- Never build or run code — only document what was already covered in the session
- Read the target file before writing — never duplicate a concept already there
- Append to existing sections — never overwrite or restructure content that already exists
- Route to the most specific file that fits — prefer a narrow topic file over a broad one
- If a concept spans two files (e.g. a DI pattern explained in the context of EF Core), write the core definition in the primary file and add a cross-reference link in the secondary file
- Never add a dated session header — content is organized by topic, not by date
