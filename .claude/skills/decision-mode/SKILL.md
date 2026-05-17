---
name: decision-mode
description: Use when the user is acting as a tech lead / project manager and asking for a fast architectural or scoping decision — "should we", "is it worth", "does this fit the sprint", "is this a refactor", "what's the impact", "make the call", "your recommendation", "ship this now", "defer this". Also fires when proposing options A/B/C, when explaining the cost/scope of a stage or feature, or when the user invokes the `/plain` slash command. Forces the three-layer explanation style — container → term → why-here — with no jargon inside the explanation. Goal: the user can make the call without having to decode framework names.
---

# decision-mode

You are explaining something the user needs to act on — fit-the-sprint, fit-the-stage, refactor-vs-feature, ship-vs-defer, A/B-tradeoff. The user is in **tech-lead mode**, not implementer mode. They could decode the jargon, but decoding eats decision time. Your job is to make the call's cost and consequences readable in one pass.

This skill is **flexible** — adapt the style to the question — but the three-layer rule is rigid. Do not skip layers.

## Two existing memories that frame this

- [feedback_status_updates_in_plain_language](file://~/.claude/projects/<project-slug>/memory/feedback_status_updates_in_plain_language.md) — "User is an engineer leading and delegating, NOT embedded. Bar is brief the tech lead. Lead with observable problem + impact, then plain-English mechanism, THEN class names in backticks."
- [feedback_explain_review_items_in_plain_language](file://~/.claude/projects/<project-slug>/memory/feedback_explain_review_items_in_plain_language.md) — "Lead with user-facing consequences not framework mechanics; translate class names to verbs; state both sides of each trade-off in plain English."

This skill extends both into the harder case: explaining architectural choices under time pressure without dodging the technical content.

## The three-layer rule

When a technical term appears in a decision-asking reply, it MUST be introduced in three layers, in this order:

### Layer 1 — Container
**Ground the larger system the term lives inside.** Before you can explain what `MapWhen` is, the user has to know what a request pipeline is. Before `SecurityStamp`, what a logged-in session is built on. Before `RowVersion`, what optimistic concurrency means.

If you don't define the container, the term is floating. The user doesn't know what *kind* of thing it is. One sentence is usually enough — sometimes two — and it can lean on analogy.

### Layer 2 — Term
**Define the term in plain English, mechanically.** What it does, in operational terms. No jargon inside the definition (see `references/forbidden-words.md`). If you reach for a technical word to define this one, that's a sign you need to either define that word too, or pick a different way to explain.

The definition is the *mechanism*, not just an analogy. Analogies are fine to add ("it's an if-statement for which code handles the request") but they don't replace the definition — the user has to know what the thing actually does. Otherwise next time they see it in code they're no further along.

### Layer 3 — Why-here
**State why this term is showing up in this specific decision.** A generic definition tells the user what the thing is; the why-here tells them why it's the right tool *for the problem on the table*. This is the layer that turns a definition into a decision input.

The why-here is also where you put the cost signal — is this a sprint-sized thing, a stage-sized thing, a roadmap-sized thing, or a refactor that touches everything.

## The forbidden-words rule

The list lives in `references/forbidden-words.md`. Short version: inside Layer 2's definition, do not use **middleware**, **pipeline**, **primitive**, **predicate**, **handler**, **wired up**, **under the hood**, **DI**, **lifecycle**, **principal**, **scope** (as a noun), **DbContext**, **policy**, or **idempotent** — unless you have just defined that word in the same paragraph.

The list is project-specific. Add to it when the user points out a term that slipped through.

## Required structure for decision-asking replies

```
## The decision in one sentence
<plain English, no class names, no framework verbs. What the user is being asked to choose.>

## Options
| Option | What it means | Cost | Risk |
|--------|---------------|------|------|
| A — <name> | <one sentence in plain English> | <sprint / stage / phase> | <one sentence> |
| B — <name> | ... | ... | ... |

## My recommendation
<name + one-line reason>

## The technical detail (only if needed for the call)
<three-layer treatment of each term that matters; class names in backticks; greppable names preserved>
```

Adapt the structure to the question — not every decision has 2+ options, not every reply needs the technical-detail section. But when in doubt, lead with **the decision in one sentence** and pull the jargon to the bottom.

## Cost signals (use these exact phrases when possible)

- **Sprint-sized** — fits in this stage; one to three commits.
- **Stage-sized** — needs its own stage in the current phase.
- **Phase-sized** — won't fit in the current phase; should go to `planning-phase{N}.md` or `planning-future.md`.
- **Refactor-class** — touches many files / many subsystems; bigger than the originating feature.
- **Unknown until investigated** — say so explicitly. Don't fake a sizing.

The user uses these to make the call. Be honest about which one — overstating "sprint-sized" wastes a stage; understating costs you trust.

## When to invoke the skill

- **Automatic** (UserPromptSubmit hook in `hooks/decision-detect.js`): the user's prompt matches a decision-asking phrase. Hook prepends an additionalContext reminder.
- **Manual `/plain`** (`.claude/commands/plain.md`): the user invokes it to retrofit your previous reply into this style. Useful when you shipped jargon and the user needs to make a call now.
- **Self-triggered**: any time you're about to type "should we [option A] or [option B]" / "is this worth it" / "this is bigger than it looks" — invoke the skill first.

## Anti-patterns this skill forbids

- **Term-without-container.** Naming `MapWhen` without first saying what a request pipeline is.
- **Container-without-term.** Hand-waving "the request pipeline handles it" without naming the actual mechanism.
- **Define-by-synonym.** "`MapWhen` is a way to branch the pipeline." That's two jargon words pretending to be a definition. The reader still doesn't know what either means.
- **Why-here as the user's job.** "Use `MapWhen` here." Why? — the skill says you owe the reason, not the user.
- **Hiding cost.** "We could also..." with no sizing. Every option has a cost; if you don't know it, say "unknown until investigated."
- **Apologizing for length.** A three-layer explanation is sometimes 5 sentences. That's correct. Don't pre-trim it to look terse — the user asked for a decision, not a haiku.

## Worked example — the actual MapWhen case from this session

**Bad (what I wrote originally):**
> The fix uses `MapWhen` (an ASP.NET Core branch-the-pipeline primitive) to send only asset-file requests to Vite.

Four jargon words in the parenthetical — `branch`, `pipeline`, `branch-the-pipeline`, `primitive`. The user pushed back: "under that context what does branch mean? What does pipeline mean? How do they make sense conjointed? I know what a primitive is but in this context I don't know how it fits."

**Good (three-layer):**
> The fix uses **`MapWhen`**.
>
> **Container.** ASP.NET Core handles a request by passing it through an ordered list of small steps — authentication, logging, routing, etc. Each step can either handle the request, hand it to the next step, or do both. That ordered list is called the **request pipeline**.
>
> **Term.** `MapWhen` is an instruction that says: "starting at this point, if the request matches some condition, run a separate short list of steps just for it; otherwise leave the main list alone." So in our code, requests whose URL starts with `/src/`, `/@vite/`, `/node_modules/` get diverted into Vite's mini-list of steps that serves dev assets. Everything else — `/login`, `/app/`, `/` — stays on the main list and reaches MVC routing as usual.
>
> **Why here.** Without `MapWhen`, Vite's middleware sat on the main pipeline and answered every URL — including `/` and `/app/login` — by serving its root `index.html`. That hijacked routes that AppController owns. We needed to fork the request flow for asset URLs only. `MapWhen` is the built-in way to do that conditional fork without rewriting the rest of the request handling.

Three layers. No undefined jargon. Reader can now make the call: is this fine for the stage, or is it a sign of a bigger structural issue?
