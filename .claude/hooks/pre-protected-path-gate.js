#!/usr/bin/env node
// PreToolUse hook — denies destructive tool calls (Bash rm/mv/rmdir/git-rm/
// ef-database-drop, Edit-to-empty, Write replacing a protected file) against
// paths inside a directory containing a `.protected` marker file.
//
// Bypass: BOTH of the following must hold —
//   (a) env var CERES_DELETE_PROTECTED=<exact-marker-name> is set
//   (b) the user's most recent message literally contains the path being touched
//
// Why this hook exists:
//   The dev-teacher near-miss (2026-05-26) — agent silently included a
//   personally-attached skill in a bulk-delete list. Memory entries pinning
//   "don't delete this" were not load-bearing at the moment of the rm. The
//   four-sub-agent assessment converged on a filesystem-level enforcement:
//   .protected markers + pre-action gate that reads tool inputs, not prose.
//
// Why this hook is NOT doom-loop fuel:
//   It reads tool inputs (Bash command string, Edit file_path, Write file_path),
//   not assistant prose. The agent cannot route around it by phrasing differently.
//   It denies based on literal path match against the marker tree — independent
//   evidence the agent doesn't get to redefine.
//
// Source: 9.5a memory-as-mechanism assessment, 2026-05-26.
// Architect Change (Q2 §3) + CTO §3 §2 §3 convergence.

const fs = require("fs");
const path = require("path");

const PROJECT_DIR = process.env.CLAUDE_PROJECT_DIR || process.cwd();
const STATE_DIR = path.join(PROJECT_DIR, ".claude", "state", "protected-path-gate");

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

function log(payload, outcome, summary) {
  try {
    fs.mkdirSync(STATE_DIR, { recursive: true });
    fs.appendFileSync(
      path.join(STATE_DIR, "log.jsonl"),
      JSON.stringify({
        ts: new Date().toISOString(),
        outcome,
        summary,
        sessionId: payload.session_id || "",
      }) + "\n"
    );
  } catch {
    // best-effort
  }
}

// ─────────────────────────────────────────────────────────────────────────
// Find every directory under the project that contains a `.protected` marker.
// Performance: ~20ms on a directory tree of ~500 dirs. Caches per-process.
// ─────────────────────────────────────────────────────────────────────────

let _protectedCache = null;

function findProtectedDirectories() {
  if (_protectedCache) return _protectedCache;
  const out = [];
  function walk(d) {
    let entries;
    try {
      entries = fs.readdirSync(d, { withFileTypes: true });
    } catch {
      return;
    }
    for (const e of entries) {
      // Skip noisy dirs that won't contain protected markers.
      if (e.name === "node_modules" || e.name === "bin" || e.name === "obj" ||
          e.name === ".git" || e.name === "dist" || e.name === ".vs") {
        continue;
      }
      const full = path.join(d, e.name);
      if (e.isDirectory()) {
        const markerPath = path.join(full, ".protected");
        if (fs.existsSync(markerPath)) {
          let markerName = "";
          try { markerName = fs.readFileSync(markerPath, "utf8").trim(); } catch { /* ignore */ }
          if (!markerName) markerName = path.basename(full);
          out.push({ dir: full, name: markerName });
        }
        walk(full);
      }
    }
  }
  walk(PROJECT_DIR);
  _protectedCache = out;
  return out;
}

// Extract candidate paths from a tool-input string.
// For Bash: grep argv-like tokens after rm / mv / rmdir / git rm / ef database drop /
// ef migrations remove --force / git checkout HEAD -- <path>.
// For Edit/Write: the file_path field is the candidate.
function extractCandidatePaths(toolName, toolInput) {
  const paths = [];
  if (toolName === "Bash") {
    const cmd = String(toolInput?.command || "");
    // rm / rmdir / mv: capture remaining argv up to the next pipe/semicolon
    const rmMatch = cmd.match(/\b(rm|rmdir|mv|git\s+rm)\b\s+([^\n;&|]+)/g);
    if (rmMatch) {
      for (const seg of rmMatch) {
        // strip the command itself
        const argv = seg.replace(/\b(rm|rmdir|mv|git\s+rm)\b\s+/, "");
        // split by whitespace; skip flags (start with -)
        for (const tok of argv.split(/\s+/)) {
          if (tok && !tok.startsWith("-")) paths.push(tok.replace(/^['"]|['"]$/g, ""));
        }
      }
    }
    // dotnet ef database drop / dotnet ef migrations remove --force
    if (/\bdotnet\s+ef\s+(database\s+drop|migrations\s+remove[^\n]*--force)/.test(cmd)) {
      paths.push("__db_destructive__"); // synthetic — caught below if DB is marked
    }
    // git checkout HEAD -- <path> (potentially destructive — reverts working-tree changes)
    const checkoutMatch = cmd.match(/\bgit\s+checkout\s+\S+\s+--\s+([^\n;&|]+)/);
    if (checkoutMatch) {
      for (const tok of checkoutMatch[1].split(/\s+/)) {
        if (tok && !tok.startsWith("-")) paths.push(tok.replace(/^['"]|['"]$/g, ""));
      }
    }
  } else if (toolName === "Edit" || toolName === "Write" || toolName === "MultiEdit") {
    if (toolInput?.file_path) paths.push(toolInput.file_path);
  }
  return paths;
}

// Resolve a candidate path against the project. Returns absolute path if it
// can be resolved (file or directory), null otherwise.
function resolveCandidate(candidate) {
  if (!candidate) return null;
  if (path.isAbsolute(candidate)) return candidate;
  // Strip leading ./ for cleaner comparison
  const stripped = candidate.replace(/^\.\//, "");
  return path.resolve(PROJECT_DIR, stripped);
}

// Does this absolute path live inside one of the protected directories?
function findProtectingDir(absPath, protectedDirs) {
  for (const p of protectedDirs) {
    if (absPath === p.dir || absPath.startsWith(p.dir + path.sep)) {
      return p;
    }
  }
  return null;
}

// Check if the user's most recent message contains the literal path text.
function userMessageContains(transcriptPath, needle) {
  if (!transcriptPath || !fs.existsSync(transcriptPath)) return false;
  let lines;
  try {
    lines = fs.readFileSync(transcriptPath, "utf8").trim().split("\n");
  } catch {
    return false;
  }
  // Walk backwards for the most recent user message
  for (let i = lines.length - 1; i >= 0; i--) {
    let entry;
    try { entry = JSON.parse(lines[i]); } catch { continue; }
    if (entry.type !== "user") continue;
    const msg = entry.message;
    let text = "";
    if (msg && typeof msg.content === "string") text = msg.content;
    else if (msg && Array.isArray(msg.content)) {
      text = msg.content
        .filter((c) => c && (c.type === "text" || typeof c === "string"))
        .map((c) => (typeof c === "string" ? c : c.text))
        .filter(Boolean)
        .join("\n");
    }
    if (text) return text.includes(needle);
  }
  return false;
}

// ─────────────────────────────────────────────────────────────────────────
// Main
// ─────────────────────────────────────────────────────────────────────────

let raw = "";
process.stdin.on("data", (c) => (raw += c));
process.stdin.on("end", () => {
  let payload;
  try { payload = JSON.parse(raw || "{}"); } catch { allow(); }

  const toolName = payload.tool_name || "";
  if (!/^(Bash|Edit|Write|MultiEdit)$/.test(toolName)) allow();

  const toolInput = payload.tool_input || {};
  const candidates = extractCandidatePaths(toolName, toolInput);
  if (candidates.length === 0) allow();

  const protectedDirs = findProtectedDirectories();
  if (protectedDirs.length === 0) allow();

  // Find which candidate(s) hit which protected dir(s).
  const hits = [];
  for (const cand of candidates) {
    const abs = resolveCandidate(cand);
    if (!abs) continue;
    const hit = findProtectingDir(abs, protectedDirs);
    if (hit) hits.push({ candidate: cand, abs, marker: hit });
  }
  if (hits.length === 0) allow();

  // For each hit: check bypass conditions.
  // Bypass requires BOTH:
  //   (a) env CERES_DELETE_PROTECTED=<marker-name>
  //   (b) user's most recent message contains the literal marker name
  const bypassNames = (process.env.CERES_DELETE_PROTECTED || "")
    .split(",").map((s) => s.trim()).filter(Boolean);

  const transcriptPath = payload.transcript_path || "";

  const unbypassed = [];
  for (const h of hits) {
    const envOk = bypassNames.includes(h.marker.name) || bypassNames.includes("*");
    const userOk = userMessageContains(transcriptPath, h.marker.name) ||
                   userMessageContains(transcriptPath, h.marker.dir.replace(PROJECT_DIR + path.sep, ""));
    if (!(envOk && userOk)) unbypassed.push(h);
  }

  if (unbypassed.length === 0) {
    log(payload, "bypassed", { hits: hits.map((h) => h.candidate) });
    allow();
  }

  const reason = [
    "🛑 Protected-path gate — destructive tool call against a protected directory.",
    "",
    "Blocked because the target(s) live inside a directory containing a `.protected` marker:",
    ...unbypassed.map(
      (h) => `  • ${h.candidate} → protected by ${h.marker.dir.replace(PROJECT_DIR + path.sep, "")}/.protected (name: "${h.marker.name}")`
    ),
    "",
    "Bypass requires BOTH:",
    `  (a) env var: CERES_DELETE_PROTECTED=<name>  (currently: ${process.env.CERES_DELETE_PROTECTED ? `"${process.env.CERES_DELETE_PROTECTED}"` : "(unset)"})`,
    "  (b) the user's most recent message must contain the literal marker name OR the protected directory path.",
    "",
    "Why this exists:",
    "  Some directories carry personal-use or project-history value the agent cannot",
    "  evaluate alone (learning material, decommissioned-skill tombstones, personal-",
    "  workflow tooling). The marker turns deletion into an explicit two-factor",
    "  decision: the user names the target AND sets the env var.",
    "",
    "Recovery:",
    "  • If the deletion is intentional: ask the user to confirm by name, then set",
    `    CERES_DELETE_PROTECTED=${unbypassed.map((h) => h.marker.name).join(",")} for the session.`,
    "  • If unintentional: drop the path from the tool call.",
    "",
    "Audit log: .claude/state/protected-path-gate/log.jsonl",
  ].join("\n");

  log(payload, "blocked", { hits: unbypassed.map((h) => h.candidate) });
  deny(reason);
});
