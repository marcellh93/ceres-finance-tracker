// Shared helper: strip "discussion frame" content from an assistant message
// before phrase-matching, so hooks don't fire on text the assistant is
// quoting, recovering from, or echoing rather than asserting.
//
// Pattern A in docs/hook-architecture-audit-2026-05-22.md. Adopted by every
// Stop hook that scans assistant prose for surface phrases.
//
// stripDiscussionFrames(text) — returns a version of `text` with:
//   - fenced code blocks (``` ... ```) removed
//   - blockquoted lines (^>) removed
//   - markdown stage headings (^## Stage \d+...) removed
//   - JSON-shaped tool-use / task-notification payloads removed
//
// It does NOT strip lines near an "audit trail" / "log.jsonl" reference — an
// earlier version of this header claimed it did. That case is handled by
// isMetaContext() below, which the caller short-circuits on instead.
//
// isMetaContext(text) — true if the message is discussing a hook itself
// (recovery instructions, audit-trail explanation, "false positive" framing).
// Short-circuit: if true, the calling hook should exit 0 BEFORE running the
// phrase matcher.

const FENCED_CODE = /```[\s\S]*?```/g;
const BLOCKQUOTE_LINE = /^>.*$/gm;
const STAGE_HEADING = /^##\s+Stage\s+\d+(\.\d+)*\b.*$/gim;
// JSON tool-use / task-notification payload that may be echoed into the
// transcript. Match conservatively: a JSON object containing one of
// "tool_use_id", "task-id", "tool_use", "agentId", or "transcript_path".
const TOOL_PAYLOAD = /\{[^{}]*?"(tool_use_id|task[-_]id|tool_use|agentId|transcript_path)"[^{}]*?\}/g;

const META_MARKERS = [
  /\bthe (hook|regex|guard|matcher|state machine) (fired|matched|caught|missed|deadlocked|stuck)\b/i,
  /\bfalse[- ]positive\b/i,
  /\baudit trail\b/i,
  /\blog\.jsonl\b/i,
  /\b(this|that) (will|would|did) (re-?)?fire the hook\b/i,
  /\bhook (recursion|deadlock|misfire)\b/i,
  /\bstop[- ]hook (feedback|blocking error)\b/i,
  /\brecovery options?:/i,
  /\bmatched (phrase|pattern)s?:/i,
];

function stripDiscussionFrames(text) {
  if (!text) return "";
  let out = text;
  out = out.replace(FENCED_CODE, "");
  out = out.replace(BLOCKQUOTE_LINE, "");
  out = out.replace(STAGE_HEADING, "");
  out = out.replace(TOOL_PAYLOAD, "");
  return out;
}

function isMetaContext(text) {
  if (!text) return false;
  return META_MARKERS.some((re) => re.test(text));
}

module.exports = { stripDiscussionFrames, isMetaContext, META_MARKERS };
