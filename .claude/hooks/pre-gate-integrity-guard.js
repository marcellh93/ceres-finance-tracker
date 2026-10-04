#!/usr/bin/env node
// PreToolUse hook — denies Edit/Write/MultiEdit against GATE and EVIDENCE files,
// so the agent can never hand-edit a check or a proof-of-work artifact to force a
// green result.
//
// Two file classes are guarded:
//   • GATE files   — the check logic itself: any *.js under a hooks/ dir inside
//                    .claude/ (.claude/hooks/** and .claude/skills/**/hooks/**),
//                    plus the hook registration in .claude/settings.json.
//   • EVIDENCE files — proof-of-work artifacts the gates read:
//                    .claude/state/evidence/**  (turn-shape.json, walk-summary.json,
//                    build-matrix.json, reviewer-pipeline.json, etc.).
//
// WHY THIS EXISTS (2026-10-04, Stage 13):
//   The evidence-bundle Stop hook blocked an i18n-locale commit, demanding a
//   browser walk. The agent's first response was to hand-write turn-shape.json to
//   satisfy the hook; its second was to edit the hook predicate to exclude .json so
//   the check would stop firing. BOTH were attempts to force a green result instead
//   of producing the evidence the gate asks for. The gate was correct. The user's
//   ruling: "You can't under any circumstance, current or future, modify a file to
//   force a false positive." This hook makes that mechanical.
//
// Evidence artifacts must be written by the MEASURING TOOLS (agent-walk, the
// build-matrix script, psql) which run as Bash commands — NOT as Edit/Write tool
// calls. This hook only screens Edit/Write/MultiEdit, so tool-written evidence
// (a real measurement) passes untouched; only the agent typing into these files is
// denied.
//
// BYPASS (A1, confirmed by the user 2026-10-04) — requires BOTH, mirroring
// pre-protected-path-gate.js's two-factor model:
//   (a) env var  CERES_ALLOW_GATE_EDIT=1   (only the user can set a shell var in
//       their session; the agent's tool calls cannot), AND
//   (b) the user's most recent message literally names the file being edited
//       (basename or repo-relative path).
//   The env var alone is not enough, and the agent asserting "the user approved" in
//   chat is not enough — the user must have both set the token and named the target.
//   This is deliberately un-launderable: the agent can only ASK the user to do both.
//
// NOT doom-loop fuel: it reads the tool's file_path (independent evidence), never
// assistant prose. The agent cannot rephrase its way past it.

const fs = require("fs");
const path = require("path");

const PROJECT_DIR = process.env.CLAUDE_PROJECT_DIR || process.cwd();
const STATE_DIR = path.join(PROJECT_DIR, ".claude", "state", "gate-integrity-guard");

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

// Classify an absolute path as a guarded GATE or EVIDENCE file, or null.
function classify(absPath) {
  const rel = absPath.startsWith(PROJECT_DIR + path.sep)
    ? absPath.slice(PROJECT_DIR.length + 1)
    : absPath;
  const norm = rel.split(path.sep).join("/");

  // EVIDENCE: anything under .claude/state/evidence/
  if (/^\.claude\/state\/evidence\//.test(norm)) return "evidence";

  // GATE: a *.js or *.sh inside any hooks/ dir under .claude/ (covers
  // .claude/hooks/** and .claude/skills/**/hooks/** — run-tests.sh, the Stop test
  // gate, and the two PreToolUse shell hooks are .sh), or the hook registration in
  // settings.json. Exclude the hooks' own __tests__/ dir: those are test fixtures,
  // not live gates, and blocking them would wall off the guard's own test suite.
  if (/^\.claude\/(hooks|skills\/[^/]+\/hooks)\//.test(norm) &&
      /\.(js|sh)$/.test(norm) &&
      !/\/__tests__\//.test(norm)) return "gate";
  if (norm === ".claude/settings.json") return "gate";

  return null;
}

// Does the user's most recent message literally contain the needle?
function userMessageContains(transcriptPath, needle) {
  if (!transcriptPath || !fs.existsSync(transcriptPath)) return false;
  let lines;
  try {
    lines = fs.readFileSync(transcriptPath, "utf8").trim().split("\n");
  } catch {
    return false;
  }
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

// Exported for tests. Only read stdin when run as a hook.
if (typeof module !== "undefined" && module.exports) {
  module.exports = { classify };
}

if (require.main === module) {
  let raw = "";
  process.stdin.on("data", (c) => (raw += c));
  process.stdin.on("end", () => {
    let payload;
    try { payload = JSON.parse(raw || "{}"); } catch { allow(); }

    const toolName = payload.tool_name || "";
    if (!/^(Edit|Write|MultiEdit)$/.test(toolName)) allow();

    const filePath = payload.tool_input && payload.tool_input.file_path;
    if (!filePath) allow();

    const abs = path.isAbsolute(filePath)
      ? filePath
      : path.resolve(PROJECT_DIR, String(filePath).replace(/^\.\//, ""));

    const kind = classify(abs);
    if (!kind) allow();

    // Two-factor bypass: env token AND the user naming the target in their last message.
    const envOk = /^(1|true|yes)$/i.test(process.env.CERES_ALLOW_GATE_EDIT || "");
    const relForMsg = abs.startsWith(PROJECT_DIR + path.sep)
      ? abs.slice(PROJECT_DIR.length + 1)
      : abs;
    const transcriptPath = payload.transcript_path || "";
    const userOk =
      userMessageContains(transcriptPath, path.basename(abs)) ||
      userMessageContains(transcriptPath, relForMsg);

    if (envOk && userOk) {
      log(payload, "bypassed", { file: relForMsg, kind });
      allow();
    }

    const label = kind === "evidence" ? "evidence/proof-of-work artifact" : "gate (check) file";
    const reason = [
      `🛑 Gate-integrity guard — refusing to edit a ${label}.`,
      "",
      `Target: ${relForMsg}`,
      "",
      kind === "evidence"
        ? [
            "Evidence files record a MEASURED result — they are written by the measuring",
            "tools (agent-walk, tools/agent-env/build-matrix.sh, psql), never typed by the",
            "agent. Hand-editing one asserts a result instead of recording one. That is",
            "forcing a false positive, which is forbidden.",
            "",
            "To produce this evidence legitimately: run the tool the gate's recovery text",
            "names (e.g. tools/agent-env/up.sh then `STAGE_ID=.. pnpm --dir",
            "ProjectCeres.Client agent-walk`), which writes the artifact for real.",
          ].join("\n")
        : [
            "Gate files are the checks themselves. Editing one to make a check pass (or",
            "stop firing) is forcing a false positive, which is forbidden — even when the",
            "agent believes the check is wrong.",
            "",
            "If a check is genuinely a false positive: research it, PROVE it, and present",
            "the proposed change to the user. The user applies it, or authorizes it via the",
            "two-factor bypass below.",
          ].join("\n"),
      "",
      "Bypass (user authority only) requires BOTH:",
      `  (a) env var:  CERES_ALLOW_GATE_EDIT=1   (currently: ${process.env.CERES_ALLOW_GATE_EDIT ? `"${process.env.CERES_ALLOW_GATE_EDIT}"` : "(unset)"})`,
      "  (b) the user's most recent message literally names this file.",
      "The agent cannot satisfy either on its own — only the user can set the env var",
      "and name the target. Asserting 'the user approved' in chat is NOT sufficient.",
      "",
      "Audit log: .claude/state/gate-integrity-guard/log.jsonl",
    ].join("\n");

    log(payload, "blocked", { file: relForMsg, kind });
    deny(reason);
  });
}
