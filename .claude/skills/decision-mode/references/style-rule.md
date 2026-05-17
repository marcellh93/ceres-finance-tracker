# Style Rule — the three-layer pattern in detail

## The pattern

```
┌─────────────────────────────────────────────────────────────┐
│  Layer 1 — CONTAINER                                        │
│  "Here's the larger system this term lives inside."         │
│  One or two sentences. Analogy allowed.                     │
└─────────────────────────────────────────────────────────────┘
              │
              ▼
┌─────────────────────────────────────────────────────────────┐
│  Layer 2 — TERM                                             │
│  "Here is what the term does, mechanically."                │
│  Plain English. No jargon (see forbidden-words.md).         │
│  Analogy ADDITIONAL to the mechanism, not in place of it.   │
└─────────────────────────────────────────────────────────────┘
              │
              ▼
┌─────────────────────────────────────────────────────────────┐
│  Layer 3 — WHY-HERE                                         │
│  "Here is why this term is showing up in THIS decision."    │
│  States the constraint, the alternative, and the cost.      │
└─────────────────────────────────────────────────────────────┘
```

## Layer 1 — Container

The container is the *category of thing* the term is an instance of. The reader's job at this layer is to import a frame.

Examples of well-formed containers:

- For `MapWhen`: "ASP.NET Core handles a request by passing it through an ordered list of small steps. That ordered list is called the **request pipeline**."
- For `SecurityStamp`: "ASP.NET Core Identity lets a user be signed in across browsers/devices simultaneously. Each cookie carries a copy of a value called the **security stamp** so the server can revoke all of them by changing the stamp once."
- For `RowVersion`: "When two requests read the same database row and try to save changes, only one should win. **Optimistic concurrency** is the strategy of letting them both read, but rejecting the second save."

A bad container is:
- One sentence that uses the same terms it's trying to define ("ASP.NET Core has middleware, which is part of the middleware pipeline")
- A pure analogy with no system grounding ("it's like a doorman at a club")
- Skipped entirely — the term is dropped without warning

## Layer 2 — Term

The term layer states what the thing does, in operational terms. The reader's job: now they can predict what the term will do in code they haven't seen yet.

A well-formed Term:
- Names the inputs and outputs in plain English
- States the *condition* under which the term behaves differently (e.g. "if the URL matches" / "if the row version differs")
- Avoids the words in `forbidden-words.md`

Example, well-formed:
> `MapWhen` is an instruction that says: "starting here, if the request matches some test, run a separate short list of steps just for it; otherwise leave the main list alone."

Example, ill-formed:
> `MapWhen` is a way to branch the pipeline conditionally based on a predicate.

The ill-formed version contains *branch*, *pipeline*, *conditionally*, *predicate*. Each one is a word the reader has to look up. Net information: zero.

### When analogy helps

After the mechanism is stated, add an analogy if it sharpens the picture:

> ... `MapWhen` is, in everyday terms, an if-statement for which code handles the request.

Analogy goes *after* the mechanism, never in place of it.

## Layer 3 — Why-here

The why-here is the layer that turns the definition into a decision input. The reader's job: now they can answer "do I want this here, or does this signal a bigger issue?"

A well-formed Why-here:
- Names the alternative — what would have happened without this term
- Names the cost — sprint-sized / stage-sized / phase-sized / refactor-class
- Names the risk if applicable — what breaks if this is wrong

Example, well-formed:
> Without `MapWhen`, Vite's middleware sat on the main list and answered every URL — including `/` and `/app/login` — by serving its root `index.html`. That hijacked routes AppController owns. The alternative would be to give every route a path prefix like `/api/...` and let Vite own everything else — much bigger change. `MapWhen` is sprint-sized; the route-prefix approach is stage-sized.

Example, ill-formed:
> `MapWhen` is the right tool here.

(Why? — the user can't audit this.)

## When to skip a layer

You may skip Layer 1 when:
- The user has already used the container term in this conversation correctly (they've internalized it)
- The term is broadly known outside framework-specific contexts (e.g. "HTTP request", "database table")

You may skip Layer 2 when:
- The term has already been defined in this conversation
- The term doesn't matter to the decision — it's incidental, not load-bearing

You may NEVER skip Layer 3 in a decision-asking reply. If you don't know the why-here, the reply is incomplete; say so explicitly: "I'm not sure why this choice over the alternative — want me to investigate?"

## Linkage to other skills

- **`deep-fix-mode`** — when you're explaining a finished diagnosis (step 6 of that skill), use the three-layer pattern for every named pattern, class, and framework term in the "Root cause" section.
- **`sync-docs`** — when writing planning-doc entries or roadmap items that explain a decision, the same three-layer pattern applies. Future readers (including future you) will not have the conversation context that lets them decode jargon.
- **`brainstorming` skill from superpowers** — Layer 3 (why-here, with cost signal) is what makes a brainstorm-output spec actionable for the user.
