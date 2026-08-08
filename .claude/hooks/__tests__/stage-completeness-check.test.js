const { test } = require("node:test");
const assert = require("node:assert");
const path = require("path");
const {
  isStageClose,
  collectGaps,
  ROADMAP_PATH_PATTERN,
  extractProposedText,
  policyCoversTable,
  tableNameFor,
} = require(path.join(
  __dirname,
  "..",
  "..",
  "skills",
  "verify-stage-completeness",
  "hooks",
  "stage-completeness-check.js"
));

// Phase E' HARD gate. It audits every IUserOwned entity / service / resx key
// against the registries a downstream consumer iterates, and denies the
// close-out edit if any cross-reference is broken. Two failure modes:
//
//   isStageClose false negative -> the gate never engages, a stage closes with
//     registry gaps intact (the exact bug the hook was built for: an entity
//     shipping with rowsecurity = false).
//   collectGaps counting advisory checks -> the gate denies on non-gaps and
//     gets bypassed out of habit.
//
// The audit* functions read the live tree and are exercised end-to-end
// separately; these pin the pure decision logic around them.

// ── isStageClose: does the gate engage? ───────────────────────────────────

test("fires on a `## Stage N ... ✅ Done` header", () => {
  assert.strictEqual(isStageClose("## Stage 12 — Sessions + Support ✅ Done"), true);
});

test("fires on a decimal sub-stage header", () => {
  assert.strictEqual(isStageClose("## Stage 9.5 — Hardening ✅ Done"), true);
});

test("fires on a lettered sub-stage header", () => {
  assert.strictEqual(isStageClose("## Stage 6c — Reauth ✅ Done"), true);
});

test("fires on the em-dash `Stage N — ... Done` shape", () => {
  assert.strictEqual(isStageClose("Stage 11 — Razor teardown, Done 2026-06-29"), true);
});

test("fires on the colon `Stage N: ... Done` shape", () => {
  assert.strictEqual(isStageClose("Stage 8: email service Done"), true);
});

test("is case-insensitive on the Done marker", () => {
  assert.strictEqual(isStageClose("Stage 8 — email service DONE"), true);
});

// ── isStageClose: must NOT engage ────────────────────────────────────────
// Every false positive here runs a full-tree audit on an unrelated edit.

test("does NOT fire on ordinary roadmap prose mentioning a stage", () => {
  assert.strictEqual(isStageClose("Stage 12 is still pending; 12.9 shipped."), false);
});

test("does NOT fire on an unchecked stage line", () => {
  assert.strictEqual(isStageClose("- [ ] Stage 12 — Sessions + Support SPA pages"), false);
});

test("does NOT fire on the word Done without a stage reference", () => {
  assert.strictEqual(isStageClose("Done with the reauth dialog for now."), false);
});

test("handles empty and undefined input", () => {
  assert.strictEqual(isStageClose(""), false);
  assert.strictEqual(isStageClose(undefined), false);
});

// ── ROADMAP_PATH_PATTERN: which files arm the gate ───────────────────────

test("matches a roadmap doc", () => {
  assert.ok(ROADMAP_PATH_PATTERN.test("/repo/docs/roadmap-phase-three.md"));
  assert.ok(ROADMAP_PATH_PATTERN.test("/repo/docs/roadmap-phase-2.md"));
});

test("does NOT match other docs", () => {
  assert.ok(!ROADMAP_PATH_PATTERN.test("/repo/docs/planning-phase3.md"));
  assert.ok(!ROADMAP_PATH_PATTERN.test("/repo/docs/testing.md"));
});

test("does NOT match a roadmap-named file outside docs/", () => {
  assert.ok(!ROADMAP_PATH_PATTERN.test("/repo/notes/roadmap-phase-three.md"));
});

// ── extractProposedText: every tool shape reaches the matcher ────────────
// A shape that returns "" silently skips the gate.

test("reads Write content", () => {
  assert.strictEqual(extractProposedText({ content: "x" }), "x");
});

test("reads Edit new_string", () => {
  assert.strictEqual(extractProposedText({ new_string: "x" }), "x");
});

test("concatenates MultiEdit new_strings", () => {
  assert.strictEqual(
    extractProposedText({ edits: [{ new_string: "a" }, { new_string: "b" }] }),
    "a\nb"
  );
});

test("returns empty string for an empty tool_input", () => {
  assert.strictEqual(extractProposedText({}), "");
  assert.strictEqual(extractProposedText(undefined), "");
});

// ── collectGaps: real gaps vs advisory checks ────────────────────────────

test("collects a failing check", () => {
  const audit = [{ kind: "entity", name: "SupportTicket", checks: [{ id: "U2", label: "policy", pass: false }] }];
  const gaps = collectGaps(audit);
  assert.strictEqual(gaps.length, 1);
  assert.strictEqual(gaps[0].id, "U2");
  assert.strictEqual(gaps[0].name, "SupportTicket");
});

test("ignores a passing check", () => {
  const audit = [{ kind: "entity", name: "Account", checks: [{ id: "D1", label: "dbset", pass: true }] }];
  assert.deepStrictEqual(collectGaps(audit), []);
});

test("ignores an advisory (na) check even when it did not pass", () => {
  // D2 is deliberately advisory — entities configured by an iteration loop
  // legitimately have no explicit modelBuilder.Entity<T> block.
  const audit = [
    { kind: "entity", name: "Account", checks: [{ id: "D2", label: "explicit block", pass: false, na: true }] },
  ];
  assert.deepStrictEqual(collectGaps(audit), []);
});

test("collects across multiple items and keeps kind + name", () => {
  const audit = [
    { kind: "entity", name: "SupportTicket", checks: [{ id: "U2", label: "policy", pass: false }] },
    { kind: "resx", name: "TicketReceived", checks: [{ id: "R1", label: "es resx", pass: false }] },
    { kind: "entity", name: "Account", checks: [{ id: "D1", label: "dbset", pass: true }] },
  ];
  const gaps = collectGaps(audit);
  assert.strictEqual(gaps.length, 2);
  assert.deepStrictEqual(gaps.map((g) => g.kind).sort(), ["entity", "resx"]);
});

test("an empty audit yields no gaps", () => {
  assert.deepStrictEqual(collectGaps([]), []);
});

// ── U2 policy coverage: the loop-shape false negative ────────────────────
// Found 2026-08-08 by planting a well-formed IUserOwned entity (GateProbe,
// concrete + DbSet + no RLS policy) and watching the gate ALLOW the close-out.
// The old code set policyFound = true for ANY entity as soon as it saw a
// migration matching /foreach (var table in FrozenUserOwnedTables)/, without
// checking whether that entity's table was in the iterated list. Because that
// list is FROZEN, no entity added after it can ever be in it — so U2 could
// never fail for a new entity, which is precisely the bug the hook exists to
// catch (an entity shipping with rowsecurity = false).

const FROZEN_MIGRATION = `
  protected override void Up(MigrationBuilder migrationBuilder) {
    foreach (var table in FrozenUserOwnedTables) {
      migrationBuilder.Sql($@"CREATE POLICY user_isolation ON ""{table}"" USING (...);");
    }
  }
  private static readonly string[] FrozenUserOwnedTables = { "Accounts", "Transactions", "UserSessions" };
`;

const EXPLICIT_MIGRATION = `
  migrationBuilder.Sql(@"CREATE POLICY user_isolation ON ""SupportTickets"" USING (...);");
`;

test("U2: explicit CREATE POLICY naming the table covers it", () => {
  assert.strictEqual(policyCoversTable([EXPLICIT_MIGRATION], "SupportTickets"), true);
});

test("U2: loop-shape migration covers a table that IS in the iterated list", () => {
  assert.strictEqual(policyCoversTable([FROZEN_MIGRATION], "Accounts"), true);
});

test("U2: loop-shape migration does NOT cover a table absent from the list", () => {
  assert.strictEqual(
    policyCoversTable([FROZEN_MIGRATION], "GateProbes"),
    false,
    "a frozen loop list must not blanket-pass an entity it never mentions"
  );
});

test("U2: no migration at all does not cover anything", () => {
  assert.strictEqual(policyCoversTable([], "Accounts"), false);
});

test("U2: a migration without CREATE POLICY does not cover anything", () => {
  assert.strictEqual(policyCoversTable(['migrationBuilder.Sql("SELECT 1");'], "Accounts"), false);
});

test("U2: an explicit migration elsewhere still covers its own table", () => {
  assert.strictEqual(
    policyCoversTable([FROZEN_MIGRATION, EXPLICIT_MIGRATION], "SupportTickets"),
    true
  );
});

// ── table-name derivation ────────────────────────────────────────────────
// U2 needs the real table name. Naive typeName + "s" produces "Categorys",
// "Settingss", "TotpReplayEntrys" — none of which exist, so the gate denied
// three healthy entities once the loop-shape blanket-pass was removed. EF's
// pluralizer handles -y -> -ies and already-plural names.

test("pluralize: regular noun takes -s", () => {
  assert.strictEqual(tableNameFor("Account", ""), "Accounts");
  assert.strictEqual(tableNameFor("Transaction", ""), "Transactions");
});

test("pluralize: consonant + y becomes -ies", () => {
  assert.strictEqual(tableNameFor("Category", ""), "Categories");
});

test("pluralize: vowel + y still takes -s", () => {
  assert.strictEqual(tableNameFor("Day", ""), "Days");
});

test("pluralize: a name already ending in s is left alone", () => {
  assert.strictEqual(tableNameFor("Settings", ""), "Settings");
});

test("pluralize: -ry becomes -ries", () => {
  assert.strictEqual(tableNameFor("TotpReplayEntry", ""), "TotpReplayEntries");
});

test("an explicit [Table(\"...\")] attribute wins over pluralization", () => {
  assert.strictEqual(
    tableNameFor("GateProbe", 'public class GateProbe { } [Table("custom_probes")]'),
    "custom_probes"
  );
});

// The three entities that regressed when the blanket-pass was removed must all
// resolve to names the frozen migration actually contains.
test("the three previously-misplurised entities resolve to real frozen tables", () => {
  const frozen = ["Categories", "Settings", "TotpReplayEntries"];
  for (const [type, expected] of [
    ["Category", "Categories"],
    ["Settings", "Settings"],
    ["TotpReplayEntry", "TotpReplayEntries"],
  ]) {
    const got = tableNameFor(type, "");
    assert.strictEqual(got, expected);
    assert.ok(frozen.includes(got), `${got} must be in the frozen list`);
  }
});
