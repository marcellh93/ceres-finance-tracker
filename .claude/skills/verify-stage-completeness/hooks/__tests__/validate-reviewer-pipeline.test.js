const { test } = require("node:test");
const assert = require("node:assert");
const { validateReviewerPipeline } = require("../evidence-bundle-check.js");

const HEAD = "abc123def456";
function write(tmp, obj) {
  const fs = require("fs"), path = require("path"), os = require("os");
  const dir = fs.mkdtempSync(path.join(os.tmpdir(), "rev-"));
  const p = path.join(dir, "reviewer-pipeline.json");
  fs.writeFileSync(p, JSON.stringify(obj));
  return p;
}
function full(extra = {}) {
  return {
    stage: "9.5e", diff_sha: HEAD,
    reviewers: [
      { role: "writer", verdict: "pass", findings: [] },
      { role: "security", verdict: "pass", findings: [] },
      { role: "playwright-test-audit", verdict: "pass", findings: [], spec_vs_assertion_diff: [] },
    ],
    ...extra,
  };
}

test("passes when 3 roles present, sha matches, no block", () => {
  const gaps = validateReviewerPipeline(write({}, full()), HEAD);
  assert.deepStrictEqual(gaps, []);
});

test("fails when a role is missing", () => {
  const o = full();
  o.reviewers = o.reviewers.slice(0, 2);
  const gaps = validateReviewerPipeline(write({}, o), HEAD);
  assert.ok(gaps.some((g) => /playwright-test-audit/.test(g)));
});

test("fails when diff_sha is stale", () => {
  const gaps = validateReviewerPipeline(write({}, full({ diff_sha: "stale000" })), HEAD);
  assert.ok(gaps.some((g) => /diff_sha/.test(g)));
});

test("fails when a reviewer verdict is block", () => {
  const o = full();
  o.reviewers[1].verdict = "block";
  const gaps = validateReviewerPipeline(write({}, o), HEAD);
  assert.ok(gaps.some((g) => /block/.test(g) && /security/.test(g)));
});

test("fails on invalid JSON", () => {
  const fs = require("fs"), path = require("path"), os = require("os");
  const dir = fs.mkdtempSync(path.join(os.tmpdir(), "rev-"));
  const p = path.join(dir, "reviewer-pipeline.json");
  fs.writeFileSync(p, "{not json");
  const gaps = validateReviewerPipeline(p, HEAD);
  assert.ok(gaps.some((g) => /invalid JSON/.test(g)));
});
