# Research Quality Bar

The non-negotiable evidence standard for `deep-fix-mode` step 5.

Anchoring research from Lou & Sun 2024 (https://arxiv.org/abs/2412.06593): "Chain-of-Thought prompting, Thoughts of Principles, instructions to ignore anchor hints, and reflection methods prove insufficient." You cannot reason your way out of anchoring. External evidence is the only way out. This file defines what counts as external.

---

## The minimum bar

**Either:**
- **Two independent authoritative sources** that agree on the relevant behavior, OR
- **One primary specification or first-party documentation** that directly answers the question.

"I think I remember" is not research. "It probably works like X" is not research. Stack Overflow alone is not research — it is a pointer to research.

If you can't meet the bar in step 5, the answer to "what should I propose" is "I don't have evidence yet." Go research more. Do not propose.

---

## What counts as authoritative — by topic

### Framework, library, runtime behavior

**Primary, in priority order:**
1. Official documentation hosted by the vendor (Microsoft Learn, postgresql.org, react.dev, vite.dev, tailwindcss.com, dotnet.microsoft.com).
2. Official GitHub repo — README, docs/ folder, or a maintainer's explicit answer in an issue. "Maintainer's explicit answer" means a committer/owner badge or a label like `team-response`.
3. The framework's source code, when the doc is ambiguous and the source is the only authority.

**Acceptable as second source, never as sole source:**
- Closed GitHub issue with a community resolution that references the source code.
- A widely cited blog post by a recognized framework contributor (Jon Skeet on .NET, Dan Abramov on React, etc.).
- An RFC or design doc referenced from official documentation.

**Pointer-only — not authoritative on its own:**
- Stack Overflow answers (regardless of votes).
- Reddit threads.
- Tutorial sites (geeksforgeeks, tutorialspoint, etc.).
- LLM-generated documentation summaries.

**Tool to use:**
- `context7` for library docs (per CLAUDE.md and skill system prompt). It pulls current vendor documentation.
- `WebFetch` for a specific URL once you know it.
- `WebSearch` to *find* the authoritative URL — then immediately `WebFetch` the primary source.

### Language and runtime semantics

**Primary:**
1. The language specification (ECMA-262 for JS, C# language spec, PostgreSQL SQL reference, etc.).
2. The runtime/compiler's own documentation.
3. The runtime's source repo when the spec is ambiguous.

### LLM / agent behavior

**Primary, in priority order:**
1. Anthropic's published engineering blog, research papers, and system cards.
2. arXiv papers from major labs (Anthropic, OpenAI, DeepMind, Google Research, Microsoft Research, Stanford NLP, MIT CSAIL, Berkeley AI Research).
3. ICLR / NeurIPS / ACL / EMNLP peer-reviewed papers.
4. Claude Code official documentation (code.claude.com).

**Acceptable as second source:**
- Established AI-engineering blogs with cited primary sources (Steve Kinney, Addy Osmani, lilianweng.github.io).
- Vendor docs for competing tools (Cursor, Aider, Continue) when the question is "what does tool X do."

### Anthropic / Claude Code internals

**Primary:**
1. https://code.claude.com/docs/en/ — official documentation tree.
2. https://github.com/anthropics/claude-code — issues, PRs, source code.
3. Anthropic engineering blog posts at anthropic.com/engineering.
4. Anthropic system cards at anthropic.com (linked from model pages).

**Acceptable as second source:**
- claude-code GitHub issues with reproducer + maintainer comment.
- Third-party reverse-engineering only when the official docs are silent on a specific behavior; flag as "undocumented" in the diagnosis.

### Security and cryptography

**Primary:**
1. RFCs (rfc-editor.org).
2. NIST publications.
3. OWASP cheat sheets and ASVS.
4. The library's own security documentation.

Never rely on Stack Overflow for security claims. Anchoring on a wrong security pattern is more expensive than the research.

### Business or product behavior (when it's relevant)

**Primary:**
1. Project docs under `docs/` in the working repo.
2. ADRs under `docs/decisions/`.
3. Planning docs (planning.md, planning-phase{N}.md, etc.).

For Ceres specifically: `docs/models.md`, `docs/testing.md`, `docs/security-model.md`, `docs/architecture.md`, `docs/api-contract.md`, `docs/design-system.md` are first-party.

---

## What the citation must include

In the diagnosis (step 6 of the skill), each cited source needs:

1. **URL.** Not "the docs said" — the actual URL.
2. **Verbatim quote** of the relevant passage. One to three sentences. If you can't quote it, you didn't read it carefully enough.
3. **Date of access.** Implicitly today's date; explicitly mention if the source is dated more than 2 years old or pre-dates the current major version of the relevant tool.
4. **Why this passage settles the question** — one line connecting the quote to the diagnosis.

Example, well-formed:

> [Microsoft Learn — Cookie Authentication](https://learn.microsoft.com/en-us/aspnet/core/security/authentication/cookie?view=aspnetcore-10.0) accessed 2026-05-16: "The default for `CookieAuthenticationOptions.Cookie.SameSite` in ASP.NET Core 8+ is `SameSiteMode.Lax`."
>
> This contradicts the assumption in attempts 1–3 that the cookie was being sent cross-site by default.

Example, malformed (do not do this):

> The Microsoft docs say SameSite defaults to Lax, so the cookie isn't being sent.

The malformed version is a paraphrase without a URL and without a quote. It is indistinguishable from a hallucination. Per `feedback_research_before_confident_claims`: cite > paraphrase.

---

## When primary documentation is unavailable

If the official doc doesn't cover the case (this happens with edge cases and recent versions):

1. State in the diagnosis: "primary documentation is silent on this. Falling back to source-code inspection."
2. Quote the relevant source lines with the GitHub permalink (include the commit SHA in the URL — `github.com/.../blob/<SHA>/...` not `.../blob/main/...`).
3. State the version/tag the source was inspected at.

If even the source code doesn't settle it, the proposal must include a test that *would* settle it, and the recommendation should be to run that test before committing to either fix.

---

## Anti-patterns

- **"Based on my training data..."** — your training data is not a source. It is a probabilistic recall that may be wrong, outdated, or hallucinated. The Lou & Sun anchoring result says you literally cannot trust your own internal reasoning when biased context is present.
- **Citing a single Stack Overflow answer with 200 upvotes** — popularity is not authority. Find the primary source the answer references.
- **Citing the skill's own `loop-patterns.md`** — that file cites primary sources; cite *those* in the diagnosis, not the meta-file.
- **Hand-waving with "this is how X usually works"** — name the specific X and find the specific documentation.
- **Citing an LLM-generated summary** — including answers from other Claude conversations, Copilot suggestions, or AI-generated documentation. These are downstream of the same training data and have the same anchoring problem.

---

## How to budget time

If after 15 minutes of research you cannot find a primary source that answers the question, the diagnosis is: **the question is mis-framed.** Go back to step 4 (layer naming). The layer you think the bug is in is probably wrong. Reframe the question and re-search.

If after 30 minutes you still have no primary source, escalate to the user: "I've spent 30 minutes looking for authoritative evidence on X and can't find it. Do you have a pointer, or should I propose two named alternatives with a test that distinguishes them?" Be explicit about the dead end. Don't ship a guess as a diagnosis.
