# Deferral Entry Template

A deferral that passes the skill's two-reason gate MUST be written using this template. Every field is required. If you can't fill one, the deferral is invalid.

---

## Template

```markdown
### [Bug name in one line]

**Where:** [file path or feature name]
**Cost to user:** [observable consequence, one sentence]

**Deferral reason:** [Reason 1 — Tooling gap | Reason 2 — Already-scheduled]

[If Reason 1:]
- Missing tool/dep/infra: [exact name]
- Evidence it's unavailable: [link, error message, version pin, ticket reference]
- Smallest action that unblocks it: [what would have to be true to fix this now]

[If Reason 2:]
- Active batch stage: [stage name in roadmap-phase-N.md]
- Existing scope-line covering this bug: [quote the line]
- Why this bug is covered by that line: [one sentence — it can't be hand-waved]

**Receiving stage:** [stage name, with the `[ ]` line added in THIS commit]

**Receiving-stage line text** (copy-paste into the receiving stage in the same commit):
```
- [ ] [bug name] — see deferral entry in [source file:line]
```

**Mechanical tripwire** (pick one, the bare minimum; more is fine):
- [ ] Failing test added now that will be made green at the receiving stage. Name: `[TestClassName.TestMethod_name]`
- [ ] Architecture-test assertion added to `ArchitectureTests.cs` that will fail once the receiving stage opens.
- [ ] `// FIXME: [bug name] — re-surface in Stage X` comment placed at the exact file:line of the bug source.
- [ ] CI check / lint rule added that asserts the bug's absence.

**Trip-wire location:** [file:line of the failing test, architecture assertion, FIXME marker, or CI check]
```

---

## Worked example — Reason 1 deferral

```markdown
### Login error timing leak — empty Argon2id work for non-existent users

**Where:** `ProjectCeres.Web.Authentication.LoginEndpoint.HandleAsync`
**Cost to user:** Attacker can enumerate registered emails by timing the login response (~70ms for existing users, ~5ms for non-existent ones).

**Deferral reason:** Reason 1 — Tooling gap

- Missing tool/dep/infra: `IFakeUserStore` test wrapper. The fix requires hashing a fixed dummy password for non-existent users to equalize the response time, but our test harness has no way to assert "Argon2id was invoked N times" without mocking out the entire `UserManager`, which itself is a multi-commit task with architecture-test implications.
- Evidence it's unavailable: no `IFakeUserStore` exists in `ProjectCeres.Tests.Architecture` or `ProjectCeres.Tests.Authentication` (grep verified).
- Smallest action that unblocks it: build the `IFakeUserStore` wrapper as the first commit of Stage 9.2's auth-hardening batch.

**Receiving stage:** Stage 9.2 — Auth Hardening (added `[ ]` line in this commit at `docs/roadmap-phase-three.md:312`).

**Receiving-stage line text:**
```
- [ ] Login timing leak — equalize Argon2id work for non-existent users. See deferral entry in docs/specs/stage-9-1-spec.md:188.
```

**Mechanical tripwire:** Failing test added now: `LoginTimingTests.Login_For_Nonexistent_User_Takes_Same_Time_As_Existing_User` — currently marked `[Fact(Skip="Stage 9.2 — see deferral entry")]` is FORBIDDEN per `[[feedback_never_skip_tests_to_make_them_pass]]`. Instead: the test is added, runs, and FAILS today. Stage 9.2 makes it green. The CI red is the tripwire.

**Trip-wire location:** `ProjectCeres.Tests.Authentication/LoginTimingTests.cs:42`
```

---

## Worked example — Reason 2 deferral

```markdown
### MovementsController.Edit uses sync FirstOrDefault

**Where:** `ProjectCeres.Web/Controllers/MovementsController.cs:144`
**Cost to user:** Request thread blocks during DB read; under load the thread pool starves and other requests queue.

**Deferral reason:** Reason 2 — Already-scheduled

- Active batch stage: Stage 9.3 — Controller async audit (in `docs/roadmap-phase-three.md:401`).
- Existing scope-line covering this bug: `[ ] Audit all controller actions for sync-over-async patterns; convert to async.`
- Why this bug is covered: the audit's scope-line targets EVERY sync DB call in EVERY controller; this is one instance.

**Receiving stage:** Stage 9.3 — Controller async audit (adding a more-specific `[ ]` line in this commit at `docs/roadmap-phase-three.md:404`).

**Receiving-stage line text:**
```
- [ ] MovementsController.Edit:144 — convert FirstOrDefault → FirstOrDefaultAsync (part of broader audit).
```

**Mechanical tripwire:** Architecture-test assertion added to `ProjectCeres.Tests.Architecture/AsyncCallsTests.cs`:
```csharp
[Fact]
public void All_Controller_Actions_Touching_DbContext_Are_Async()
{
    // currently fails on MovementsController.Edit:144; passes when Stage 9.3 closes
}
```

**Trip-wire location:** `ProjectCeres.Tests.Architecture/AsyncCallsTests.cs:88`
```

---

## What's intentionally NOT in the template

- "Priority" — not a field. The two valid reasons are the only filter.
- "Estimated effort" — not a field. The scheduling already happens via the receiving-stage checkbox.
- "Notes for future" — not a field. Notes go in the source-stage spec or planning doc, NOT in the deferral entry.

The template is deliberately stripped. Every field that's there is load-bearing.
