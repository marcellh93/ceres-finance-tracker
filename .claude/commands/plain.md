Invoke the `decision-mode` skill via the Skill tool. Then re-emit the IMMEDIATELY PREVIOUS assistant reply using the three-layer pattern from `.claude/skills/decision-mode/references/style-rule.md` — container → term → why-here, with no jargon inside the definitions.

If the previous reply was already in the right style, say so and stop — do not pad. If the previous reply named more than three technical terms, rank them by load-bearing-ness and translate the top three; for the rest, list them with a one-line plain-English gloss but skip the full three-layer treatment.

Required structure for the retrofit:

1. **The decision in one sentence** — plain English, no class names, no framework verbs. What the user is being asked to choose, or what conclusion they're being asked to accept.
2. **Options table** (if the previous reply was comparing options) — `Option | What it means | Cost | Risk`.
3. **My recommendation** — name + one-line reason.
4. **The technical detail** — for each load-bearing term:
   - Layer 1 (Container): one or two sentences naming the larger system the term lives inside.
   - Layer 2 (Term): plain-English definition of what the term does, mechanically. Check `references/forbidden-words.md` — none of those words should appear inside this definition unless you've just defined them.
   - Layer 3 (Why-here): why this term is showing up in THIS decision, with a cost signal (sprint-sized / stage-sized / phase-sized / refactor-class / unknown until investigated).

The user's optional argument follows: $ARGUMENTS

If non-empty, treat it as a specific term or section to focus on — translate only that. If empty, translate the whole previous reply.

If the previous "reply" was just acknowledging or asking a clarifying question (i.e. there was nothing to translate), say so and stop.
