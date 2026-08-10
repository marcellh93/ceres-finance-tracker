const { test } = require("node:test");
const assert = require("node:assert");
const path = require("path");
const {
  extractClaims,
  extractReadPaths,
  findMissing,
} = require(path.join(
  __dirname, "..", "..", "skills", "verify-image-claims", "hooks", "stop-image-claim-detect.js"
));

// Stop hook: blocks when an <image-finding> block cites an image the turn
// never Read. It exits 2 on transcript history, which a blocked turn cannot
// change — the same shape as the 2026-08-07 evidence-bundle loop. The log
// already shows one 2-block identical repeat (2026-05-22, same two paths,
// 4 minutes apart). These pin the matcher so a claim can neither be missed
// (false negative = ungrounded findings ship) nor invented (false positive =
// the turn is blocked with nothing to fix).

// ── extractClaims: the marker matcher ────────────────────────────────────

test("extracts a single marker block", () => {
  const got = extractClaims('<image-finding image="/a/b.png">F1 — clipped</image-finding>');
  assert.strictEqual(got.length, 1);
  assert.strictEqual(got[0].imagePath, "/a/b.png");
});

test("extracts multiple marker blocks", () => {
  const text = [
    '<image-finding image="/a/1.png">F1</image-finding>',
    "some prose in between",
    '<image-finding image="/a/2.png">F2</image-finding>',
  ].join("\n");
  assert.deepStrictEqual(extractClaims(text).map((c) => c.imagePath), ["/a/1.png", "/a/2.png"]);
});

test("trims whitespace around the declared path", () => {
  assert.strictEqual(extractClaims('<image-finding image="  /a/b.png  ">F</image-finding>')[0].imagePath, "/a/b.png");
});

test("handles a multi-line finding body", () => {
  const got = extractClaims('<image-finding image="/a/b.png">\n  F1 — line one\n  F2 — line two\n</image-finding>');
  assert.strictEqual(got.length, 1);
  assert.ok(got[0].findingSnippet.includes("F1"));
});

test("is case-insensitive on the tag name", () => {
  assert.strictEqual(extractClaims('<IMAGE-FINDING image="/a/b.png">F</IMAGE-FINDING>').length, 1);
});

// ── extractClaims: must NOT invent claims ────────────────────────────────
// Every false positive blocks a turn with nothing the agent can fix.

test("no claim from prose merely describing an image", () => {
  assert.deepStrictEqual(extractClaims("The screenshot shows the OTP cell clipping at the right edge."), []);
});

test("no claim from an unclosed marker", () => {
  assert.deepStrictEqual(extractClaims('<image-finding image="/a/b.png">F1 — dangling'), []);
});

test("no claim from a marker missing the image attribute", () => {
  assert.deepStrictEqual(extractClaims("<image-finding>F1</image-finding>"), []);
});

test("handles empty and undefined input", () => {
  assert.deepStrictEqual(extractClaims(""), []);
  assert.deepStrictEqual(extractClaims(undefined), []);
});

test("repeated calls do not leak regex lastIndex state", () => {
  const text = '<image-finding image="/a/b.png">F</image-finding>';
  assert.strictEqual(extractClaims(text).length, 1);
  assert.strictEqual(extractClaims(text).length, 1, "second call must match too (global regex state)");
});

// ── extractReadPaths: the evidence scan ──────────────────────────────────

test("finds a Read file_path in a transcript line", () => {
  const lines = ['{"name":"Read","input":{"file_path":"/a/b.png"}}'];
  assert.ok(extractReadPaths(lines).has("/a/b.png"));
});

test("finds Read paths across many lines", () => {
  const lines = [
    '{"name":"Read","input":{"file_path":"/a/1.png"}}',
    '{"name":"Bash","input":{"command":"ls"}}',
    '{"name":"Read","input":{"file_path":"/a/2.png"}}',
  ];
  const got = extractReadPaths(lines);
  assert.ok(got.has("/a/1.png") && got.has("/a/2.png"));
  assert.strictEqual(got.size, 2);
});

test("ignores a non-Read tool carrying a file_path", () => {
  const lines = ['{"name":"Edit","input":{"file_path":"/a/b.png"}}'];
  assert.strictEqual(extractReadPaths(lines).size, 0);
});

test("tolerates malformed lines", () => {
  assert.strictEqual(extractReadPaths(["not json", "", null]).size, 0);
});

// ── findMissing: the verdict ─────────────────────────────────────────────

test("no missing when every claim was Read", () => {
  const claims = [{ imagePath: "/a/b.png" }];
  assert.deepStrictEqual(findMissing(claims, new Set(["/a/b.png"])), []);
});

test("reports a claim never Read", () => {
  const claims = [{ imagePath: "/a/b.png" }];
  assert.deepStrictEqual(findMissing(claims, new Set()).map((c) => c.imagePath), ["/a/b.png"]);
});

test("reports only the unread subset", () => {
  const claims = [{ imagePath: "/a/1.png" }, { imagePath: "/a/2.png" }];
  const got = findMissing(claims, new Set(["/a/1.png"]));
  assert.deepStrictEqual(got.map((c) => c.imagePath), ["/a/2.png"]);
});

test("path match is exact — a basename match is not evidence", () => {
  const claims = [{ imagePath: "/a/b/shot.png" }];
  assert.strictEqual(findMissing(claims, new Set(["/other/shot.png"])).length, 1);
});

test("no claims means nothing missing", () => {
  assert.deepStrictEqual(findMissing([], new Set()), []);
});
