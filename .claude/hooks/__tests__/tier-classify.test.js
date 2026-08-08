const { test } = require("node:test");
const assert = require("node:assert");
const cp = require("child_process");
const path = require("path");

// run-tests.sh picks the dotnet test scope from the session's write list. That
// logic used to be a heredoc inside the shell script, so nothing could test it
// — a wrong tier either wastes 4 minutes every turn or silently skips the suite
// on a turn that changed .cs. It now lives in tier-classify.py; these cases pin
// the contract documented in run-tests.sh's "Tier rule" header.
//
//   TIER 0 — frontend only (.ts/.tsx). No .NET impact.
//   TIER 1 — production .cs/.csproj under ProjectCeres/ only.
//   TIER 2 — test files, .sln, or anything ambiguous.

const SCRIPT = path.join(__dirname, "..", "tier-classify.py");

function tier(payload) {
  const input = typeof payload === "string" ? payload : JSON.stringify(payload);
  const out = cp.execSync(`python3 ${JSON.stringify(SCRIPT)}`, {
    input,
    encoding: "utf8",
    stdio: ["pipe", "pipe", "ignore"],
  });
  return parseInt(out.trim(), 10);
}

// --- TIER 0: frontend-only ------------------------------------------------

test("TIER 0 — a single .tsx file", () => {
  assert.strictEqual(tier({ files: ["ProjectCeres.Client/src/App.tsx"] }), 0);
});

test("TIER 0 — .ts and .tsx together", () => {
  assert.strictEqual(
    tier({ files: ["ProjectCeres.Client/src/lib/utils.ts", "ProjectCeres.Client/src/App.tsx"] }),
    0
  );
});

// --- TIER 1: production .NET only ----------------------------------------

test("TIER 1 — a production .cs file under ProjectCeres/", () => {
  assert.strictEqual(tier({ files: ["ProjectCeres/Program.cs"] }), 1);
});

test("TIER 1 — a production .csproj under ProjectCeres/", () => {
  assert.strictEqual(tier({ files: ["ProjectCeres/ProjectCeres.csproj"] }), 1);
});

test("TIER 1 — several production .cs files", () => {
  assert.strictEqual(
    tier({ files: ["ProjectCeres/Program.cs", "ProjectCeres/Controllers/Api/HomeController.cs"] }),
    1
  );
});

// --- TIER 2: tests, solution, ambiguity ----------------------------------

test("TIER 2 — a test .cs file", () => {
  assert.strictEqual(tier({ files: ["ProjectCeres.Tests/Unit/SavingsRateTests.cs"] }), 2);
});

test("TIER 2 — the .sln file", () => {
  assert.strictEqual(tier({ files: ["ProjectCeres.sln"] }), 2);
});

test("TIER 2 — the test project's .csproj", () => {
  assert.strictEqual(tier({ files: ["ProjectCeres.Tests/ProjectCeres.Tests.csproj"] }), 2);
});

test("TIER 2 — production .cs mixed with a test .cs (tier UP, never down)", () => {
  assert.strictEqual(
    tier({ files: ["ProjectCeres/Program.cs", "ProjectCeres.Tests/Unit/X.cs"] }),
    2
  );
});

test("TIER 2 — frontend mixed with a test .cs", () => {
  assert.strictEqual(
    tier({ files: ["ProjectCeres.Client/src/App.tsx", "ProjectCeres.Tests/Unit/X.cs"] }),
    2
  );
});

// A .tsx + production .cs mix has .NET impact but no test/sln, so it lands in
// TIER 1 — the unit filter still covers the .cs change.
test("TIER 1 — frontend mixed with production .cs", () => {
  assert.strictEqual(
    tier({ files: ["ProjectCeres.Client/src/App.tsx", "ProjectCeres/Program.cs"] }),
    1
  );
});

// --- Fail-safe: ambiguity must tier UP, never skip ------------------------

test("TIER 2 — empty file list (legacy fallback)", () => {
  assert.strictEqual(tier({ files: [] }), 2);
});

test("TIER 2 — malformed JSON never skips the suite", () => {
  assert.strictEqual(tier("not json at all"), 2);
});

test("TIER 2 — JSON that is not an object", () => {
  assert.strictEqual(tier("[1,2,3]"), 2);
});

test("TIER 2 — object with no files key", () => {
  assert.strictEqual(tier({ other: "value" }), 2);
});

test("TIER 2 — empty stdin", () => {
  assert.strictEqual(tier(""), 2);
});

// --- Case / extension edge cases -----------------------------------------

test("TIER 1 — uppercase .CS extension is still a .NET file", () => {
  assert.strictEqual(tier({ files: ["ProjectCeres/Program.CS"] }), 1);
});

test("TIER 0 — a file with no extension is not treated as .NET", () => {
  assert.strictEqual(tier({ files: ["ProjectCeres/Dockerfile"] }), 0);
});
