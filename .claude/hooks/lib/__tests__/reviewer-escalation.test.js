const { test } = require("node:test");
const assert = require("node:assert");
const { applyEscalation } = require("../reviewer-escalation.js");

const KNOWN = ["missing-rls-policy", "entity-without-migration", "pre-auth-write-without-scope", "missing-query-filter", "missing-resx-pair"];

test("first classified miss increments to 1, no marker", () => {
  const { state, marker } = applyEscalation({ rule_classes: {} }, ["missing-rls-policy"], "sha1", KNOWN);
  assert.strictEqual(state.rule_classes["missing-rls-policy"].consecutive, 1);
  assert.strictEqual(marker, null);
});

test("second consecutive same-class miss (new sha) fires the marker", () => {
  const prior = { rule_classes: { "missing-rls-policy": { consecutive: 1, last_diff_sha: "sha1" } } };
  const { state, marker } = applyEscalation(prior, ["missing-rls-policy"], "sha2", KNOWN);
  assert.strictEqual(state.rule_classes["missing-rls-policy"].consecutive, 2);
  assert.deepStrictEqual(marker, { rule_class: "missing-rls-policy", consecutive: 2 });
});

test("clean run resets a class to 0", () => {
  const prior = { rule_classes: { "missing-rls-policy": { consecutive: 1, last_diff_sha: "sha1" } } };
  const { state } = applyEscalation(prior, [], "sha2", KNOWN);
  assert.strictEqual(state.rule_classes["missing-rls-policy"].consecutive, 0);
});

test("same diff sha does not double-count", () => {
  const prior = { rule_classes: { "missing-rls-policy": { consecutive: 1, last_diff_sha: "sha1" } } };
  const { state, marker } = applyEscalation(prior, ["missing-rls-policy"], "sha1", KNOWN);
  assert.strictEqual(state.rule_classes["missing-rls-policy"].consecutive, 1);
  assert.strictEqual(marker, null);
});

test("unknown rule class increments nothing", () => {
  const { state, marker } = applyEscalation({ rule_classes: {} }, ["totally-made-up"], "sha1", KNOWN);
  assert.deepStrictEqual(state.rule_classes, {});
  assert.strictEqual(marker, null);
});
