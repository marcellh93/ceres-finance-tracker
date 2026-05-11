Run the Definition of Done check from `docs/testing.md` § Rules.

Perform each step and report the result. Do not skip or summarize — show the actual output for each check.

1. Run `dotnet build` and report: errors, warnings (especially any new ones), and overall status.
2. Run `dotnet test --nologo` and report: total tests, passed, failed, skipped. List every failed test with its assertion. List every skipped test with the skip reason and whether it has an inline tracked-issue comment.
3. For the current working-tree diff (`git diff HEAD`), list every test file that was modified or created. For each one, state whether the change is:
   (a) a new test (new behavior coverage),
   (b) a contract-driven update (and name the contract change),
   (c) a bug-driven update to a previously-wrong test (and name the user-facing behavior the test should describe), or
   (d) none of the above — which means the change should be reverted.
4. For the production code in the diff, list each new branch (new `if`, `switch`, `case`, ternary, or guard clause) and name the test that exercises it. If no test exercises a new branch, say so explicitly.

After running all four steps, conclude with one of:

- **DONE** — all four steps clean, all test changes fall into (a), (b), or (c), every new branch has a test.
- **NOT DONE** — and list every reason. Do not declare DONE if any step has unresolved issues.
