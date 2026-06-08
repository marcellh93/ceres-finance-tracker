const { test } = require("node:test");
const assert = require("node:assert");
const { SLOT_TABLE } = require("../evidence-bundle-check.js");

// Spec §7 "Slot-trigger test": the reviewer-pipeline.json slot must be REQUIRED for a
// diff touching Common/Authentication/** , Migrations/** , or Models/** (over-trigger on
// any Models/ change), and must NOT be required for a docs-only diff. This pins the
// `when` predicate that decides whether the whole reviewer pipeline fires for a diff.
function reviewerSlotRequired(files) {
  const required = SLOT_TABLE.filter((s) => s.when(files));
  return required.some((s) => s.slot === "reviewer-pipeline.json");
}

test("required when the diff touches Common/Authentication/**", () => {
  assert.strictEqual(
    reviewerSlotRequired(["ProjectCeres/Common/Authentication/EmailChangeService.cs"]),
    true);
});

test("required when the diff touches Migrations/**", () => {
  assert.strictEqual(
    reviewerSlotRequired(["ProjectCeres/Migrations/20260608_AddSomething.cs"]),
    true);
});

test("required when the diff touches Models/** (over-trigger on any Models change)", () => {
  assert.strictEqual(
    reviewerSlotRequired(["ProjectCeres/Models/Account.cs"]),
    true);
});

test("NOT required for a docs-only diff", () => {
  assert.strictEqual(
    reviewerSlotRequired(["docs/roadmap-phase-three.md", "CHANGELOG.md"]),
    false);
});

test("NOT required for an unrelated source diff (Controllers, no watched path)", () => {
  assert.strictEqual(
    reviewerSlotRequired(["ProjectCeres/Controllers/Api/DashboardController.cs"]),
    false);
});

test("required when a watched path is mixed in with unrelated files", () => {
  assert.strictEqual(
    reviewerSlotRequired([
      "ProjectCeres.Client/src/pages/Login.tsx",
      "ProjectCeres/Common/Authentication/LockoutUnlockService.cs",
    ]),
    true);
});
