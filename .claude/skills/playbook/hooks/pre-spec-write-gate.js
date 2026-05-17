#!/usr/bin/env node
// PreToolUse hook for Write|Edit|MultiEdit on docs/superpowers/specs/*.md.
//
// REPLACES .claude/hooks/require-verify-against-codebase-before-spec.js
// with a superset: also requires superpowers:brainstorming to have fired
// this session (Phase B in playbook/references/constitution.md).
//
// Behavior:
//   - If target is NOT under docs/superpowers/specs/ → allow.
//   - If target file already exists on disk → allow (rewrite of an existing spec).
//   - Else: check playbook state for both superpowers:brainstorming AND
//     the matching verify skill(s). If anything required is missing, deny.
//   - Bypass: an explicit verify-skill name (verify-backend / verify-frontend /
//     verify-against-codebase) in the transcript counts (preserved from the
//     old hook's transcript-scan behavior for continuity).
//
// Tiered verify (added 2026-05-17 alongside the split):
//   The proposed spec content is scanned for backend vs frontend signals:
//     - Backend signals: ProjectCeres/ path, .cs / .csproj filename, Program.cs,
//       DbContext, EF migration, HTTP status codes, ASP.NET, Razor, ModelState,
//       docs/api-contract.md, docs/multi-tenancy-strategy.md.
//     - Frontend signals: ProjectCeres.Client/ path, .tsx / .ts filename,
//       shadcn, base-ui, Radix, Tailwind, Vite, Vitest, React, JSX/hook names,
//       <Badge>/<Numeric>/<StatTile>, docs/design-system.md.
//   Backend-only signals → require verify-backend (or legacy router).
//   Frontend-only signals → require verify-frontend (or legacy router).
//   Both, or unable to classify (no signals) → require BOTH (default-safe).
//
// State source: .claude/state/playbook/<session_id>.json (written by
// state-record-skill-fire.js).

const fs = require("fs");
const path = require("path");

const PROJECT_DIR = process.env.CLAUDE_PROJECT_DIR || process.cwd();
const STATE_DIR = path.join(PROJECT_DIR, ".claude", "state", "playbook");
const SPEC_PATH_PATTERN = /\/docs\/superpowers\/specs\/[^/]+\.md$/;

const BACKEND_SIGNAL_RE = /\b(ProjectCeres\/|Program\.cs|DbContext|EF migration|EF Core|ASP\.NET|Razor|InvalidModelStateResponseFactory|ModelState|ValidationProblemDetails|UnprocessableEntity|HttpStatusCode|api-contract\.md|multi-tenancy-strategy\.md|\.cs\b|\.csproj\b)/i;
const FRONTEND_SIGNAL_RE = /\b(ProjectCeres\.Client\/|shadcn|base-ui|@base-ui|Radix|Tailwind|Vite|Vitest|design-system\.md|<Badge|<Numeric|<StatTile|<StatRow|<EquationRow|<CardError|useApi|useState|useEffect|useMemo|\.tsx\b|\.ts\b)/i;

const BACKEND_SKILLS = new Set(["verify-backend", "verify-against-codebase"]);
const FRONTEND_SKILLS = new Set(["verify-frontend", "verify-against-codebase"]);

function allow() {
  process.stdout.write(JSON.stringify({ permissionDecision: "allow" }));
  process.exit(0);
}

function deny(reason) {
  process.stdout.write(JSON.stringify({ permissionDecision: "deny", permissionDecisionReason: reason }));
  process.exit(0);
}

function readState(sessionId) {
  if (!sessionId) return null;
  const file = path.join(STATE_DIR, `${sessionId}.json`);
  try {
    return JSON.parse(fs.readFileSync(file, "utf8"));
  } catch {
    return null;
  }
}

let raw = "";
process.stdin.on("data", (c) => (raw += c));
process.stdin.on("end", () => {
  let payload;
  try { payload = JSON.parse(raw || "{}"); } catch { allow(); }

  const toolName = payload.tool_name || "";
  if (!/^(Write|Edit|MultiEdit)$/.test(toolName)) allow();

  const filePath = payload?.tool_input?.file_path || "";
  if (!SPEC_PATH_PATTERN.test(filePath)) allow();

  // Existing-spec rewrite exemption.
  try {
    if (fs.existsSync(filePath)) allow();
  } catch {
    // fall through; treat as new
  }

  const sessionId = payload.session_id;
  const state = readState(sessionId);
  const fired = (state && Array.isArray(state.fired)) ? state.fired : [];

  const brainstormingFired = fired.some((s) =>
    /superpowers:?brainstorming/i.test(s)
  );

  // Tier the spec by scanning its proposed content for backend / frontend
  // signals. The Write tool input carries the content as `content` (Write) or
  // `new_string` (Edit). MultiEdit's `edits[].new_string` is concatenated.
  const proposed = extractProposedContent(payload);
  const hasBackendSignal = BACKEND_SIGNAL_RE.test(proposed);
  const hasFrontendSignal = FRONTEND_SIGNAL_RE.test(proposed);
  // Default-safe: if we can't classify, require both.
  const requireBackend = hasBackendSignal || (!hasBackendSignal && !hasFrontendSignal);
  const requireFrontend = hasFrontendSignal || (!hasBackendSignal && !hasFrontendSignal);

  const backendFired = fired.some((s) => BACKEND_SKILLS.has(s));
  const frontendFired = fired.some((s) => FRONTEND_SKILLS.has(s));

  // Bypass-friendly carve-out: scan transcript for verify-skill names.
  let backendInTranscript = false;
  let frontendInTranscript = false;
  const transcriptPath = payload?.transcript_path || "";
  if (transcriptPath) {
    try {
      if (fs.existsSync(transcriptPath)) {
        const transcript = fs.readFileSync(transcriptPath, "utf8");
        if (/verify-against-codebase/i.test(transcript)) {
          backendInTranscript = true;
          frontendInTranscript = true;
        }
        if (/verify-backend/i.test(transcript)) backendInTranscript = true;
        if (/verify-frontend/i.test(transcript)) frontendInTranscript = true;
      }
    } catch {
      // transcript unreadable; err on the side of allowing rather than blocking
      allow();
    }
  }

  const backendOk = !requireBackend || backendFired || backendInTranscript;
  const frontendOk = !requireFrontend || frontendFired || frontendInTranscript;

  if (brainstormingFired && backendOk && frontendOk) allow();

  const missing = [];
  if (!brainstormingFired) missing.push("`superpowers:brainstorming`");
  if (!backendOk) missing.push("`verify-backend` (or legacy `verify-against-codebase`)");
  if (!frontendOk) missing.push("`verify-frontend` (or legacy `verify-against-codebase`)");

  const tierLabel =
    requireBackend && requireFrontend
      ? "mixed (both backend and frontend signals, OR signal-free spec — default-safe requires both)"
      : requireBackend
        ? "backend-only signals"
        : "frontend-only signals";

  const reason = [
    "Spec write blocked (Phase B in playbook/references/constitution.md).",
    "",
    `Spec content tier: ${tierLabel}.`,
    `Required skill(s) missing from this session's state file: ${missing.join(" + ")}.`,
    "",
    "Why:",
    "  • superpowers:brainstorming establishes the design + user approval before the spec is written.",
    "  • verify-backend / verify-frontend catch the failure mode of inventing homegrown infrastructure for problems the framework / existing project conventions already solve (e.g. a custom MfaTicketService duplicating ASP.NET Identity's Identity.TwoFactorUserId cookie; a hand-rolled `<Badge>` lookalike when the variant already exists).",
    "",
    "Recover:",
    "  • If you have a design but skipped the formal brainstorm, invoke `superpowers:brainstorming` first.",
    "  • Then invoke the appropriate verify skill against the proposed design.",
    "  • Bypass (verify only): mention the literal string `verify-backend`, `verify-frontend`, or `verify-against-codebase` in your next message — the hook scans the transcript for those strings. (No bypass for brainstorming — it must be invoked via Skill.)",
    "",
    "Existing-spec rewrites are allowed without re-running either skill — the gate only fires for NEW spec authoring.",
  ].join("\n");

  deny(reason);
});

function extractProposedContent(payload) {
  const t = payload?.tool_name || "";
  const ti = payload?.tool_input || {};
  if (t === "Write") return String(ti.content || "");
  if (t === "Edit") return String(ti.new_string || "");
  if (t === "MultiEdit") {
    const edits = Array.isArray(ti.edits) ? ti.edits : [];
    return edits.map((e) => String(e?.new_string || "")).join("\n");
  }
  return "";
}
