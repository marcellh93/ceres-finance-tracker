---
name: verify-runtime-state
description: >
  Use BEFORE making any claim about a runtime state value (database row, file contents,
  environment variable, process state) AND BEFORE handing the user a manual-test
  checklist. Catches two failure modes: (1) inferring DB/runtime values from code
  inspection alone instead of querying the live system; (2) drafting a manual-test
  list whose steps require an upstream UI/route/wiring that doesn't exist yet.
  Triggers on phrases like "the user has X enabled", "TwoFactorEnabled is false",
  "this row says Y", "manual tests you have to do", "browser checklist", "verify
  these in the browser". The skill is HARD-enforced via two Stop hooks; this
  doc explains the rules so I can satisfy them in advance.
---

# verify-runtime-state

This skill exists because of two real incidents on 2026-05-18:

1. **DB-state assumption.** I told the user their `AspNetUsers.TwoFactorEnabled` row
   was `false` based on the absence of a grep hit in `SeedDevUser.cs`. The user
   correctly pointed out that absence of a seed line doesn't tell you the value of
   a row — you have to query the database. I was making a runtime claim from
   design-time evidence.
2. **Manual-test handoff against missing prerequisite UI.** I drafted a 17-step
   manual TOTP-flow test list for the user without first verifying that the
   upstream UI exists. Step 1 was "sign in with a TOTP-enabled user" — but the
   SPA has no TOTP enrolment page, no SPA call to `/api/auth/mfa/enroll`, and
   the seeded user has TwoFactorEnabled=false. The whole list was uncrawlable.
   I shouldn't have shipped it.

Both failure modes share a root cause: claiming or testing against a system state
without verifying that state exists and is reachable.

## When this skill applies

### Rule A — Runtime-state claims

Before any assertion in user-facing text about:
- A row value in the live database (`user.TwoFactorEnabled = false`, "the seed
  user doesn't have email confirmed", "this account has 3 backup codes left")
- A file's existence or contents on disk in a NON-source location (cache files,
  state files, dev-DB dumps, MailDrop folder)
- An environment variable's runtime value (not what's in `appsettings.json`, but
  what's actually loaded into the process)
- A process state ("the dev server is running", "no other workers are bound to
  this port")

I must EITHER:
- Run the command that produces the evidence and quote the actual output in the
  same message, OR
- Use `AskUserQuestion` to authorize the command if it touches credentials/PII
  the auto-mode classifier will block (production DB reads, secret files), OR
- Explicitly mark the statement as an assumption needing verification ("if the
  seed didn't enable TOTP, this row will be false — please confirm with
  `psql -c 'SELECT \"TwoFactorEnabled\" FROM \"AspNetUsers\";'`").

Code inspection (grep / read of source files) does NOT satisfy this rule.
Source code tells you what the code does; it does not tell you what the
runtime state is. A `SeedDevUser.cs` that doesn't enable TOTP doesn't mean
the existing row has it disabled — the row could have been touched by any
of the migrations, by manual SQL, by another tool, by a previous test run.

### Rule B — Manual-test handoffs

Before any user-facing message containing a manual-test checklist (regex
fingerprints: "manual tests you have to do", "things to test in the browser",
"browser checklist", "verify these in the browser", "what to test on your
end", "steps to manually verify"), I must FIRST audit the prerequisite UI
graph:

1. For each step in the checklist, name the entry point the user reaches it
   from (a route, a button, an external trigger like email).
2. For each entry point, confirm the route/button/trigger ACTUALLY EXISTS in
   the current codebase (not in spec, not in planning — in committed code).
3. If any prerequisite is missing, either:
   - Build the missing prerequisite first (typical: a wiring page that lets
     the user reach the surface under test), OR
   - Explicitly call out the missing prerequisite at the top of the checklist
     with a "prerequisite — not yet built" block, and either omit those test
     steps or mark them as blocked.

The audit is a sentence or two per step in the checklist. It costs me a
minute; it saves the user from a confused dead-end test session.

## The hooks that enforce these rules

Both rules are HARD-gated via Stop hooks:

- `hooks/stop-runtime-state-claim.js` — scans the assistant's final message for
  Rule A bypass patterns and blocks if no verification evidence is co-located.
- `hooks/stop-manual-test-handoff.js` — scans for Rule B fingerprints and blocks
  if the message doesn't include a prerequisite-audit line per checklist step.

Both hooks log every match to `.claude/state/runtime-state-verify/log.jsonl`
for the same audit-trail discipline as the chat-deferral hook.

Per-session bypass: `CERES_SKIP_RUNTIME_STATE_HOOK=1`.

## What this skill does NOT do

- Does not catch claims about source code (that's `verify-backend` /
  `verify-frontend`).
- Does not gate `Read` or grep tool calls — those are how I gather evidence,
  not the bypass.
- Does not require running every test in the checklist for the user — only
  that each prerequisite the test depends on is reachable.

## Linked memory

- `feedback_research_before_confident_claims` — the broader "no confident
  claims without evidence" rule. This skill extends it to runtime state
  specifically.
- `feedback_no_flag_without_action` — the manual-test handoff failure is a
  flag-without-action at the test-design level.
