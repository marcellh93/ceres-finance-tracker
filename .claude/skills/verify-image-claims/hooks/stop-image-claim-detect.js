#!/usr/bin/env node
// Stop hook — blocks the Stop event when the assistant's last message asserts
// content about a user-provided screenshot/image WITHOUT having Read the
// cited image file in the same turn.
//
// Why this hook exists:
//   2026-05-20: During the Section E mobile-viewport walkthrough I shipped a
//   finding list F1..F11 about user-provided screenshots. Of the eleven
//   findings, only two held up under a Read of the actual image files. The
//   other nine were preflight-expectation anchoring — I treated the harness
//   surfacing screenshot LABELS as having examined the screenshot PIXELS.
//   Sibling failure mode to verify-runtime-state (inference substituting for
//   direct read), at the visual-evidence surface.
//
// Architecture (mirrors stop-runtime-state-claim.js):
//   - On Stop, read the most recent assistant message + recent tool-call
//     trail from the transcript.
//   - Scan for image-finding-phrase patterns that ALSO contain an image
//     reference signal.
//   - Apply guards before blocking:
//       Guard 1: IMAGE-READ-CO-LOCATED — a Read tool call on an image file
//                (image-cache path, or .png/.jpg/.jpeg/.webp) appears in
//                the assistant's recent tool calls this turn.
//       Guard 2: SOLICITATION-NOT-ASSERTION — the finding is framed as a
//                question to the user, not a claim ("can you confirm...",
//                "did the X appear below the crop?").
//       Guard 3: EXPLICIT-EXPECTATION — the claim is hedged as a prediction
//                or spec expectation, not an observation ("the spec says
//                X should appear — please confirm").
//   - Only block if a finding pattern matches AND none of the guards apply.

const fs = require("fs");
const path = require("path");

// Image-finding-phrase patterns. Each must co-occur (within a short window)
// with an image-reference signal in the same message — that conjunction is
// what distinguishes a real finding-claim from neutral text mentioning the
// same words.
//
// 2026-05-21 audit: tightened after a 100% production-misfire rate. The
// previous version included bare verbs (`/wraps?/`, `/overlaps?/`,
// `/touching/`, `/colliding/`, `/is missing/`) that match backend prose
// (the assistant wraps an API; lockout windows overlap; validation is
// missing) with no visual meaning. Those have been dropped or anchored
// to visual nouns within the same regex.
const FINDING_PHRASES = [
  // First-person observation verbs
  /\bI (can )?see\b[\s\S]{0,80}\b(image|screenshot|#\d|\.png)/i,
  /\b(the )?image (shows|reveals|has|carries|is missing|contains)\b/i,
  /\b(the )?screenshot (shows|reveals|has|carries|is missing|contains)\b/i,
  // Layout-break verbs anchored with a visual subject within ~40 chars.
  // Keeps `the button overflows the card`; drops bare `the function overflows`.
  /\b(button|label|input|cell|row|column|card|tile|stepper|dropdown|menu|modal|dialog|sheet|toast|banner|heading|text|copy|icon|avatar|tab|chip|badge|drawer)\b[\s\S]{0,40}\b(overflows?|clipping|clipped|clips at|crops at|cropped at|cut off|pushed off-screen|pushes into|crashes into|crammed)\b/i,
  /\b(button|label|input|cell|row|column|card|tile|stepper|dropdown|menu|modal|dialog|sheet|toast|banner|heading|text|copy|icon|avatar|tab|chip|badge|drawer)\b[\s\S]{0,40}\b(overlaps?|overlapping|touching|colliding|collides with|wraps mid-word|wraps awkwardly)\b/i,
  // Element-presence claims anchored with a visual noun in the same sentence.
  /\b(button|link|label|icon|cell|input|toggle|dropdown|menu|modal|dialog|tab|chip|badge|drawer|stepper|sheet|toast|banner|heading|avatar)\b[\s\S]{0,40}\b(is|are) (missing|absent|cropped|cut off|cut|hidden|not visible|not rendered|not shown)\b/i,
  /\b(no|zero) (button|link|label|icon|cell|input|toggle) (visible|appears|shows up|present)\b/i,
  // Findings list shapes — F1/F2/Finding N. Co-required with image refs below.
  /^\s*[*-]?\s*F\d+\s*[—-]/m,
  /^\s*[*-]?\s*Finding\s+\d+\s*[—:-]/im,
  /^\s*###\s*F\d+\b/m,
  // Quantitative claims that only make sense when looking at pixels
  /\bI count(ed)?\s+\d+\s+(cells?|cols?|columns?|rows?|items?|buttons?|inputs?)\b/i,
];

// Image-reference signals — at least one must appear in the message for the
// finding to be image-grounded.
//
// 2026-05-21 audit: removed the bare `/\(?#\d+\)?/` regex. It matched any
// `#NN` token — GitHub issue refs, PR numbers, tool-use IDs, internal
// ticket IDs — producing a 100% misfire rate on production traffic by
// always satisfying the image-ref gate even when no image was in scope.
// Only explicit-token forms remain.
const IMAGE_REFS = [
  /\bImage\s*#\s*\d+/i,                             // harness's own "Image #59"
  /\bscreenshot\s*#?\s*\d+/i,
  /\b\d+\.(png|jpg|jpeg|webp|gif)\b/i,              // bare filename
  /image-cache\/[^\s)]+/,                           // image-cache path fragment
];

// Guard 1 — Read tool call on an image path in this turn's tool trail.
// The transcript serializes tool_use blocks as JSON where the tool `name`
// and `file_path` arguments can appear in either order (`"name":"Read"`
// then `"file_path":"...png"`, or vice versa). So we check the two
// conditions separately and require BOTH on the same line.
const IMAGE_PATH_MARKER = /(image-cache\/[^\s"')]+|\/[\w./-]+\.(png|jpg|jpeg|webp|gif))/i;
const READ_TOOL_MARKER = /"name"\s*:\s*"Read"|tool_use[^}]*Read/i;

// Guard 2 — solicitation (question to user) rather than assertion.
const SOLICITATION_MARKERS = [
  /\bcan you (confirm|check|verify|tell me) (whether|if|that)\b/i,
  /\b(did|does) (the|that|this) [^?]{0,100}\?/i,
  /\bplease (confirm|check|verify) (whether|if|that)\b/i,
  /\bI need to ask\b/i,
];

// Guard 3 — explicit expectation/prediction rather than observation.
const EXPECTATION_MARKERS = [
  /\bthe spec (says|requires|expects|calls for)\b/i,
  /\b(my )?preflight (predicted|expected|said|claimed)\b/i,
  /\bI (predict|expect|anticipate) that\b/i,
  /\bif (the design|the spec|the contract) (is|holds)\b/i,
  /\bshould (appear|be visible|render|show up)\b/i,
];

// User-authorization guard — the user explicitly waived the Read requirement
// (rare; surfaces e.g. when the image is a public logo or the user already
// described what's in it).
const USER_AUTH = [
  /\b(skip|don't|no need to) read the image\b/i,
  /\bI'?ll describe (it|the image|the screenshot)\b/i,
  /\btrust me on the (image|screenshot)\b/i,
];

const SESSION_BYPASS = process.env.CERES_SKIP_IMAGE_CLAIMS_HOOK === "1";

let raw = "";
process.stdin.on("data", (c) => (raw += c));
process.stdin.on("end", () => {
  let input;
  try {
    input = JSON.parse(raw || "{}");
  } catch {
    process.exit(0);
  }

  if (input.stop_hook_active) process.exit(0);
  const transcriptPath = input.transcript_path || "";
  if (!transcriptPath || !fs.existsSync(transcriptPath)) process.exit(0);

  let lines;
  try {
    lines = fs.readFileSync(transcriptPath, "utf8").trim().split("\n");
  } catch {
    process.exit(0);
  }

  // Walk transcript backwards to collect:
  //   - last assistant text message
  //   - last user text message
  //   - the assistant's recent tool calls THIS TURN (between the last user
  //     message and the assistant text we're scanning)
  let lastAssistantText = "";
  let lastUserText = "";
  let lastAssistantIdx = -1;
  let lastUserIdx = -1;

  for (let i = lines.length - 1; i >= 0; i--) {
    let entry;
    try {
      entry = JSON.parse(lines[i]);
    } catch {
      continue;
    }
    if (entry.type === "assistant" && !lastAssistantText) {
      const msg = entry.message;
      if (msg && Array.isArray(msg.content)) {
        const text = msg.content
          .filter((c) => c && c.type === "text" && typeof c.text === "string")
          .map((c) => c.text)
          .join("\n");
        if (text) {
          lastAssistantText = text;
          lastAssistantIdx = i;
        }
      }
    }
    if (entry.type === "user" && !lastUserText) {
      const msg = entry.message;
      if (msg && typeof msg.content === "string") {
        lastUserText = msg.content;
        lastUserIdx = i;
      } else if (msg && Array.isArray(msg.content)) {
        const text = msg.content
          .filter((c) => c && (c.type === "text" || typeof c === "string"))
          .map((c) => (typeof c === "string" ? c : c.text))
          .filter(Boolean)
          .join("\n");
        if (text) {
          lastUserText = text;
          lastUserIdx = i;
        }
      }
    }
    if (lastAssistantText && lastUserText) break;
  }

  if (!lastAssistantText) process.exit(0);

  // First check: does the assistant message contain a finding phrase AND
  // an image reference? Both must be present for the hook to engage at all.
  const findingMatched = FINDING_PHRASES.filter((re) => re.test(lastAssistantText)).map((re) =>
    re.toString().slice(0, 80),
  );
  const imageRefMatched = IMAGE_REFS.some((re) => re.test(lastAssistantText));

  if (findingMatched.length === 0 || !imageRefMatched) process.exit(0);

  // Guard 1 — scan the turn's tool trail for an image-Read. Walk from the
  // last user message forward up to (but not including) the last assistant
  // text message, looking at tool_use / tool_result entries. A line counts
  // as an image-Read if it carries both an image-path marker AND a
  // Read-tool marker (order-independent because the JSON serialization
  // may place `name:Read` before or after `file_path:...png`).
  let imageReadCoLocated = false;
  if (lastUserIdx >= 0 && lastAssistantIdx >= 0) {
    for (let i = lastUserIdx + 1; i < lastAssistantIdx; i++) {
      const blob = lines[i] || "";
      if (IMAGE_PATH_MARKER.test(blob) && READ_TOOL_MARKER.test(blob)) {
        imageReadCoLocated = true;
        break;
      }
    }
  }

  // Guard 2 — solicitation (question, not claim).
  const solicitation = SOLICITATION_MARKERS.some((re) => re.test(lastAssistantText));

  // Guard 3 — explicit prediction/expectation framing.
  const expectation = EXPECTATION_MARKERS.some((re) => re.test(lastAssistantText));

  // User authorization in their last message.
  const userAuthorized = USER_AUTH.some((re) => re.test(lastUserText));

  const guardsPassed =
    imageReadCoLocated || solicitation || expectation || userAuthorized || SESSION_BYPASS;

  const outcome = SESSION_BYPASS ? "bypassed" : guardsPassed ? "passed" : "blocked";
  try {
    const stateDir = path.join(
      process.env.CLAUDE_PROJECT_DIR || process.cwd(),
      ".claude/state/image-claims-verify",
    );
    fs.mkdirSync(stateDir, { recursive: true });
    const logEntry = {
      ts: new Date().toISOString(),
      sessionId: input.session_id || "",
      hook: "image-claim-detect",
      outcome,
      findingMatched,
      imageRefMatched,
      guards: {
        imageReadCoLocated,
        solicitation,
        expectation,
        userAuthorized,
        passed: guardsPassed,
      },
      assistantSnippet: lastAssistantText.slice(0, 360),
      userSnippet: lastUserText.slice(0, 200),
    };
    fs.appendFileSync(path.join(stateDir, "log.jsonl"), JSON.stringify(logEntry) + "\n");
  } catch {
    /* best-effort log */
  }

  if (guardsPassed) process.exit(0);

  const reason = [
    "🛑 Image-claim detected without co-located Read of the image file.",
    "",
    `Matched finding phrases: ${findingMatched.join(", ")}`,
    "Message references at least one image (Image #N / *.png / image-cache path).",
    "",
    "Four guards were checked AND ALL FAILED:",
    `  • Image-Read-co-located guard: ${imageReadCoLocated ? "PASS" : "fail"} (no Read tool call on an image path found in this turn's tool trail)`,
    `  • Solicitation guard: ${solicitation ? "PASS" : "fail"} (the message asserts content, doesn't ask the user a question about it)`,
    `  • Expectation-framing guard: ${expectation ? "PASS" : "fail"} (the message states observations, doesn't frame them as predictions/spec expectations)`,
    `  • User-authorization guard: ${userAuthorized ? "PASS" : "fail"} (user did not waive the Read in their last message)`,
    "",
    "The `verify-image-claims` skill applies to ANY claim about the content of",
    "a user-provided screenshot or image. The harness surfacing a screenshot",
    "label does NOT constitute having examined the screenshot pixels.",
    "Anchoring on preflight expectations and reading screenshots through that",
    "lens — the 2026-05-20 Section E F1-F11 incident, nine of eleven findings",
    "wrong — is exactly what this hook exists to prevent.",
    "",
    "Recovery options:",
    "  • Read each cited image file by absolute path, THEN re-state the",
    "    finding anchored to specific regions you observed (top stepper, last",
    "    OTP cell, bottom-right of the card, etc.).",
    "  • If your preflight expected a problem and the image doesn't show it,",
    "    say so explicitly: \"predicted F2 OTP overflow; image shows comfortable",
    "    fit, prediction wrong\".",
    "  • Reframe the claim as a question (\"can you confirm whether the Copy",
    "    button appears below the crop?\") if you genuinely can't see the answer",
    "    in the screenshot you have.",
    "  • Frame as expectation (\"the spec says X should appear — please",
    "    confirm in the actual render\") if you're stating what should be true",
    "    rather than what you observed.",
    "  • If the image is a widely-known reference (brand logo, public docs)",
    "    where a Read would not add ground truth, set CERES_SKIP_IMAGE_CLAIMS_HOOK=1.",
    "",
    "See .claude/state/image-claims-verify/log.jsonl for the audit trail.",
  ].join("\n");

  process.stderr.write(reason);
  process.exit(2);
});
