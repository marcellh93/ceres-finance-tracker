---
name: changelog-sync
description: >
  Update a project CHANGELOG.md file following the Keep a Changelog standard (https://keepachangelog.com). Use this skill whenever the user says things like "update the changelog", "log what I did today", "end of session changelog", "wrap up the session", "bump the version", or "cut a release".
  Also use it when the user describes changes they made and there's a CHANGELOG.md present in the project. The skill handles two operations: (1) adding entries to [Unreleased] during development, and (2) promoting [Unreleased] to a versioned release at release time.
---

# Changelog Skill

## Purpose

This skill keeps a `CHANGELOG.md` file accurate and well-formatted according to the [Keep a Changelog](https://keepachangelog.com) standard. It does NOT use git commits as the source of truth — the user describes what changed, and this skill formats and inserts it correctly.

## Two operations

### Operation 1 — End-of-session update (most common)

Triggered by: "update the changelog", "log what I did", "end of session", or when the user describes changes they made during a coding session.

**Steps:**

1. Read the existing `CHANGELOG.md` to understand its current state.
2. Ask the user: _"What did you work on this session? Describe the changes and I'll categorize and format them."_ — unless they already described them in the message that triggered the skill.
3. Categorize each change into the correct Keep a Changelog section:
   - **Added** — new features or files that didn't exist before
   - **Changed** — modifications to existing behavior or content
   - **Deprecated** — features that still work but will be removed in future
   - **Removed** — features or files that no longer exist
   - **Fixed** — bug fixes
   - **Security** — security-related fixes
4. Write the entries in plain, user-facing language. Do NOT copy-paste technical internals unless they're meaningful to someone reading the log. Each entry should be one line, starting with a dash (`-`).
   4a. Within each section, group entries by functional module using a bold label. A module is a named area of functionality (e.g. Budgets, Transfers, Accounts) or a technical cross-cutting area (e.g. Data Models, Migrations, Docs, Tests). Format:

   ```
   **Module Name**
   - Entry one
   - Entry two
   ```

   Rules for module grouping:
   - **Before naming any module, read recent entries in the CHANGELOG and list the module names already in use.** Reuse existing names verbatim. Inventing a new name when an equivalent already exists creates inconsistency.
   - Only create a module group if there are one or more entries that belong to it. Do not create empty groups.
   - If a change genuinely doesn't belong to any specific module (e.g. a global config tweak), list it at the top of the section without a group label, before any groups.
   - Ask the user if you're unsure which module a change belongs to or if you're considering a name not already in the file.

5. Insert the new entries under `## [Unreleased]`, under the correct section headers. If a section header (e.g. `### Fixed`) doesn't exist yet under `[Unreleased]`, create it. Maintain this section order: Added, Changed, Deprecated, Removed, Fixed, Security.

   **Phase-subsection convention (Project Ceres specific).** When the project has explicit development phases (Phase 1, Phase 2, Phase 3, etc.), wrap the standard sections in a `### Phase N` subsection inside the version block. The structure becomes:

   ```
   ## [0.3.0] — YYYY-MM-DD

   ### Phase 2

   #### Added
   **Module**
   - Entry

   #### Changed
   ...

   ### Phase 3

   #### Added
   ...
   ```

   The `[Unreleased]` block uses the same `### Phase N` wrapping when entries belong to different phases. This makes phase boundaries explicit in the changelog and matches the project's planning-phase{N}.md document structure. Read recent versioned blocks first to confirm the convention is in use before applying it.
6. Show the user the proposed additions as a formatted list — one bullet per entry, grouped by section and module, exactly as they will appear in the file. Do NOT write to the file yet. Ask: _"Does this look right? I'll write it once you confirm."_
7. Write to the file only after the user explicitly confirms. Never write before confirmation.

### Operation 2 — Cut a release

Triggered by: "cut a release", "bump the version", "release version X", or when the user says they're ready to publish/ship.

**Steps:**

1. Read the existing `CHANGELOG.md`.
2. Look at all entries currently under `## [Unreleased]` and determine the version bump using semantic versioning rules:
   - **Major** (X.0.0) — any entry under `Removed` that breaks existing behavior, or any explicit breaking change
   - **Minor** (0.X.0) — any entry under `Added` with no breaking changes
   - **Patch** (0.0.X) — only `Fixed`, `Changed`, `Security`, or `Deprecated` entries; nothing new was added
3. Propose the version number to the user with a one-line explanation: _"Based on the unreleased entries, I'd suggest version 0.2.0 — you added new features with no breaking changes. Does that work?"_
4. If the user confirms (or provides their own version number), do the following in the file:
   - Replace `## [Unreleased]` with `## [Unreleased]` (blank, no entries)
   - Add a new versioned block immediately below it: `## [X.Y.Z] — YYYY-MM-DD` using today's date in ISO 8601 format
   - Move all former `[Unreleased]` entries into that new versioned block
   - Add a horizontal rule (`---`) after the new versioned block if one isn't already there
5. Show the proposed file diff before writing. Write only after confirmation.

## Format rules (always apply)

- Dates use ISO 8601 format: `YYYY-MM-DD` (e.g. `2026-04-16`). Never use regional formats like `04/16/26` or `16 April 2026`.
- Section headers use `###` (three hashes): `### Added`, `### Fixed`, etc.
- Version headers use `##` (two hashes): `## [Unreleased]`, `## [0.2.0] — 2026-04-16`
- Entries are bullet points starting with `- `
- Never add entries for: whitespace changes, comment-only edits, formatting fixes, or documentation typos — unless the user specifically asks.
- Deprecations and breaking changes must always be logged, even if minor.

## Finding the CHANGELOG file

- Default location: `CHANGELOG.md` in the project root.
- If not found there, search one level deep.
- If multiple projects are open, ask the user which project this is for before reading or writing anything.
- If no `CHANGELOG.md` exists at all, ask: _"I don't see a CHANGELOG.md. Want me to create one from scratch following the Keep a Changelog format?"_

## What NOT to do

- Do not read git log or git diff to determine what changed. The user decides what is noteworthy.
- Do not write to the file without showing a preview and getting confirmation.
- Do not invent version numbers without explaining the reasoning.
- Do not omit the `## [Unreleased]` section after a release — it must always remain in the file, empty, ready for the next cycle.
