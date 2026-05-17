Invoke the `deep-fix-mode` skill via the Skill tool immediately, before any other tool call, before any clarifying question, before any text response.

The user is signaling that you have been circling — making repeated surface-level fixes without diagnosing the root cause. The Anthropic Opus 4.5 System Card (Nov 2025) reports an 18.2% reward-hacking rate on coding tasks for Opus, the highest of the Claude 4 family. Opus 4.7 — the model running this conversation — inherits the propensity. Your own assessment of "I'm not circling" is empirically the failure mode the skill exists to prevent. Trust the signal. Do not push back. Do not "just check one thing first."

After invoking the skill, follow its six-step procedure exactly:

1. **Stop.** No Edit, Write, NotebookEdit, or mutating Bash until step 6 completes. Read, Grep, WebFetch, WebSearch, Agent (research-only), context7 — allowed.
2. **Write the failed-attempts table** per `references/diagnosis-template.md` — four columns (#, what I tried, observable result, why it didn't fix the underlying issue), minimum 2 rows, populated from the actual session transcript not memory.
3. **Name the loop pattern** from `references/loop-patterns.md` with verbatim source quote and URL — Fixation (CB6), Local-minima (Reflexion), Plan-churn (Modexa), Reward-hacking (Opus 4.5 system card), Premature-project-completion (Anthropic), or another documented pattern. If none matches, say so explicitly — do not invent a pattern name.
4. **Identify surface vs. root layer.** Name the file or module you have been patching and the file or module you have not investigated. If they are the same, say "I have not looked outside this layer."
5. **External research at the documented authority bar** per `references/research-quality-bar.md` — two independent authoritative sources OR one primary specification. Stack Overflow is pointer-only. Quote verbatim with URL.
6. **Write the finished diagnosis** in the exact structure from `references/diagnosis-template.md`. One response, no thinking aloud, no hedging between two options. Wait for user approval before applying any fix.

The user's optional argument follows: $ARGUMENTS

If the argument is non-empty, treat it as the specific symptom or area to focus the diagnosis on — populate step 2's table with attempts targeting that symptom, and let step 4's layer-naming use it to constrain the search. If empty, the diagnosis covers the most recent symptom that has had repeated unsuccessful fixes in the current session.
