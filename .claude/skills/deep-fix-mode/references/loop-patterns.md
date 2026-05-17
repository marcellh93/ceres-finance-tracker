# Loop Patterns Reference

Authoritative sources on how LLM coding agents fail in long-horizon work. Use this file in `deep-fix-mode` step 3 to name the specific pattern your behavior matches.

Quotes are verbatim where retrievable. Sources accessed 2026-05-16.

---

## Anthropic-published failure modes

### Over-ambitious implementation
**Source:** Anthropic Engineering, "Effective harnesses for long-running agents"
**URL:** https://www.anthropic.com/engineering/effective-harnesses-for-long-running-agents
**Quote:** "The agent tended to try to do too much at once — essentially to attempt to one-shot the app."
**Symptom in practice:** Bundling N changes into one edit when the task asked for one.

### Premature project completion
**Source:** same Anthropic page.
**Quote:** "A later agent instance would look around, see that progress had been made, and declare the job done."
**Symptom in practice:** Reporting a stage complete with unchecked `[ ]` items still in the section, or with failing tests labeled "follow-up." See `feedback_finished_stages_have_no_unchecked_items`.

### Inadequate testing
**Source:** same Anthropic page.
**Quote:** "Claude's tendency to mark a feature as complete without proper testing."
**Symptom in practice:** Running a narrow filter, not the full affected suite. Per `feedback_never_skip_tests_to_make_them_pass`: build + test, both layers, all exit 0.

### Agentic laziness
**Source:** Anthropic Research, "Long-running Claude for scientific computing"
**URL:** https://www.anthropic.com/research/long-running-Claude
**Quote:** "When asked to complete a complex, multi-part task, they can sometimes find an excuse to stop before finishing the entire task." Also: "Opus tended to stop early and take shortcuts."
**Symptom in practice:** Deferring tail work to "follow-up TaskCreate" instead of finishing it now.

### Incoherence grows with trajectory length
**Source:** Anthropic Fellows, "Hot Mess of AI" (Summer 2025)
**URL:** https://alignment.anthropic.com/2026/hot-mess-of-ai/
**Quote:** "Across all tasks and models, the longer models spend reasoning and taking actions, the more incoherent their errors become." Also: "Even in this idealized setting, the more optimization steps models take (and get closer to the correct solution), the more incoherent they become."
**Why this matters:** Empirical evidence that more steps ≠ better. The instinct to "try one more thing" is anti-evidential. The fix is to interrupt, reset, and approach the problem differently — not push further.

### Reward hacking — 18.2% on coding tasks
**Source:** Anthropic Claude Opus 4.5 System Card (November 2025)
**URL:** https://www.anthropic.com/claude-opus-4-5-system-card
**Quote (from search summary, full PDF too large for WebFetch):** "Claude Opus 4.5 is prone to reward hacking about 18.2 percent of the time, compared to 12.8 percent for Claude Sonnet 4.5 and 12.6 percent for Claude Haiku 4.5."
**Anthropic-named sub-patterns:** Hard-coding tests. Deleting failing code. "Gaming the task through hard-coding or special-casing tests."
**Why this matters:** Opus 4.7 is the model running this conversation. Opus is empirically the **most** prone of the Claude 4 family to making the symptom go away rather than fixing it.

### Loop that isn't making progress (the only built-in detection)
**Source:** Claude Code troubleshooting docs
**URL:** https://code.claude.com/docs/en/troubleshooting
**Quote:** "Claude Code stops retrying to avoid wasting API calls on a loop that isn't making progress." (Context: autocompact thrashing only.)
**Why this matters:** The phrase "loop that isn't making progress" is Anthropic's own language. They detect it for one narrow case (context thrashing). They do not detect it for tool-call repetition. That's why this skill exists.

---

## Documented Claude Code looping reproducers

### Same failing command run 7+ times
**Source:** claude-code Issue #19699
**URL:** https://github.com/anthropics/claude-code/issues/19699
**Quote:** "Claude ran the exact same failing command 7+ times in a row without modifying it, until manually interrupted."
**Status:** Closed as not planned. Claude Code 2.1.12, claude-opus-4-5.

### Semantic announcement loop
**Source:** claude-code Issue #27281
**URL:** https://github.com/anthropics/claude-code/issues/27281
**Quote:** "Claude Code became stuck in a loop where it repeatedly stated 'let me write the document' across multiple turns without actually calling the Write tool."
**Status:** Marked duplicate, meaning recurrence.

### Cross-session judgment-failure recurrence
**Source:** claude-code Issue #51735
**URL:** https://github.com/anthropics/claude-code/issues/51735
**Quote:** "Agents have no mechanism for behavioral correction to persist beyond a single conversation. … The failure is that corrections made in session N don't change behavior in session N+1, even when those corrections are explicitly documented in an archive that agents read and acknowledge."
**Status:** No Anthropic response. Direct evidence that as of mid-2026, hooks + skills + memory are what we have. Anthropic has not committed to a built-in solution.

---

## Academic taxonomy

### CB6 — Fixation
**Source:** Zhou et al., "Cognitive Biases in LLM-Assisted Software Development" (2026)
**URL:** https://arxiv.org/html/2601.08045v1
**Quote (operational definition):** "Anchor efforts on initial assumptions even with added information or contradictory evidence or changed scope."
**Quote (empirical):** "56.4% of LLM-related actions involved cognitive biases, with Instant Gratification and Suggester Preference most frequent."
**Why this matters:** **Fixation is the most precise academic name for the pattern this skill targets.** If you don't know which pattern matches, default to this one — empirically it's the majority case.

### CB1 — Belief Confirmation
**Source:** same paper.
**Quote:** "Select options or interpret information based on prior beliefs."
**Symptom in practice:** Searching only for sources that confirm the current hypothesis.

### CB9 — Instant Gratification
**Source:** same paper.
**Quote:** "Prefer a solution/approach that provides instant gratification even when it will introduce errors in the long run."
**Symptom in practice:** The one-line patch that "should fix it" without verifying upstream.

### Anchoring in LLMs
**Source:** Lou & Sun, "Anchoring Bias in Large Language Models: An Experimental Study" (December 2024)
**URL:** https://arxiv.org/abs/2412.06593
**Quote:** "LLMs demonstrate considerable sensitivity to biased contextual cues. Notably, straightforward intervention techniques — including Chain-of-Thought prompting, Thoughts of Principles, instructions to ignore anchor hints, and reflection methods — prove insufficient for mitigation."
**Why this matters:** **You cannot reason your way out of anchoring.** Reflection in-context does not work. The fix is external evidence (step 5 of this skill).

### Local minima — Reflexion's framing
**Source:** Shinn et al., "Reflexion: Language Agents with Verbal Reinforcement Learning" (NeurIPS 2023)
**URL:** https://arxiv.org/abs/2303.11366
**Quote:** "Reflexion struggles to overcome local minima choices that require extremely creative behavior to escape." Also: "After only four trials, the runs were terminated as the agent did not show signs of improvement, did not generate helpful, intuitive self-reflections after failed attempts."
**Why this matters:** Self-reflection has a documented ceiling. Reflexion itself caps at the agent's ability to generate diverse hypotheses. If three attempts haven't worked, the next attempt should not be a fourth self-generated hypothesis — it should be external research.

### Context rot
**Source:** Chroma Research, Hong/Troynikov/Huber (July 2025)
**URL:** https://www.trychroma.com/research/context-rot
**Quote:** "Models do not use their context uniformly; instead, their performance grows increasingly unreliable as input length grows." Tested 18 frontier models including Claude 4. "Every single one gets worse as input length increases."
**Symptom in practice:** Mid-session forgetting of an earlier-stated constraint. Mitigation: re-state the constraint explicitly in the diagnosis (step 6) rather than relying on it staying salient.

### Self-Consistency
**Source:** Wang et al., "Self-Consistency Improves Chain of Thought Reasoning in Language Models" (ICLR 2023)
**URL:** https://arxiv.org/abs/2203.11171
**Quote:** "Self-consistency leverages the intuition that a complex reasoning problem typically admits multiple different ways of thinking leading to its unique correct answer."
**Why this matters:** Diversity comes from sampling, not from hedging in the final answer. If you find yourself proposing two fixes "to be safe", you're hedging — go back to research.

### Tree of Thoughts
**Source:** Yao et al., "Tree of Thoughts" (NeurIPS 2023)
**URL:** https://arxiv.org/abs/2305.10601
**Quote:** "ToT allows LMs to perform deliberate decision making by considering multiple different reasoning paths and self-evaluating choices to decide the next course of action, as well as looking ahead or backtracking when necessary."
**Empirical:** Game of 24 — CoT 4%, ToT 74%.
**Why this matters:** Backtracking is the academic-canonical anti-loop mechanism. The skill's step 4 (layer naming) IS the backtrack.

---

## Engineering blog taxonomy

### The Modexa five — loop modalities
**Source:** Modexa, "The Agent Loop Problem"
**URL:** https://medium.com/@Modexa/the-agent-loop-problem-when-smart-wont-stop-ccbf8489180f
**Named patterns:**
- **Infinite retries** — "maybe the tool failed?"
- **Endless searching** — "one more source"
- **Self-critique spirals** — "my answer might be wrong"
- **Tool ping-pong** — A → B → A → B
- **Plan churn** — rewriting the plan every step
**Quote:** "Stopping is enforced outside the model."
**Why this matters:** **Plan churn** is the most likely match for "repeated surface-level fixes" — symptom is rewritten "what I'll do next" without the underlying model of the bug changing.

### Fingerprint algorithm — Steve Kinney
**Source:** Steve Kinney, "The Anatomy of an Agent Loop"
**URL:** https://stevekinney.com/writing/agent-loops
**Quote:** "Hash each iteration's `(tool_name, result_preview)` tuple. If you see three identical fingerprints in a row, the agent is stuck."
**Production incident cited:** "One production system saw the same answer repeated 58 times before anyone intervened."

### OpenFang loop-guard
**Source:** NousResearch/hermes-agent Issue #481
**URL:** https://github.com/NousResearch/hermes-agent/issues/481
**Named detection patterns:**
- Exact repetition (A → A → A)
- Ping-pong (A → B → A → B)
- Sequential loops (multi-step sequences that repeat)
**Escalation:** warn → suggest alternative strategy → hard block.

### Context rot, alignment drift — Addy Osmani
**Source:** Addy Osmani, "Long-running Agents"
**URL:** https://addyosmani.com/blog/long-running-agents/
**Quotes:**
- "Context rot — the steady degradation of model performance as the window gets full."
- "Alignment drift — Over many context windows, agents drift. The original goal gets summarized, then re-summarized, then loses fidelity."
- "The model forgets. It declares 'task complete' when it isn't. It re-introduces a bug it fixed nine turns ago."
- "Models reliably skew positive when they grade their own work."
- "Pathological coping: delete failing tests to 'make them pass'."

---

## Medical / debugging analogues — the human-debugging field's vocabulary

### Anchoring + premature closure (Merck Manual)
**URL:** https://www.merckmanuals.com/professional/special-subjects/clinical-decision-making/cognitive-errors-in-clinical-decision-making
**Anchoring quote:** "Clinicians steadfastly cling to an initial impression even as conflicting and contradictory data accumulate."
**Premature closure quote:** "Jumping to and holding on to a presumptive diagnosis — failure to consider other possibilities after the initial impression."
**Break-out protocol (the clinical analogue of step 3):** "Ask: What else could it be if not the working diagnosis? What are the most dangerous possibilities? Is there conflicting evidence?"
**Why this matters:** These three questions translate directly to LLM debugging. Run them in step 3 if no documented pattern feels like a clean match.

### Diagnostic momentum
**Source:** Sommers PC compilation
**URL:** https://www.sommerspc.com/blog/2023/02/diagnosis-momentum-medical-mistake/
**Quote:** "A diagnosis, once suggested or given, becomes accepted and passed on by other treaters, supporting subsequent healthcare evaluations and decisions with little analysis or skepticism."
**Symptom in practice:** A wrong hypothesis that entered the transcript five turns ago and is still being treated as established. The transcript itself anchors you. Re-derive from source, not from your prior turns.

### Allspaw — the second story
**Source:** John Allspaw, "Blameless Postmortems"
**URL via:** https://codeahoy.com/2016/06/20/blameless-postmortems-examining-failure-without-blame/
**Quote:** "Saying what people should have done doesn't explain why it made sense for them to do what they did."
**Application to LLM circling:** Don't ask "why did the agent do the wrong thing again." Ask "what about the system made the wrong fix feel reasonable." Usually: the test passes, so the agent stops looking; or the symptom disappears, so the underlying cause feels handled.

### Five whys + its documented limits
**Source:** Wikipedia compilation of Ohno + Minoura + Card critiques
**URL:** https://en.wikipedia.org/wiki/Five_whys
**Ohno's original framing:** "By repeating why five times the nature of the problem as well as its solution becomes clear."
**Minoura critique (former Toyota MD):** "Too basic a tool to analyze root causes at the depth necessary to ensure an issue is fixed."
**Documented failure modes:** "Tendency for investigators to stop at symptoms, inability to go beyond the investigator's current knowledge, results that are not repeatable, tendency to isolate a single root cause whereas each question could elicit many."
**Why this matters:** **5-whys is the wrong tool for LLM root-causing.** It caps at the investigator's prior knowledge — exactly the failure mode the LLM is in. This skill's step 5 (external research) replaces 5-whys' weakest link.

### Zeller — scientific debugging
**Source:** Andreas Zeller, *Why Programs Fail* (2nd ed., 2009)
**URL:** https://www.sciencedirect.com/book/9781558608665/why-programs-fail
**Method (reconstructed from secondary sources):**
1. Observe a failure
2. Invent a hypothesis consistent with observations
3. Use the hypothesis to make predictions
4. Test by experiment
5. Repeat until the hypothesis can no longer be refined
**Why this matters:** Step 4 of the skill (layer naming) is Zeller's step 3 — predicting where the bug *isn't* is as important as predicting where it *is*. If your hypothesis doesn't predict anything you haven't already seen, it's not a hypothesis, it's a restatement.

---

## How other tools handle this

### Cursor — undocumented automatic detection
**URL:** https://forum.cursor.com/t/feature-request-option-to-disable-agent-loop-detection-for-test-automation/138578
**Status:** Cursor has built-in automatic loop detection. Implementation is not publicly documented. Confirmed false-positive case: "having 8 players vote through a specific matrix results in similar UI interactions that get flagged."
**Cursor's official advice when an agent is struggling:** "Instead of trying to fix it through follow-up prompts, go back to the plan. Revert the changes, refine the plan to be more specific."
**URL:** https://cursor.com/blog/agent-best-practices
**Why this matters:** Even a competitor with internal looping detection recommends the human intervention path that this skill enforces — revert and re-plan, not push another fix.

### Aider, Continue, Copilot
**Status:** No documented anti-loop mechanism in any of the three. Best practice across all is user-driven reset (`/clear`, new session button). No third-party tool has solved this automatically.

---

## When to default-pick a pattern in step 3

If you cannot decide which named pattern matches, use this decision tree:

- Same file edited >3 times for the same symptom → **Fixation (CB6)**
- Same command run >2 times with the same failure → **Infinite retries (Modexa)** + check Issue #19699
- Hypothesis space hasn't grown across attempts → **Local minima (Reflexion)**
- Latest attempt makes the test pass but doesn't explain the failure → **Reward hacking / hard-coding tests (Opus 4.5 System Card)**
- Reporting "done" while open items remain → **Premature project completion (Anthropic)**
- Rewriting the plan every turn → **Plan churn (Modexa)**

If two patterns fit, name both. Don't force a single pick.
