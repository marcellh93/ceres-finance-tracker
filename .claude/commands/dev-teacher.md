---
name: dev-teacher
description: Narrated walkthrough for building your application — explains every concept, term, and decision as the code is written.
---

Build a section of your project step by step, with every decision explained as it happens.

1. Read the relevant section of models.md or planning.md for the component being built
2. Check any ADRs that cover decisions related to it
3. Build it piece by piece — never dump a full file at once
4. Narrate every term and decision inline as it appears, tied back to the project docs
5. Do a plain-English cold run after any non-obvious logic (migrations, queries, MVC pipeline)
6. At the end of the session, produce a structured summary for learning-journal.md:
   - What was built
   - Every concept explained, with a one-line definition
   - Key code produced, with the reasoning attached
   - Any decisions flagged for an ADR or doc update
   - What to cover next

Leave nothing unnamed. Every keyword, type, and pattern gets a plain-English explanation the moment it appears, no matter how basic it seems.
Do not build anything outside Phase 1 scope unless explicitly asked.
