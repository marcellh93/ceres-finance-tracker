Run the Definition of Done check from `docs/testing.md` § Rules — tiered by what the diff actually touches.

**Step 0 — pick the tier.** Inspect the working-tree diff:

```
git diff --name-only HEAD
git ls-files --others --exclude-standard
```

Filter for tracked extensions (`.cs`, `.ts`, `.tsx`, `.csproj`, `.sln`). Then pick the tier — same scheme as `.claude/hooks/run-tests.sh` and the `feedback_never_skip_tests_to_make_them_pass` memory rule:

- **TIER 0** — no tracked-extension files touched (doc/config only): report `TIER 0 — doc-only, no build steps required` and skip directly to step 3.
- **TIER F** — only `.ts` / `.tsx` under `ProjectCeres.Client/` touched: run the **frontend pair** in step 1+2. Do NOT run `dotnet build` / `dotnet test`.
- **TIER B** — only `.cs` / `.csproj` under `ProjectCeres/` touched (no test-project edits, no `.sln`): run the **backend pair** in step 1+2.
- **TIER M** — mixed, OR `.sln` touched, OR `ProjectCeres.Tests/` edits, OR anything ambiguous: run **both pairs**.

Report the chosen tier and the file list it was based on before running anything else.

**Step 1 — build (tier-scoped).** Run only what the tier requires; report errors, warnings (especially any new ones), and overall status for each.

- Frontend pair (TIER F or TIER M): `pnpm --dir ProjectCeres.Client build`
- Backend pair (TIER B or TIER M): `dotnet build ProjectCeres/ProjectCeres.csproj`

**Step 2 — test (tier-scoped).** Run only what the tier requires.

- Frontend pair: `pnpm --dir ProjectCeres.Client test --run` — report total tests, passed, failed, skipped. List every failed test with its assertion. List every skipped test with the reason.
- Backend pair: `dotnet test --nologo` — same reporting requirements. List every skipped test with the skip reason and whether it has an inline tracked-issue comment.

Allow ONE retry on a known-isolation Vitest flake — but if it fails twice, root-cause it (per the memory rule). No retries for `dotnet test`.

**Step 3 — test-file diff inspection** (runs regardless of tier). For the current working-tree diff (`git diff HEAD`), list every test file that was modified or created. For each one, state whether the change is:

   (a) a new test (new behavior coverage),
   (b) a contract-driven update (and name the contract change),
   (c) a bug-driven update to a previously-wrong test (and name the user-facing behavior the test should describe), or
   (d) none of the above — which means the change should be reverted.

**Step 4 — branch coverage** (runs regardless of tier). For the production code in the diff, list each new branch (new `if`, `switch`, `case`, ternary, or guard clause) and name the test that exercises it. If no test exercises a new branch, say so explicitly.

---

After running every step the tier required, conclude with one of:

- **DONE** — every required step clean, all test changes fall into (a)/(b)/(c), every new branch has a test.
- **NOT DONE** — and list every reason. Do not declare DONE if any required step has unresolved issues.

**Forbidden shortcuts:**

- "I'll just run all four to be safe." No. Pick the tier the diff actually warrants — that's the whole point of tiering. Burning `dotnet test` on a `.tsx`-only diff is the failure mode this command was rewritten to prevent.
- "The pre-existing failure was already there, doesn't count." It counts. Per the memory rule, any failing build/test on `main` blocks DONE, regardless of who introduced it.
- "TIER 0 means I can skip steps 3 and 4." No — those are diff-shape checks, not build checks. A doc-only commit that touches a test file (yes, it can happen via a docstring) still gets step 3.
