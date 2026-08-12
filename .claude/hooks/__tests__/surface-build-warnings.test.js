const { test } = require("node:test");
const assert = require("node:assert");
const {
  parseDotnetTestElapsedSeconds,
  CORPUS,
  BUILD_COMMAND_RE,
} = require("../surface-build-warnings.js");

// PostToolUse advisory over Bash output: surfaces warnings that don't fail the
// build (deprecations, Browserslist staleness, a Tailwind content-glob
// regression, test-runtime creep). A miss means the warning class it exists to
// catch scrolls past unnoticed — which is how the site.css regression it cites
// (f8616087) shipped in the first place.

const byId = (id) => CORPUS.find((c) => c.id === id);
const run = (id, input) => byId(id).check({ command: "", output: "", ...input });

// ── which commands are watched ───────────────────────────────────────────

for (const cmd of [
  "dotnet build",
  "dotnet test",
  "pnpm build",
  "pnpm test",
  "pnpm --dir ProjectCeres.Client build",
  "pnpm run watch:css",
]) {
  test(`watched command: ${cmd}`, () => assert.strictEqual(BUILD_COMMAND_RE.test(cmd), true));
}

for (const cmd of ["git status", "ls -la", "dotnet ef migrations add X"]) {
  test(`unwatched command: ${cmd}`, () => assert.strictEqual(BUILD_COMMAND_RE.test(cmd), false));
}

// ── elapsed-time parser: the three dotnet summary shapes ─────────────────

test("parses the vstest Elapsed hh:mm:ss shape", () => {
  assert.strictEqual(parseDotnetTestElapsedSeconds("Elapsed: 00:03:30.45"), 210);
});

test("parses the msbuild 'Total time: N Seconds' shape", () => {
  assert.strictEqual(parseDotnetTestElapsedSeconds("Total time: 12.5 Seconds"), 12.5);
});

test("parses the 'Time: N min' shape", () => {
  assert.strictEqual(parseDotnetTestElapsedSeconds("Total tests: 9 Time: 3.5 min"), 210);
});

test("parses an hour-long run", () => {
  assert.strictEqual(parseDotnetTestElapsedSeconds("Elapsed: 01:00:00"), 3600);
});

test("returns null when no timing is present", () => {
  assert.strictEqual(parseDotnetTestElapsedSeconds("Build succeeded."), null);
});

test("returns null on empty output", () => {
  assert.strictEqual(parseDotnetTestElapsedSeconds(""), null);
});

// ── test-runtime-regression: the 6-minute threshold ──────────────────────

test("does not fire below the 6 min threshold", () => {
  assert.strictEqual(run("test-runtime-regression", { command: "dotnet test", output: "Elapsed: 00:03:30" }), null);
});

test("does not fire exactly at 6 min (rule is > 360s)", () => {
  assert.strictEqual(run("test-runtime-regression", { command: "dotnet test", output: "Elapsed: 00:06:00" }), null);
});

test("fires past the threshold", () => {
  const r = run("test-runtime-regression", { command: "dotnet test", output: "Elapsed: 00:07:00" });
  assert.ok(r && /7\.0 min/.test(r), `expected a fire naming the runtime, got: ${r}`);
});

test("does not fire for a non-test command even when slow", () => {
  assert.strictEqual(run("test-runtime-regression", { command: "dotnet build", output: "Elapsed: 00:09:00" }), null);
});

// ── dotnet-deprecation: code extraction ──────────────────────────────────

test("catches a CA analyzer warning", () => {
  const r = run("dotnet-deprecation", { output: "x.cs(65,42): warning CA1859: Change return type" });
  assert.ok(r && r.includes("CA1859"), r);
});

test("catches NETSDK / MSB / CS06xx codes", () => {
  for (const code of ["NETSDK1234", "MSB3277", "CS0612"]) {
    const r = run("dotnet-deprecation", { output: `warning ${code}: something` });
    assert.ok(r && r.includes(code), `${code}: ${r}`);
  }
});

test("de-duplicates repeated codes", () => {
  const out = "warning CA1859: a\nwarning CA1859: b\nwarning CA1835: c";
  const r = run("dotnet-deprecation", { output: out });
  assert.strictEqual((r.match(/CA1859/g) || []).length, 1, "each code listed once");
  assert.ok(r.includes("CA1835"));
});

test("does not fire on a clean build", () => {
  assert.strictEqual(run("dotnet-deprecation", { output: "Build succeeded.\n0 Warning(s)" }), null);
});

test("does not fire on an unrelated warning code", () => {
  assert.strictEqual(run("dotnet-deprecation", { output: "warning NU1701: package fallback" }), null);
});

// ── browserslist + tailwind detectors ────────────────────────────────────

test("browserslist fires on the caniuse-lite notice", () => {
  const r = run("browserslist-outdated", { output: "Browserslist: caniuse-lite is outdated. Please run..." });
  assert.ok(r && /update-browserslist-db/.test(r), r);
});

test("browserslist stays quiet otherwise", () => {
  assert.strictEqual(run("browserslist-outdated", { output: "vite v5 building for production..." }), null);
});

test("tailwind fires when all utility classes were pruned", () => {
  const r = run("tailwind-no-utility-classes", { output: "warn - No utility classes were detected" });
  assert.ok(r && /content/.test(r), r);
});

test("tailwind stays quiet on a normal build", () => {
  assert.strictEqual(run("tailwind-no-utility-classes", { output: "Done in 412ms." }), null);
});

// ── corpus contract ──────────────────────────────────────────────────────

test("every corpus row has a unique id and a check function", () => {
  const ids = CORPUS.map((c) => c.id);
  assert.strictEqual(new Set(ids).size, ids.length, "ids must be unique");
  for (const c of CORPUS) assert.strictEqual(typeof c.check, "function", `${c.id} needs a check()`);
});

test("no detector throws on empty input", () => {
  for (const c of CORPUS) {
    assert.doesNotThrow(() => c.check({ command: "", output: "" }), `${c.id} threw`);
  }
});

test("no detector fires on a clean, silent build", () => {
  const quiet = { command: "pnpm build", output: "built in 1.2s" };
  for (const c of CORPUS) {
    // Disk-reading detectors (site.css floor, stale locks) depend on the live
    // tree, so only the output-driven ones are asserted silent here.
    if (["site-css-size-floor", "stale-claude-lock"].includes(c.id)) continue;
    assert.strictEqual(c.check(quiet), null, `${c.id} fired on a clean build`);
  }
});
