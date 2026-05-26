#!/usr/bin/env node
// turn-shape.json generator — 9.5a evidence bundle slot. Closes the
// verbal-promise / research-before-confidence / runtime-assertion gaps the
// deleted prose-reading Stop hooks used to catch. Usage:
//   node turn-shape-generator.js --transcript <path> --stage-id <id> \
//     [--turn-start <ISO>] --output <path>
// Exit 0 on success; 1 on unreadable transcript or invalid args.

const fs = require("fs");
const path = require("path");
const crypto = require("crypto");

// ─── Config (tune here — dispatcher below is generic) ────────────────────────

const FIX_MENTION_PATTERNS = [
  /\bthe fix is\b/i,
  /\bthe bug is\b/i,
  /\bI'?ll fix\b/i,
  /\bfixing (this|that|it) now\b/i,
  /\bI'?m going to fix\b/i,
  // "the problem is" only counts when followed by a location ("here:" / "in <path>")
  /\bthe problem is\b(?=[^.\n]{0,80}?\b(here[:\s]|in [A-Za-z0-9_./]+\.(cs|ts|tsx|js|jsx|md|json|sh|sql)\b))/i,
  // "root cause is" only counts when followed by a fix proposal
  /\broot cause is\b(?=[^.\n]{0,160}?\b(fix|change|replace|add|remove|patch|update|rewrite)\b)/i,
];

const CONFIDENCE_PATTERNS = [
  /\b(root cause|the cause|the issue|the answer) is\b/i,
  // "this is because" must be in a finding/diagnosis context
  /\bthis is because\b(?=[\s\S]{0,200}?\b(found|identified|discovered|root cause|diagnosis|traced|isolated)\b)|(?<=\b(found|identified|discovered|root cause|diagnosis|traced|isolated)\b[\s\S]{0,200})\bthis is because\b/i,
  /\bthe (correct|right) (approach|pattern|way) is\b/i,
  /\bMicrosoft Learn (says|states|confirms)\b/i,
  /\bthe (docs|documentation|spec) (say|says|states|confirms)\b/i,
  /\bper (the spec|the docs|the documentation)\b/i,
];

const RUNTIME_PATTERNS = [
  // Field.Property = value  (e.g. "user.TwoFactorEnabled = false")
  /\b[A-Za-z]\w*\.\w+\s*=\s*(true|false|null|\d+|'[^']+'|"[^"]+")\b/,
  // Field.Property is/equals/returns value
  /\b[A-Za-z]\w*\.\w+\s+(is|equals|returns)\s+(true|false|null|\d+|'[^']+'|"[^"]+")\b/i,
  // Bare CamelCase column/field name asserted: "TwoFactorEnabled is false"
  // Requires the identifier to be ≥2 chars, start uppercase, contain a lower-
  // case run (filters out acronyms like "TLS is true" which would be code-talk).
  /\b[A-Z][a-z]\w*[a-z]\w*\s+(is|equals|returns)\s+(true|false|null|\d+|'[^']+'|"[^"]+")\b/,
  // env var assertions
  /\b(env\.|process\.env\.)\w+\s*=/i,
  // "this column / the row has / contains / equals"
  /\b(this|the)\s+(column|row|field|table)\s+(has|contains|equals)\b/i,
];

// Quote-introducing prefixes — text within 200 chars after these is skipped.
const QUOTE_INTRO_PHRASES = [
  /\bverbatim quote:/i,
  /\buser said:/i,
  /\bthe user wrote:/i,
  /\bprior turn said:/i,
  /\bquoted from\b[^:]{0,30}:/i,
];
const QUOTE_INTRO_WINDOW = 200;

// Diagnosis-template heading sections to skip (prior-turn quotes live here).
const SKIP_HEADINGS = [
  /^##+\s+Failed[- ]attempts?\b/im,
  /^##+\s+Prior[- ]turn quotes\b/im,
  /^##+\s+Verbatim quotes\b/im,
];

const EDIT_TOOLS = new Set(["Edit", "Write", "MultiEdit", "NotebookEdit"]);
const RESEARCH_TOOLS = new Set(["WebFetch", "WebSearch", "Agent", "Task"]);
const QUERY_BASH_COMMANDS = /\b(psql|curl|cat|ls|find|grep|head|tail|awk|sed)\b/;

// ─── Discussion-frame strip (inlined fallback) ───────────────────────────────

// Prefer the shared lib (sibling precedent: stop-chat-deferral-detect.js);
// fall back to inline regex if the lib is removed.
let stripDiscussionFrames;
try {
  ({ stripDiscussionFrames } = require(path.join(
    process.env.CLAUDE_PROJECT_DIR || process.cwd(),
    ".claude/hooks/lib/discussion-frame-strip.js",
  )));
} catch {
  const FENCED = /```[\s\S]*?```/g;
  const INLINE = /`[^`\n]+`/g;
  const BLOCKQ = /^>.*$/gm;
  const STAGEH = /^##\s+Stage\s+\d+(\.\d+)*\b.*$/gim;
  stripDiscussionFrames = (t) => !t ? "" : t.replace(FENCED, "").replace(INLINE, "").replace(BLOCKQ, "").replace(STAGEH, "");
}

function stripForClaimDetection(text) {
  if (!text) return "";
  let out = stripDiscussionFrames(text);
  out = out.replace(/`[^`\n]+`/g, "");
  // Drop diagnosis-template sections (heading + body until next heading).
  for (const re of SKIP_HEADINGS) {
    const m = out.match(re);
    if (m) {
      const start = m.index;
      const rest = out.slice(start + m[0].length);
      const nextHeading = rest.search(/^##+\s+/m);
      const end = nextHeading >= 0
        ? start + m[0].length + nextHeading
        : out.length;
      out = out.slice(0, start) + out.slice(end);
    }
  }
  // Mask quote-introduction windows with whitespace of identical length so
  // offsets stay stable.
  for (const intro of QUOTE_INTRO_PHRASES) {
    const re = new RegExp(intro.source, intro.flags.includes("g") ? intro.flags : intro.flags + "g");
    let match;
    while ((match = re.exec(out)) !== null) {
      const windowEnd = Math.min(out.length, match.index + match[0].length + QUOTE_INTRO_WINDOW);
      const len = windowEnd - match.index;
      out = out.slice(0, match.index) + " ".repeat(len) + out.slice(windowEnd);
      re.lastIndex = windowEnd;
    }
  }
  return out;
}

// ─── Arg parsing ─────────────────────────────────────────────────────────────

function parseArgs(argv) {
  const args = {};
  for (let i = 2; i < argv.length; i++) {
    const k = argv[i];
    if (k.startsWith("--")) {
      const key = k.slice(2);
      const v = argv[i + 1];
      if (v === undefined || v.startsWith("--")) {
        args[key] = true;
      } else {
        args[key] = v;
        i++;
      }
    }
  }
  return args;
}

// ─── Transcript reading ──────────────────────────────────────────────────────

function readTranscript(transcriptPath) {
  let raw;
  try {
    raw = fs.readFileSync(transcriptPath, "utf8");
  } catch (e) {
    process.stderr.write(`turn-shape-generator: cannot read transcript ${transcriptPath}: ${e.message}\n`);
    process.exit(1);
  }
  const events = [];
  for (const line of raw.split("\n")) {
    if (!line.trim()) continue;
    try {
      events.push(JSON.parse(line));
    } catch {
      // Skip malformed lines — transcripts occasionally have truncated tails.
    }
  }
  return events;
}

// Find the most recent user-message timestamp if --turn-start was omitted.
function defaultTurnStart(events) {
  let latest = null;
  for (const e of events) {
    if (e.type === "user" && e.message && e.message.role === "user" && e.timestamp) {
      latest = e.timestamp;
    }
  }
  return latest;
}

// Return events with ISO timestamp >= turnStart.
function eventsInTurn(events, turnStartISO) {
  const cutoff = Date.parse(turnStartISO);
  if (Number.isNaN(cutoff)) return events;
  return events.filter((e) => {
    if (!e.timestamp) return false;
    const t = Date.parse(e.timestamp);
    return !Number.isNaN(t) && t >= cutoff;
  });
}

// ─── Tool-call collection ────────────────────────────────────────────────────

function collectToolCalls(turnEvents) {
  const calls = [];
  for (const e of turnEvents) {
    if (e.type !== "assistant" || !e.message || !Array.isArray(e.message.content)) continue;
    for (const block of e.message.content) {
      if (block.type !== "tool_use") continue;
      calls.push({
        name: block.name,
        input: block.input || {},
        id: block.id,
        timestamp: e.timestamp,
      });
    }
  }
  return calls;
}

// Concatenate text blocks; record each tool_use's offset in the concat'd text
// so we can ask "did an Edit appear AFTER offset N?".
function concatAssistantText(turnEvents) {
  let text = "";
  // List of { offset, call } — the call was emitted at this offset in `text`.
  const toolBoundaries = [];
  for (const e of turnEvents) {
    if (e.type !== "assistant" || !e.message || !Array.isArray(e.message.content)) continue;
    for (const block of e.message.content) {
      if (block.type === "text" && typeof block.text === "string") {
        text += block.text + "\n";
      } else if (block.type === "tool_use") {
        toolBoundaries.push({
          offset: text.length,
          call: {
            name: block.name,
            input: block.input || {},
            id: block.id,
            timestamp: e.timestamp,
          },
        });
      }
    }
  }
  return { text, toolBoundaries };
}

// Run patterns against stripped text; dedupe by offset (one entry per offset).
function findMatches(strippedText, patterns) {
  const matches = [];
  const seen = new Set();
  for (const re of patterns) {
    const g = new RegExp(re.source, re.flags.includes("g") ? re.flags : re.flags + "g");
    let m;
    while ((m = g.exec(strippedText)) !== null) {
      if (m.index === g.lastIndex) g.lastIndex++; // zero-width safety
      if (seen.has(m.index)) continue;
      seen.add(m.index);
      const snippetStart = Math.max(0, m.index - 50);
      const snippetEnd = Math.min(strippedText.length, m.index + 100);
      matches.push({
        offset: m.index,
        match: m[0],
        snippet: strippedText.slice(snippetStart, snippetEnd).replace(/\s+/g, " ").trim(),
      });
    }
  }
  return matches.sort((a, b) => a.offset - b.offset);
}

// ─── Co-location checks ──────────────────────────────────────────────────────

function findEditAfter(offset, toolBoundaries) {
  for (const { offset: o, call } of toolBoundaries) {
    if (o >= offset && EDIT_TOOLS.has(call.name)) {
      return call;
    }
  }
  return null;
}

function findResearchAnywhere(toolBoundaries) {
  // Research can come before or after — both count.
  for (const { call } of toolBoundaries) {
    if (RESEARCH_TOOLS.has(call.name)) return call;
  }
  return null;
}

function findQueryAnywhere(toolBoundaries, claimSnippet) {
  // Match either: Bash with a query-shaped command, or Read against a file
  // whose name appears in the claim snippet.
  for (const { call } of toolBoundaries) {
    if (call.name === "Bash") {
      const cmd = (call.input && call.input.command) || "";
      if (QUERY_BASH_COMMANDS.test(cmd)) {
        return { tool: "Bash", command: cmd.slice(0, 200) };
      }
    }
    if (call.name === "Read") {
      const fp = (call.input && call.input.file_path) || "";
      if (fp && claimSnippet) {
        // Look for the basename of the read file in the claim snippet.
        const base = path.basename(fp);
        if (base && claimSnippet.includes(base)) {
          return { tool: "Read", file_path: fp };
        }
      }
    }
  }
  return null;
}

// ─── Build the three categories ──────────────────────────────────────────────

function buildFixMentions(strippedText, toolBoundaries) {
  const matches = findMatches(strippedText, FIX_MENTION_PATTERNS);
  return matches.map((m) => {
    const edit = findEditAfter(m.offset, toolBoundaries);
    return {
      assistant_message_offset: m.offset,
      snippet: m.snippet,
      co_located_edit: !!edit,
      edit_file: edit ? (edit.input.file_path || null) : null,
    };
  });
}

function buildConfidenceClaims(strippedText, toolBoundaries) {
  const matches = findMatches(strippedText, CONFIDENCE_PATTERNS);
  const research = findResearchAnywhere(toolBoundaries);
  return matches.map((m) => ({
    assistant_message_offset: m.offset,
    snippet: m.snippet,
    co_located_research: !!research,
    research_tool: research ? research.name : null,
    url: research && research.input && research.input.url ? research.input.url : null,
  }));
}

function buildRuntimeAssertions(strippedText, toolBoundaries) {
  const matches = findMatches(strippedText, RUNTIME_PATTERNS);
  return matches.map((m) => {
    const q = findQueryAnywhere(toolBoundaries, m.snippet);
    return {
      assistant_message_offset: m.offset,
      snippet: m.snippet,
      co_located_query: !!q,
      query_output: null, // generator does not have the per-call output sink; consumer wires it
      tool_used: q ? q.tool : null,
    };
  });
}

// ─── Output ──────────────────────────────────────────────────────────────────

function shortHash(s) {
  return crypto.createHash("sha1").update(s).digest("hex").slice(0, 12);
}

function main() {
  const args = parseArgs(process.argv);
  if (!args.transcript || !args["stage-id"] || !args.output) {
    process.stderr.write(
      "Usage: turn-shape-generator.js --transcript <path> --stage-id <id> [--turn-start <ISO>] --output <path>\n",
    );
    process.exit(1);
  }

  const events = readTranscript(args.transcript);
  const turnStart = args["turn-start"] || defaultTurnStart(events);
  const turnEvents = turnStart ? eventsInTurn(events, turnStart) : events;

  const { text, toolBoundaries } = concatAssistantText(turnEvents);
  const strippedText = stripForClaimDetection(text);

  const fix_mentions = buildFixMentions(strippedText, toolBoundaries);
  const confidence_claims = buildConfidenceClaims(strippedText, toolBoundaries);
  const runtime_assertions = buildRuntimeAssertions(strippedText, toolBoundaries);

  const bundle = {
    stage_id: args["stage-id"],
    turn_id: shortHash(`${turnStart || ""}|${args.transcript}`),
    generated_at: new Date().toISOString(),
    fix_mentions,
    confidence_claims,
    runtime_assertions,
  };

  const outDir = path.dirname(args.output);
  fs.mkdirSync(outDir, { recursive: true });
  fs.writeFileSync(args.output, JSON.stringify(bundle, null, 2) + "\n", "utf8");

  process.stderr.write(
    `turn-shape-generator: wrote ${args.output} ` +
      `(fix=${fix_mentions.length}, conf=${confidence_claims.length}, runtime=${runtime_assertions.length})\n`,
  );
  process.exit(0);
}

if (require.main === module) main();

module.exports = {
  stripForClaimDetection,
  findMatches,
  buildFixMentions,
  buildConfidenceClaims,
  buildRuntimeAssertions,
  FIX_MENTION_PATTERNS,
  CONFIDENCE_PATTERNS,
  RUNTIME_PATTERNS,
};
