# Dead-lock Defenses

Five real dead-lock mechanisms in multi-turn hook-coordinated state machines, with primary-source authority quotes and the corresponding mitigation in this skill.

Sourced 2026-05-20 from AWS Step Functions docs, Microsoft Learn Compensating Transaction pattern, and Temporal documentation. Two independent authoritative sources for each mechanism per the deep-fix-mode research-quality bar.

---

## 1 — State waits indefinitely

**Mechanism.** A state with no timeout never exits if the expected event doesn't arrive. In our context: the user is asked "fix or document?", closes the laptop, comes back two days later. The state file still says `awaiting=fix-or-document`. Every subsequent turn blocks.

**Authority — Temporal:**

> "No state should wait indefinitely for an event; a timeout ensures progress by setting a maximum duration for a state and defining a transition on timeout to an error or idle state."
>
> — Search synthesis of Temporal docs, fetched 2026-05-20

**Authority — Microsoft Learn (Compensating Transaction pattern):**

> "A step might not fail immediately but instead get blocked. You might need to implement a timeout mechanism."
>
> — [learn.microsoft.com/en-us/azure/architecture/patterns/compensating-transaction](https://learn.microsoft.com/en-us/azure/architecture/patterns/compensating-transaction), § Problems and considerations

**Mitigation in this skill.** Every non-`none` state carries `started_at` (ISO 8601 timestamp). Any hook that observes `now - started_at > 24h` auto-releases to `none` with a log entry. The 24-hour window is generous — the failure this catches is multi-day session pauses, not impatient users. A user pausing for hours mid-conversation is normal; a state surviving 24 hours is desync.

---

## 2 — State file corruption mid-write

**Mechanism.** A hook writes the state file, crashes (or is killed by the OS, or hits disk full, or the process is interrupted) mid-write, leaves invalid JSON on disk. Next hook can't parse the file. If it fails-closed, every subsequent turn denies. If it silently recreates empty state, context is lost without notification.

**Authority — Microsoft Learn:**

> "The infrastructure that handles the steps must meet the following criteria: It's resilient in both the original operation and the compensating transaction. It doesn't lose the information required to compensate for a failing step."
>
> — [learn.microsoft.com/en-us/azure/architecture/patterns/compensating-transaction](https://learn.microsoft.com/en-us/azure/architecture/patterns/compensating-transaction), § Problems and considerations

**Authority — AWS Step Functions:**

> "Standard workflows have exactly-once workflow execution and can run for up to one year."
>
> — [docs.aws.amazon.com/step-functions/latest/dg/welcome.html](https://docs.aws.amazon.com/step-functions/latest/dg/welcome.html)

(Exactly-once execution requires durable state writes; the inverse — torn writes — must not corrupt the workflow.)

**Mitigation in this skill.** Every state write follows write-then-rename:

```javascript
const tmp = statePath + ".tmp";
fs.writeFileSync(tmp, JSON.stringify(state, null, 2));
fs.renameSync(tmp, statePath);  // POSIX rename is atomic
```

POSIX `rename(2)` is atomic on the same filesystem. A reader either sees the old file or the new file, never a partial write.

On parse failure (corrupt file from a non-atomic write that happened before this defense was deployed, or a manual edit gone wrong), the hooks fall back to treating the state as `none` with a loud stderr log line — **never deny a turn because state can't be parsed.** The cost of false-deny here is "user can't continue conversation"; the cost of false-allow is "user gets one un-gated turn." False-allow is the cheaper failure.

---

## 3 — Hook execution failure

**Mechanism.** The hook itself has a bug — null reference, regex throws, fs.access fails on a permissions issue. Today's `pre-write-deferral.js` already shows this class of risk; if it crashed mid-execution, the harness would interpret the non-zero exit as a deny.

**Authority — AWS Step Functions:**

> "Error handling (Retry / Catch) – You can retry failed tasks, or catch failed tasks and automatically run alternative steps."
>
> — [docs.aws.amazon.com/step-functions/latest/dg/welcome.html](https://docs.aws.amazon.com/step-functions/latest/dg/welcome.html), § Example use cases

The principle: a hook crash is a Retry/Catch event, not a workflow termination.

**Mitigation in this skill.** All three hooks wrap their main body in `try/catch`. On exception:

```javascript
try {
  // ... hook logic ...
} catch (err) {
  fs.appendFileSync(errorLogPath, JSON.stringify({
    ts: new Date().toISOString(),
    hook: __filename,
    error: err.message,
    stack: err.stack,
  }) + "\n");
  process.stderr.write(
    `⚠ fix-interaction hook errored; gate open this turn. See ${errorLogPath}.\n`
  );
  process.exit(0);  // fail-open
}
```

Exit code 0 lets the session continue. The operator sees the stderr warning + the error log; the gate doesn't strand the conversation. This is the **inverse default of `pre-write-deferral.js`** (which fails-closed), because the cost calculus differs:

- `pre-write-deferral.js` false-deny: one Write attempt wasted; user retries.
- `fix-interaction` hook false-deny: entire conversation stranded until the user finds the bypass.

The latter is worse, so the fail-open default applies here.

---

## 4 — State desync between hook and reality

**Mechanism.** The state machine says `awaiting=where`, but the user's last message is actually about a different topic ("forget that, let's talk about Stage 12"). The hook keeps blocking turns waiting for a destination doc name, but the user has mentally moved past the bug entirely.

**Authority — Microsoft Learn:**

> "[The infrastructure] reliably monitors compensation logic progress. Compensating transactions run after the original operations commit, and other transactions might change intermediate states. Therefore, ensure that you can correlate and audit both the original operation and its compensation end-to-end."
>
> — [learn.microsoft.com/en-us/azure/architecture/patterns/compensating-transaction](https://learn.microsoft.com/en-us/azure/architecture/patterns/compensating-transaction), § Problems and considerations

Translation: the state machine must not block when reality has moved on.

**Mitigation in this skill.** Before blocking on `await-answer`, the hook lexically checks the user's message:

- **Engagement signals** — message contains any of: `fix`, `document`, `doc`, `now`, `later`, `where`, the named doc path, any stage reference (`Stage 9.11`, etc.). These mean the user is engaging with the gate.
- **Topic-shift signals** — message contains any of: `actually`, `let's`, `move on`, `different question`, `forget that`, `nevermind`, `drop it`, `skip that`, `something else`. These mean the user has moved on.

Decision:
- Engagement present → process the answer (transition normally).
- Topic-shift present AND no engagement → release the gate to `none`, log the topic-shift, let the new prompt through unblocked.
- Neither present (ambiguous) → conservative: process as engagement, let the assistant's reply ask for clarification.

The user's intent overrides the state machine. Always.

---

## 5 — No human-override channel

**Mechanism.** Every dead-lock paper names this as the last-resort layer. If defenses 1–4 don't catch a stuck state, the user needs a fast, named way out — without leaving the chat, without editing files, without consulting docs.

**Authority — AWS Step Functions:**

> "You can configure a timeout for action states to set the maximum number of seconds your state can run before it fails, and use timeouts to prevent stuck executions. As a best practice, add a task level timeout in order to avoid a stuck execution."
>
> — [docs.aws.amazon.com/step-functions/latest/dg/sfn-stuck-execution.html](https://docs.aws.amazon.com/step-functions/latest/dg/sfn-stuck-execution.html)

(Step Functions also exposes manual `StopExecution` and `SendTaskFailure` APIs — the operator escape hatch.)

**Authority — Microsoft Learn:**

> "Sometimes manual intervention is the only way to recover from a failed step. In these situations, the system should raise an alert that includes detailed information about the reason for the failure."
>
> — [learn.microsoft.com/en-us/azure/architecture/patterns/compensating-transaction](https://learn.microsoft.com/en-us/azure/architecture/patterns/compensating-transaction), § Solution

**Mitigation in this skill.** Three-way escape, escalating cost:

| Cost | Channel | When to use |
|---|---|---|
| Cheapest | `CERES_SKIP_FIX_INTERACTION_HOOK=1` env var | Per-session bypass when running an automated flow that legitimately mentions fixes without acting (e.g. spec authoring) |
| Mid | `rm .claude/state/fix-interaction/<session_id>.json` | The state is desynced and you want a clean slate for the rest of the conversation |
| Heaviest | `/fix-interaction-reset` slash command | Same as the rm above, but in-chat — no terminal switch, logged to audit trail |

All three log to `.claude/state/fix-interaction/log.jsonl` with the bypass channel + timestamp + reason (where capturable) so post-hoc analysis can see when the gate was overridden and how often.

---

## Bonus — Two patterns the research surfaced that we adopted

### Idempotent state transitions

> "A step might run multiple times when retried, so design each step as an idempotent command."
>
> — Microsoft Learn, [Compensating Transaction pattern](https://learn.microsoft.com/en-us/azure/architecture/patterns/compensating-transaction)

Every state transition in this skill follows compare-and-set semantics:

```javascript
function transition(state, expectedFrom, to) {
  if (state.awaiting !== expectedFrom) {
    // Hook re-fired; the transition already happened. No-op.
    return false;
  }
  state.awaiting = to;
  state.started_at = new Date().toISOString();
  writeStateAtomic(state);
  return true;
}
```

This means a hook re-fire (harness retry, race condition, manual replay) cannot double-transition or corrupt the machine.

### Audit trail as load-bearing diagnostic

> "Therefore, ensure that you can correlate and audit both the original operation and its compensation end-to-end."
>
> — Microsoft Learn, same source

Every state transition writes to `.claude/state/fix-interaction/log.jsonl`:

```json
{
  "ts": "2026-05-20T15:32:11.234Z",
  "interaction_id": "fix-2026-05-20-a3f9",
  "from": "fix-or-document",
  "to": "where",
  "trigger": "await-answer",
  "user_msg_snippet": "document it under Stage 9.11",
  "topic_shift_detected": false
}
```

When something goes wrong (stuck state, unexpected transition, false-positive prompt), the log is how we reconstruct what the machine thought was happening. Same pattern the existing `stop-chat-deferral-detect.js` uses at `.claude/state/deferral-detect/log.jsonl`.

---

## Sources

- AWS Step Functions — *Using timeouts to avoid stuck executions* — [docs.aws.amazon.com/step-functions/latest/dg/sfn-stuck-execution.html](https://docs.aws.amazon.com/step-functions/latest/dg/sfn-stuck-execution.html)
- AWS Step Functions — *Welcome / Overview* — [docs.aws.amazon.com/step-functions/latest/dg/welcome.html](https://docs.aws.amazon.com/step-functions/latest/dg/welcome.html)
- Microsoft Learn — *Compensating Transaction Pattern* (fetched 2026-05-20) — [learn.microsoft.com/en-us/azure/architecture/patterns/compensating-transaction](https://learn.microsoft.com/en-us/azure/architecture/patterns/compensating-transaction)
- Microsoft Learn — *Saga Design Pattern* — [learn.microsoft.com/en-us/azure/architecture/patterns/saga](https://learn.microsoft.com/en-us/azure/architecture/patterns/saga)
- Temporal — *Beyond State Machines for Reliable Distributed Applications* — [temporal.io/blog/temporal-replaces-state-machines-for-distributed-applications](https://temporal.io/blog/temporal-replaces-state-machines-for-distributed-applications)
- Zhou et al., *Cognitive Biases of Large Language Model Agents (CB6 Anchoring)*, 2024 — [arxiv.org/abs/2412.06593](https://arxiv.org/abs/2412.06593) (used to name the Fixation pattern in the deep-fix-mode diagnosis that produced this design)
