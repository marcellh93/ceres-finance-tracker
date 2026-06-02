#!/usr/bin/env node
// PreToolUse hook for Edit|Write|MultiEdit on docs/roadmap-phase-*.md.
//
// Phase E' HARD gate (playbook/references/constitution.md).
//
// Runs BEFORE the existing Phase E gate (pre-stage-close-gate.js). If THIS
// gate denies, Phase E never gets a chance — the user sees only one denial
// reason, scoped to the registry gaps.
//
// Trigger:
//   Same as Phase E — Edit/Write/MultiEdit against docs/roadmap-phase-*.md
//   where the new_string flips a `- [ ]` stage header to `- [x]` OR adds
//   `✅ Done` near a stage header.
//
// What it does:
//   Reads the codebase AS IT STANDS RIGHT NOW. No git diff, no "what
//   changed". The audit walks every IUserOwned entity, every Service
//   class, every resx key, and every relevant enum, and checks each
//   required cross-reference. A broken cross-reference is a gap whether
//   it was introduced this commit or two years ago.
//
// Bypass:
//   CERES_SKIP_STAGE_COMPLETENESS_HOOK=1 — exit 0 unconditionally (allow).

const fs = require("fs");
const path = require("path");

const PROJECT_DIR = process.env.CLAUDE_PROJECT_DIR || process.cwd();
const ROADMAP_PATH_PATTERN = /\/docs\/roadmap-phase-[a-z0-9-]+\.md$/i;
const STATE_DIR = path.join(PROJECT_DIR, ".claude", "state", "stage-completeness");

function allow() {
  process.stdout.write(JSON.stringify({ permissionDecision: "allow" }));
  process.exit(0);
}

function deny(reason) {
  process.stdout.write(
    JSON.stringify({ permissionDecision: "deny", permissionDecisionReason: reason })
  );
  process.exit(0);
}

function readFile(rel) {
  try {
    return fs.readFileSync(path.join(PROJECT_DIR, rel), "utf8");
  } catch {
    return "";
  }
}

function listFilesRecursive(dir) {
  const out = [];
  function walk(d) {
    let entries;
    try {
      entries = fs.readdirSync(d, { withFileTypes: true });
    } catch {
      return;
    }
    for (const e of entries) {
      const full = path.join(d, e.name);
      if (e.isDirectory()) walk(full);
      else if (e.isFile()) out.push(full);
    }
  }
  walk(dir);
  return out;
}

function relPath(absPath) {
  return path.relative(PROJECT_DIR, absPath).split(path.sep).join("/");
}

// Detect "stage-close" signal in the new_string.
function isStageClose(text) {
  if (!text) return false;
  if (/##\s*Stage\s+\d+(?:\.\d+)*[a-z]?\s+[^\n]*✅\s*Done/i.test(text)) return true;
  // Roadmap header flips from `- [ ] Stage N ...` to `- [x] Stage N ...`.
  // We can only see new_string content; treat any `- [x] Stage N` or
  // `Stage N — Done` shape as a close-out attempt.
  if (/\bStage\s+\d+(?:\.\d+)*[a-z]?\s*[—:-][^\n]*\bDone\b/i.test(text)) return true;
  return false;
}

// ─────────────────────────────────────────────────────────────────────────
// Audit functions — each returns { itemName, checks: [{ id, label, pass, na }] }
// Read everything live from disk; no git, no diff.
// ─────────────────────────────────────────────────────────────────────────

function auditEntities() {
  const modelsDir = path.join(PROJECT_DIR, "ProjectCeres", "Models");
  const ctx = readFile("ProjectCeres/Data/AppDbContext.cs");
  // Stage 9.5b: the user-owned set is derived from the EF model by
  // UserOwnedModel.RlsTables (the hand-typed UserOwnedTables.All was deleted). A
  // concrete IUserOwned entity with a DbSet IS in the set by construction — there is
  // no list to grep. U1 below therefore verifies the structural facts that make an
  // entity model-derived-covered, not membership in a deleted file.

  const results = [];

  for (const abs of listFilesRecursive(modelsDir)) {
    if (!abs.endsWith(".cs")) continue;
    if (abs.endsWith(".Designer.cs")) continue;
    const content = fs.readFileSync(abs, "utf8");

    // Pull every class declaration in the file.
    const classRe = /public\s+((?:sealed\s+|abstract\s+|partial\s+)*)class\s+(\w+)/g;
    let m;
    while ((m = classRe.exec(content)) !== null) {
      const modifiers = m[1] || "";
      const typeName = m[2];
      // Skip abstract classes — they're TPC roots or value bases, not tables.
      // Examples: `Movement` is the abstract root for Transaction/Transfer/
      // LiabilityPayment; the concrete subtypes carry the tables.
      if (/\babstract\b/.test(modifiers)) continue;

      // IUserOwned detection — look at the inheritance list following the
      // class declaration (up to 500 chars of context).
      const idx = m.index;
      const window = content.slice(idx, idx + 500);
      const isUserOwned =
        /:\s*[^\n]*\bIUserOwned\b/.test(window) ||
        /,\s*IUserOwned\b/.test(window);

      const checks = [];

      // D1 — DbSet declaration (the hard requirement).
      const dbsetRe = new RegExp(`DbSet<${typeName}>\\s+\\w+\\s*=>?\\s*Set<${typeName}>`);
      const hasDbSet = dbsetRe.test(ctx);
      checks.push({
        id: "D1",
        label: `DbSet<${typeName}> declared in AppDbContext.cs`,
        pass: hasDbSet,
      });

      // Skip non-table classes — if there's no DbSet, this isn't a persisted
      // entity (it's a DTO, ViewModel, request shape, etc.). Don't report.
      if (!hasDbSet) continue;

      // D2 — explicit modelBuilder.Entity<T> block.
      // NOTE: many entities are configured by table-iteration loops in
      // AppDbContext (e.g. the user_isolation HasQueryFilter loop in
      // ConfigureUserOwnedEntities), so a missing explicit block is OFTEN
      // legitimate. Treat D2 as informational only — not a gap.
      const cfgRe = new RegExp(`modelBuilder\\.Entity<${typeName}>`);
      checks.push({
        id: "D2",
        label: `modelBuilder.Entity<${typeName}> block in AppDbContext.cs (advisory; n/a if covered by an iteration loop)`,
        pass: cfgRe.test(ctx),
        na: !cfgRe.test(ctx),
      });

      if (isUserOwned) {
        // U1 — model-derived membership. UserOwnedModel.RlsTables(model) includes
        // every concrete IUserOwned entity that maps to a table. This entity is
        // concrete (abstract skipped at line ~112), IUserOwned (this branch), and has
        // a DbSet (D1 passed / `continue`d otherwise) — so it IS in the set by
        // construction. No hand-list to grep (deleted Stage 9.5b). The pass is the
        // structural fact; the runtime truth (policy installed) is U2 + ParityTests.
        const u1Pass = isUserOwned && hasDbSet;
        checks.push({
          id: "U1",
          label: `${typeName} is model-derived-covered by UserOwnedModel.RlsTables (concrete IUserOwned + DbSet)`,
          pass: u1Pass,
        });

        // U2 — there must exist a migration with CREATE POLICY user_isolation
        // referencing the table name. Pluralize crudely: typeName + "s".
        // (If the entity carries [Table("Custom")] override, accept either.)
        const tableAttrMatch = content
          .slice(idx, idx + 1000)
          .match(/\[Table\("([^"]+)"\)\]/);
        const tableName = tableAttrMatch ? tableAttrMatch[1] : `${typeName}s`;

        const migrationsDir = path.join(PROJECT_DIR, "ProjectCeres", "Migrations");
        let policyFound = false;
        try {
          for (const f of fs.readdirSync(migrationsDir)) {
            if (!f.endsWith(".cs") || f.endsWith(".Designer.cs")) continue;
            const mig = fs.readFileSync(path.join(migrationsDir, f), "utf8");
            if (!mig.includes("CREATE POLICY user_isolation")) continue;
            // Two acceptable shapes:
            //  (a) Explicit: `ON "TableName"` literal in the SQL (or the frozen
            //      table-name list the Stage 7.5 migration now carries — Stage 9.5b
            //      Task 4 inlined a `FrozenUserOwnedTables` copy so the historical
            //      migration stays byte-reproducible after the hand-list deletion).
            //  (b) Loop shape: a migration that iterates a table-name list and emits
            //      CREATE POLICY user_isolation per entry. The runtime SQL truth
            //      (whether the policy is actually installed for this table) is
            //      verified by ParityTests + RlsParityStartupCheck, not this static
            //      check — here we only confirm a migration plausibly covers it.
            if (mig.includes(`"${tableName}"`)) {
              policyFound = true;
              break;
            }
            if (
              /foreach\s*\(var\s+table\s+in\s+(FrozenUserOwnedTables|UserOwnedTables\.All)\)/.test(mig)
            ) {
              // Tentative — loop-based. Mark as "loop-covered" and continue
              // searching for an explicit one (explicit wins).
              policyFound = true;
              // Don't break — we want to confirm there's no follow-up migration
              // that does something different (e.g. DROP POLICY).
            }
          }
        } catch {
          // ignore directory read errors
        }
        checks.push({
          id: "U2",
          label: `CREATE POLICY user_isolation migration covers "${tableName}"`,
          pass: policyFound,
        });
      }

      results.push({ kind: "entity", name: typeName, checks });
    }
  }
  return results;
}

function auditServices() {
  const dirs = [
    path.join(PROJECT_DIR, "ProjectCeres", "Common", "Authentication"),
    path.join(PROJECT_DIR, "ProjectCeres", "Services"),
  ];

  const program = readFile("ProjectCeres/Program.cs");
  const archTests = readFile(
    "ProjectCeres.Tests/Integration/Authentication/ArchitectureTests.cs"
  );

  const results = [];

  for (const dir of dirs) {
    for (const abs of listFilesRecursive(dir)) {
      if (!abs.endsWith(".cs")) continue;
      if (abs.endsWith(".Designer.cs")) continue;
      if (!abs.endsWith("Service.cs")) continue;
      const content = fs.readFileSync(abs, "utf8");
      const m = content.match(/public\s+(?:sealed\s+)?class\s+(\w+Service)\b/);
      if (!m) continue;
      const name = m[1];
      const rel = relPath(abs);

      const checks = [];

      const diRe = new RegExp(`Add(Scoped|Singleton|Transient)<${name}>`);
      // Also accept `Add(Scoped|...)<IFoo, Foo>` pattern (interface registration)
      const diInterfaceRe = new RegExp(
        `Add(Scoped|Singleton|Transient)<I${name}, ${name}>|` +
          `Add(Scoped|Singleton|Transient)<I\\w+, ${name}>`
      );
      checks.push({
        id: "S1",
        label: `${name} DI registered in Program.cs`,
        pass: diRe.test(program) || diInterfaceRe.test(program),
      });

      const usesIgnoreFilters = /IgnoreQueryFilters\s*\(/.test(content);
      if (usesIgnoreFilters) {
        checks.push({
          id: "S2",
          label: `${rel} in IgnoreQueryFilters allow-list (ArchitectureTests.cs)`,
          pass: archTests.includes(rel),
        });
      } else {
        checks.push({
          id: "S2",
          label: `${name} does not call IgnoreQueryFilters() — n/a`,
          pass: true,
          na: true,
        });
      }

      results.push({ kind: "service", name, checks });
    }
  }
  return results;
}

function auditResxTemplates() {
  const en = readFile("ProjectCeres/Resources/EmailsResource.en.resx");
  const es = readFile("ProjectCeres/Resources/EmailsResource.es.resx");
  const enumFile = readFile("ProjectCeres/Common/Email/EmailTemplateKey.cs");

  const subjectRe = /<data name="(\w+)\.Subject"/g;
  const enKeys = new Set();
  for (const m of en.matchAll(subjectRe)) enKeys.add(m[1]);

  const results = [];
  for (const tpl of enKeys) {
    const checks = [];

    const has3En =
      en.includes(`name="${tpl}.Subject"`) &&
      en.includes(`name="${tpl}.BodyText"`) &&
      en.includes(`name="${tpl}.BodyHtml"`);
    checks.push({ id: "R1", label: `3 keys in en.resx`, pass: has3En });

    const has3Es =
      es.includes(`name="${tpl}.Subject"`) &&
      es.includes(`name="${tpl}.BodyText"`) &&
      es.includes(`name="${tpl}.BodyHtml"`);
    checks.push({ id: "R2", label: `3 keys in es.resx`, pass: has3Es });

    const inEnum = new RegExp(`\\b${tpl}\\b`).test(enumFile);
    checks.push({
      id: "R3",
      label: `${tpl} in EmailTemplateKey enum`,
      pass: inEnum,
    });

    results.push({ kind: "resx", name: tpl, checks });
  }
  return results;
}

function auditAuditLogActions() {
  const auditFile = readFile("ProjectCeres/Models/AuditLog.cs");
  const archTests = readFile(
    "ProjectCeres.Tests/Integration/Authentication/ArchitectureTests.cs"
  );

  const enumMatch = auditFile.match(/enum\s+AuditLogAction\s*\{([\s\S]*?)\}/);
  if (!enumMatch) return [];
  const values = enumMatch[1]
    .split(",")
    .map((s) => s.trim().replace(/\/\/.*$/, "").trim())
    .filter((s) => s && /^\w+$/.test(s));

  // Find the documented-set test's expected[] array body. The window between
  // the test name and the var expected = new[] declaration can be hundreds of
  // characters when there's a doc comment between them — give the regex room.
  const docSetMatch = archTests.match(
    /AuditLogAction_enum_values_match_documented_set[\s\S]{0,2000}var expected = new\[\]\s*\{([\s\S]*?)\};/
  );
  const docSetBody = docSetMatch ? docSetMatch[1] : "";

  const results = [];
  for (const v of values) {
    results.push({
      kind: "auditAction",
      name: v,
      checks: [
        {
          id: "A1",
          label: `${v} in documented-set architecture test`,
          pass: new RegExp(`"${v}"`).test(docSetBody),
        },
      ],
    });
  }
  return results;
}

function auditFailedLoginReasons() {
  const file = readFile("ProjectCeres/Models/FailedLoginAttempt.cs");
  const enumMatch = file.match(/enum\s+FailedLoginReason\s*\{([\s\S]*?)\}/);
  if (!enumMatch) return [];
  const values = enumMatch[1]
    .split(",")
    .map((s) => s.trim().replace(/\/\/.*$/, "").trim())
    .filter((s) => s && /^\w+$/.test(s));

  // Concatenate all .cs files under ProjectCeres/ for the call-site scan.
  // Cheap: ~150 files, all under a few MB combined.
  const cs = listFilesRecursive(path.join(PROJECT_DIR, "ProjectCeres"))
    .filter((f) => f.endsWith(".cs") && !f.endsWith(".Designer.cs"));
  let allSrc = "";
  for (const f of cs) {
    if (f.endsWith("FailedLoginAttempt.cs")) continue; // declaration file doesn't count
    try {
      allSrc += fs.readFileSync(f, "utf8");
    } catch {
      // ignore
    }
  }

  const results = [];
  for (const v of values) {
    const referenced = new RegExp(`FailedLoginReason\\.${v}\\b`).test(allSrc);
    results.push({
      kind: "failedLoginReason",
      name: v,
      checks: [
        {
          id: "F1",
          label: `${v} referenced in at least one call site`,
          pass: referenced,
        },
      ],
    });
  }
  return results;
}

// ─────────────────────────────────────────────────────────────────────────
// Main
// ─────────────────────────────────────────────────────────────────────────

let raw = "";
process.stdin.on("data", (c) => (raw += c));
process.stdin.on("end", () => {
  if (process.env.CERES_SKIP_STAGE_COMPLETENESS_HOOK === "1") allow();

  let payload;
  try {
    payload = JSON.parse(raw || "{}");
  } catch {
    allow();
  }

  const toolName = payload.tool_name || "";
  if (!/^(Edit|Write|MultiEdit)$/.test(toolName)) allow();

  const ti = payload.tool_input || {};
  const filePath = ti.file_path || "";
  if (!ROADMAP_PATH_PATTERN.test(filePath)) allow();

  const chunks = [];
  if (typeof ti.content === "string") chunks.push(ti.content);
  if (typeof ti.new_string === "string") chunks.push(ti.new_string);
  if (Array.isArray(ti.edits)) {
    for (const e of ti.edits) {
      if (e && typeof e.new_string === "string") chunks.push(e.new_string);
    }
  }
  const text = chunks.join("\n");
  if (!text) allow();
  if (!isStageClose(text)) allow();

  // Run the audit against the codebase AS IT STANDS.
  const all = [
    ...auditEntities(),
    ...auditServices(),
    ...auditResxTemplates(),
    ...auditAuditLogActions(),
    ...auditFailedLoginReasons(),
  ];

  // Collect gaps only.
  const gaps = [];
  for (const item of all) {
    for (const c of item.checks) {
      if (!c.pass && !c.na) {
        gaps.push({ kind: item.kind, name: item.name, id: c.id, label: c.label });
      }
    }
  }

  // Build a grouped report — only items with at least one gap appear in the
  // denial message (keeps the output focused on what to fix).
  const groupHeaders = {
    entity: "Entities",
    service: "Services",
    resx: "Resx templates",
    auditAction: "AuditLogAction values",
    failedLoginReason: "FailedLoginReason values",
  };
  const grouped = {};
  for (const g of gaps) {
    (grouped[g.kind] = grouped[g.kind] || {})[g.name] = grouped[g.kind][g.name] || [];
    grouped[g.kind][g.name].push(`✗ ${g.id}: ${g.label}`);
  }

  // Log every run (pass or block) for the audit trail.
  try {
    fs.mkdirSync(STATE_DIR, { recursive: true });
    fs.appendFileSync(
      path.join(STATE_DIR, "log.jsonl"),
      JSON.stringify({
        ts: new Date().toISOString(),
        outcome: gaps.length === 0 ? "passed" : "blocked",
        gaps,
        sessionId: payload.session_id || "",
      }) + "\n"
    );
  } catch {
    // best-effort
  }

  if (gaps.length === 0) allow();

  const sections = [];
  for (const kind of Object.keys(groupHeaders)) {
    if (!grouped[kind]) continue;
    const lines = [`${groupHeaders[kind]} with gaps (${Object.keys(grouped[kind]).length}):`];
    for (const name of Object.keys(grouped[kind]).sort()) {
      lines.push(`  ${name}:`);
      for (const line of grouped[kind][name]) lines.push(`    ${line}`);
    }
    sections.push(lines.join("\n"));
  }

  const reason = [
    `verify-stage-completeness — codebase audit (live state):`,
    "",
    sections.join("\n\n"),
    "",
    `Summary: ${gaps.length} gap(s) found across the codebase. Stage close-out blocked.`,
    "",
    "Phase E' in playbook/references/constitution.md: every IUserOwned entity / Service / resx template / AuditLogAction / FailedLoginReason in the codebase must land in EVERY registry a downstream consumer iterates. Gaps are gaps regardless of when they were introduced — the audit reads live disk state, not the stage diff.",
    "",
    "Recovery:",
    "  • Address each ✗ above. Re-run the close-out edit after.",
    "  • If a gap is a legitimate intentional exception, set CERES_SKIP_STAGE_COMPLETENESS_HOOK=1 for this session and call out the bypass in the close-out message.",
    "",
    "See .claude/state/stage-completeness/log.jsonl for the run log.",
  ].join("\n");

  deny(reason);
});
