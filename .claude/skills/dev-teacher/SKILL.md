---
name: dev-teacher
description: After a coding session, routes what was learned into the structured developer guide at docs/guide/. Use this skill when the user asks to document, journal, or write up what was learned or explained in a session — NOT for building or explaining code live. Triggers on phrases like "write up what we covered", "update the guide", "document what we built", "summarise the session for my notes". Also triggers on "fill in the gaps", "audit the guide", or "review existing files for missing concepts".
---

This skill runs _after_ a coding session. It does not build or run code. Its job is to extract concepts, patterns, and decisions from the session and route them into the correct topic files in `docs/guide/`.

## Steps

1. **Separate primitives from project patterns.**
   Review the session and split what was covered into two buckets:
   - **Language/framework primitives**: syntax or concepts that exist independently of this project (e.g. lambda expressions, the ternary operator, `Where`, `async/await`). These belong in Module 01–03 files.
   - **Project-specific patterns**: how this project applies those primitives (e.g. balance derivation logic, the `IsSystem` filter, opening balance handling). These belong in Module 04+ files.

   Document primitives first. A project pattern example should never be the first time a concept is introduced — if the underlying primitive has no definition yet, write that first (or create the Module 01 file) before showing the project usage.

2. **Read `docs/guide/syllabus.md`** to find which module files are relevant. Identify the 1–3 topic files that best cover what was discussed.

   **If no existing module fits**, do NOT silently create a new module. The syllabus structure is a project-level decision. Stop and ask the user: name the proposed module, where it should slot in (which module number, which group), and why. Only proceed once the user confirms.

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
[Plain-English definition. Why this concept exists. One or two paragraphs max.]

For syntax concepts (operators, keywords, special characters), always include:
- What it is called
- How to read it aloud
- The simplest possible example before any project-specific usage

```csharp
// 1. ISOLATION EXAMPLE — define the concept with the simplest possible dummy data,
//    no project types involved
```

**Dry run** — [required for any concept that executes: loops, operators, conditionals, method calls]
Show concrete dummy values going in, trace each step explicitly, show the output.
Use a code comment block or a plain-text trace table — whichever is clearer.
Format:
```
Input:  [concrete dummy value]
Step 1: [what happens]
Step 2: [what happens]
Result: [output]
```
For branching concepts (if/else, switch, ternary), trace at least two paths — one that takes each branch.
Skip the dry run only for purely structural concepts (class declarations, interface signatures, attribute syntax) where there is no runtime behavior to trace.

**When to use:** [one or two sentences — what situation calls for this concept, and what to use instead when it does not apply. Always contrast with at least one alternative.]

```csharp
// 2. PROJECT EXAMPLE — show the concept applied in this codebase,
//    with a comment pointing to the file and line
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

---

## Gap Audit Mode

**Only run this mode when the user explicitly asks** (e.g. "fill in the gaps", "audit the guide", "review existing files for missing concepts"). Do not run it on every invocation.

When triggered:

1. Read all existing files under `docs/guide/` (starting with Module 01–04, as they are most likely to have gaps).
2. For each file, identify any concept that is *used* in a code example or explanation but never *defined* in the guide — e.g. a lambda appears in a LINQ example but no file explains what `t =>` means.
3. For each gap found, determine the correct file to add the definition (prefer the most foundational module that fits).
4. Read that file, then insert the missing definition before the first place it is used. Do not restructure existing content — insert only.
5. Report all gaps found and filled in the final reply: `✅ Gap audit complete — [N] gaps found, [N] filled. Files changed: [list].`

---

## Rules

- Never build or run code — only document what was already covered in the session
- Read the target file before writing — to avoid duplication and to know exactly where to place new content
- In normal mode, append to existing sections — never overwrite or restructure content that already exists; in gap audit mode, inserting mid-file before the first usage of an undefined concept is allowed
- For primitives vs. project patterns, foundational module wins over specificity: a C# lambda belongs in Module 01 even if it was first encountered in a Module 04 example — use a cross-reference link in the Module 04 file to connect them
- For everything else, route to the most specific file that fits — prefer a narrow topic file over a broad one
- If a concept spans two files, write the core definition in the more foundational file and add a cross-reference link in the other
- Never add a dated session header — content is organized by topic, not by date
- Write for a reader encountering the concept for the first time — define every symbol, keyword, and operator before showing it used in project code
- Every concept that executes at runtime requires a dry run: concrete dummy input → explicit step-by-step trace → output. Skip only for purely structural concepts (class declarations, interface signatures, attribute syntax) where there is no runtime behavior to trace
- Every concept requires a "When to use" line that names at least one alternative — a concept without a decision rule teaches what something is but not when to reach for it
- Code block order within a concept is fixed: (1) isolation example with dummy data, (2) dry run trace, (3) project usage example. Never show project code before the concept has been defined and traced in isolation
