# Test flakiness: causes, industry practice, and our plan

> **Diataxis type:** Explanation. Companion to `docs/testing.md` (which holds the binding rules). This doc is the *why* behind them: what flakiness is, why it happens, what mature teams do about it, what our codebase already does, and the concrete work to reduce it at the source.
>
> Written 2026-09-12 after a run of "filter the symptom and move on" responses to CI flakes. The point of this document is to stop treating flakes as minor.

## 1. What a flaky test actually is

A flaky test is one that **passes and fails against the same code** — the result depends on something other than the code under test. That "something" is the root cause, and it is almost always one of a small, well-studied set. Flakiness is not randomness to be tolerated; it is a defect in the test (or occasionally the product) whose trigger is non-deterministic.

Why it matters, in one line each:
- **It destroys signal.** If a red build might be noise, every red build is ignored — including the real regressions. Industry write-ups converge on this: reruns to "get past" a flake train developers to distrust the suite.
- **It is expensive at scale.** Microsoft reported ~25% of test failures in large CI systems are flakes, not defects; Google found ~84% of *retried* failures were flakiness, not real regressions ([search summary, enterprise practice](https://trunk.io/blog/eradicating-flaky-tests)). A suite that flakes 1% per test is effectively always-red once you have a few hundred tests.

## 2. Why flakes happen — the empirical taxonomy

The canonical study is Luo et al., *An Empirical Analysis of Flaky Tests* (FSE 2014), which classified fixed flaky tests in open-source projects. Its categories are still the reference taxonomy a decade later. The most common causes it found:

- **Async wait (~45%)** — the test proceeds before an asynchronous operation finished (a fetch, a render, an animation, a timer). "Fixed" almost always by **waiting for a condition, not a fixed delay** (`waitFor(() => expect(...))`, never `sleep(500)`).
- **Concurrency (~20%)** — two things race (threads, parallel workers, shared connections).
- **Test order dependency (~12%)** — a test only passes if another ran first (or didn't), because they **share state** that isn't reset: a static field, a file, a mock, a singleton, a database row.
- Then the long tail: resource leaks, network, time/clock, randomness, floating-point, unordered collections.

Later studies refine the *proportions by context* but not the categories. The [SAP HANA study (2026)](https://arxiv.org/html/2602.03556) found **concurrency (23%)**, **timeout (16%)**, oracle brittleness (10%), configuration (10%), **async wait (9%)**, **isolation (8%)** — concurrency dominates in a heavily-parallel C++ DB engine, and *native unit tests* skewed even harder to concurrency (39%) while *system tests* skewed to timeouts. A [multivocal review](https://arxiv.org/pdf/2212.00908) and a [JS-specific study](https://arxiv.org/pdf/2207.01047) reach the same shape; in **UI tests specifically, async wait is the single most prominent cause**, and in JS the environmental/timeout dimension is large.

**The takeaway for us:** our stack is UI-heavy JS (Vitest + Testing Library, Playwright) plus a parallel .NET integration suite. So our expected top causes are exactly the two we keep hitting: **async-wait/timeout under CPU contention** and **isolation / shared-state leakage**. This is not bad luck — it is the textbook profile of our stack.

## 3. What mature teams do about it

There is **no one-size-fits-all fix** — the SAP HANA authors say this explicitly; the remedy is tailored per root cause. But the *process* around flakes is well-established, and it is a lifecycle, not a filter:

**Detect → Quarantine → Fix (root cause) → Un-quarantine**, with an SLA.

- **Detect.** Identify a test as flaky *statistically* — the same test passing and failing without a code change (rerun-based or historical-signal-based). Google/Facebook built automated detection early ([enterprise summary](https://trunk.io/blog/eradicating-flaky-tests), [2026 guide](https://scrolltest.com/flaky-tests-detection-quarantine-prevention-guide-2026/)).
- **Quarantine — the key idea we are missing.** A known-flaky test **keeps running but its failure no longer fails the build**; its failures are collected for diagnosis. Google, Slack, Dropbox, Reddit, Flexport all do this. Crucially this is *per-test and visible*, not a global "ignore this error string." Critical tests are marked never-quarantine.
- **Fix at the root**, tracked with an SLA so quarantine is a waiting room, not a graveyard. Reruns/retries are explicitly described as a **band-aid that masks** rather than fixes — acceptable only as a stopgap with a ticket behind it.
- **Prevent** — deterministic time (inject a clock/fake timers), condition-based waits, per-test isolation (fresh state each test), hermetic resources (own DB/dir/port).

**How this maps to our current posture.** Our `docs/testing.md` rule — "a flake is a failure until root-caused; no silent retries" — is *correct and stricter than most*. Where we fall short is the back half: when a root cause is annoying to chase, we've reached for a **suppression filter or a retry and called it done**, without the quarantine-with-SLA discipline that says "non-fatal, but tracked and owed a fix." That's the gap this doc closes.

## 4. What our codebase already has (the audit)

Honest inventory as of 2026-09-12 — much of this is genuinely good:

**Vitest (`ProjectCeres.Client/vite.config.ts` + `src/test-setup.ts`):**
- `maxWorkers: 4` — caps worker concurrency so an 8-core box doesn't saturate and blow `waitFor` budgets. (Addresses async-wait-under-contention.) ✅
- `testTimeout: 15_000` — generous per-test budget. ✅ (but see §5 — a big timeout hides slowness rather than removing the wait race.)
- `retry { count: 2, condition: /Unable to find|timed out|timeout/i }` — retries **only** timeout/not-found flakes, never assertion failures. This is a reasonable stopgap and correctly scoped. ⚠️ stopgap, not a fix.
- `onUnhandledError` filter for `/api/settings`, `Failed to parse URL`, and (2026-09-12) `window is not defined`. ⚠️ **This is symptom suppression** — see §5.
- `test-setup.ts` `afterEach`: restores `global.fetch`, `vi.restoreAllMocks()`, `vi.unstubAllGlobals()`, resets the `useSettings` singleton; zeroes CSS animation/transition durations; polyfills matchMedia/ResizeObserver/scrollIntoView/getBoundingClientRect/elementFromPoint. This is strong isolation hygiene. ✅
- **Resolved (2026-09-13):** the `input-otp` real-`setInterval` leak (the "window is not defined" flake) is fixed at the source by defaulting `pushPasswordManagerStrategy="none"` in the `InputOTP` wrapper, so the interval is never armed. `afterEach` keeps `vi.useRealTimers()` (resets fake-timer state); `vi.clearAllTimers()` was removed — it only affects fake timers and was inert here. See § 5. ✅

**Playwright (`e2e/playwright.golden.config.ts`):** `retries: process.env.CI ? 1 : 0`, `workers: 1`, `fullyParallel: false`, `timeout: 60_000`. Serial + one CI retry. ⚠️ retry is a stopgap; the serialization avoids concurrency flakes at a speed cost.

**.NET (`xunit.runner.json` + collections):** serial by default (`parallelizeTestCollections: false`); parallel-with-clones opt-in (Stage 12.18) with `DisableParallelization` per bucket and **DB-per-bucket physical isolation**. This is a genuine root-cause fix for the concurrency/isolation class — distinct DB per bucket, not a retry. ✅ **This is the model to follow elsewhere.**

**Documented incident history — every one fits the taxonomy:**
- `project_vitest_waitfor_flake` — async wait: `App.test.tsx` needed a 3000ms `waitFor` because 1s starved under load.
- `project_userjobrunner_order_flake` — order dependency: fixed by removing an implicit ordering assumption.
- `project_analyzer_tests_refpack_flake` — environment/resource: a NuGet ref-pack temp-cache miss.
- Stage 12.17 — async wait/timeout: webkit `locator.click` under CI load → one CI retry.
- Stage 12.18 — isolation/concurrency: the whole DB-per-bucket rework.
- 2026-09-11 client-test — resource leak (timer) → currently *suppressed*, root cause below.

## 5. The specific root cause behind the latest flake (verified, not guessed)

The 2026-09-11 `client-test` failure — all 1113 tests pass, but the run fails on unhandled `ReferenceError: window is not defined` attributed to `LoginTotp.test.tsx`, non-reproducible in isolation — is a **resource leak (leaked timer)**, the classic "passes alone, fails in the suite" isolation signature ([Vitest guidance](https://buildpulse.io/blog/vitest-isolate-flaky-tests-ci), [Vitest issue #2480](https://github.com/vitest-dev/vitest/issues/2480)).

Traced to source: `LoginTotp` renders `<InputOTP>` (`input-otp`). The library's minified code runs, on mount:
```js
let a = setInterval(h, 1e3);   // h reads window.innerWidth + getBoundingClientRect()
// plus setTimeout(T, 2e3/5e3/6e3) — T calls document.elementFromPoint / querySelectorAll
```
React clears these on unmount, but under full-suite CPU contention a tick can fire in the window between the worker finishing the file and jsdom tearing down `window`. The interval's `h()` then touches `window` → `ReferenceError`, which Vitest reports as an unhandled error and fails the run.

Our `afterEach` restores mocks and globals but **never clears pending timers**, so a leaked interval survives teardown. That is the exact gap the [Vitest best-practice guidance](https://qaskills.sh/blog/vitest-unhandled-errors-detected-fix) names: "fake timers left running / not-cleared timers cause unhandled rejections; clear them in `afterEach`."

**Correction (2026-09-13) — a first fix that didn't work, and the one that did.** The first attempt added `vi.clearAllTimers()` + `vi.useRealTimers()` to `afterEach` and, after three green runs, was declared fixed. It wasn't: [Vitest docs](https://vitest.dev/api/vi.html) confirm **`vi.clearAllTimers()` only clears FAKE timers** (created under `vi.useFakeTimers()`); `input-otp` uses a **real** `setInterval` with no fake timers active, so the call was a no-op against this leak. The three green runs were the intermittent flake simply not firing — a classic under-sampling error (see § 7). It recurred on CI two days later.

**A SECOND wrong fix, then the real root cause.** After `clearAllTimers` recurred, the next attempt defaulted `pushPasswordManagerStrategy="none"` on the `InputOTP` wrapper — reasoning that input-otp's `setInterval` served only the password-manager badge. That disarmed the *badge* timers (`Ct`) but the flake recurred on CI again (now across two OTP files). A fresh-perspective inspection of the installed library found the **actual** cause: **`input-otp@1.4.2` has an upstream bug.** A separate, *unconditional* `useEffect` schedules three post-mount `setTimeout`s (0/10/50 ms — they dispatch a synthetic `input` event and read `document`/selection state) and **discards the timer handles, returning no cleanup**, so React unmount never clears them. A pending one fires after the Vitest worker tears down jsdom → `ReferenceError: window is not defined`. RTL cleanup can't help — the component itself never registered a `clearTimeout`.

**The fix that works: bump `input-otp` 1.4.2 → 1.5.0.** Confirmed by inspecting the 1.5.0 bundle: its effect captures the handles and returns `() => a.forEach(r => clearTimeout(r))` — the exact cleanup 1.4.2 was missing. This removes the leak at the library level (every input-otp timer, not one cluster). We kept `pushPasswordManagerStrategy="none"` (still correct — removes a needless 1s production interval), removed the inert `vi.clearAllTimers()`, and left the `window is not defined` suppression line out of `onUnhandledError`. **Verified on the CI runner** (where this flake actually fires — local green never proved anything about it): see the roadmap / commit for the green run.

**Lesson for the register:** "N green runs" is not proof against an *intermittent* failure unless N is large and the runs stress the trigger (worker contention). Prefer a fix that makes the failure *structurally impossible* (no timer armed) over one that races to clean up (clear-on-teardown), and verify the API you're relying on actually applies (real vs. fake timers).

## 6. Our plan — reduce flakiness at the source

Ordered by leverage. Each item is root-cause work, not suppression.

1. **Clear timers on teardown (this week).** Add `vi.clearAllTimers()` + `vi.useRealTimers()` to `test-setup.ts afterEach`; verify by running the full suite repeatedly; then delete the `window is not defined` suppression. Fixes the resource-leak class for every timer-using component (`input-otp`, and anything with `setInterval`/`setTimeout`), not just `LoginTotp`. *(Tracked: roadmap §12.19.)*
2. **Adopt a written quarantine lifecycle (this stage).** Extend `docs/testing.md § Flaky tests` with the detect→quarantine→fix→un-quarantine model and an SLA: a flake may be made non-fatal **only** with (a) a documented root-cause hypothesis, (b) a tracked `[ ]` line owing the fix, and (c) a scope/expiry. This turns today's ad-hoc filters/retries into a disciplined, visible register instead of silent debt.
3. **Prefer deterministic waits over big timeouts.** Audit `waitFor`/`findBy` sites relying on the 15s budget or the 3000ms `App.test.tsx` bump; where the wait is for a *specific* condition, assert that condition. A large timeout hides a slow/racy path; it doesn't remove the race.
4. **Consider a real detection signal.** We currently detect flakes by a human noticing a red CI. A lightweight rerun-on-failure that *labels* a retried pass as "flaky" (Playwright already reports this; Vitest `retry` does too) feeding a tracked list would let us find flakes before they erode trust — without letting a retry silently green a real failure.
5. **Keep extending physical isolation where concurrency bites.** The DB-per-bucket model (§12.18) is the right pattern; apply the same "give each parallel unit its own resource" instinct to any future parallelization (ports, temp dirs, files) rather than reaching for a retry.

## 7. The rule this doc changes

Old reflex: *flake → add a filter/retry → "mitigated" → move on.*

New rule: **a flake is a defect with a known taxonomy. Name its category, fix the cause, and if you must make it non-fatal first, that is a quarantine with a tracked owed fix and an expiry — never a silent filter.** Suppression without a root-cause ticket is prohibited, same as `[Fact(Skip=...)]`.

## Sources

- [Luo et al., *An Empirical Analysis of Flaky Tests*, FSE 2014](https://mir.cs.illinois.edu/lamyaa/publications/fse14.pdf) — the canonical root-cause taxonomy.
- [*Flaky Tests in a Large Industrial DBMS (SAP HANA)*, 2026](https://arxiv.org/html/2602.03556) — category proportions by test type.
- [*Test Flakiness' Causes, Detection, Impact and Responses: A Multivocal Review*](https://arxiv.org/pdf/2212.00908).
- [*An Empirical Study of Flaky Tests in JavaScript*](https://arxiv.org/pdf/2207.01047).
- [Trunk.io — *Eradicating flaky tests*](https://trunk.io/blog/eradicating-flaky-tests) — enterprise quarantine practice (Google/Slack/Dropbox/Reddit/Flexport).
- [Flaky test detection/quarantine/prevention guide, 2026](https://scrolltest.com/flaky-tests-detection-quarantine-prevention-guide-2026/).
- [BuildPulse — *Vitest isolate: speed vs. flaky tests*](https://buildpulse.io/blog/vitest-isolate-flaky-tests-ci) and [QASkills — *Vitest "Unhandled Errors Detected" Fix*](https://qaskills.sh/blog/vitest-unhandled-errors-detected-fix) — Vitest teardown/timer guidance.
- [Vitest issue #2480 — unhandled rejections after tests pass](https://github.com/vitest-dev/vitest/issues/2480).
