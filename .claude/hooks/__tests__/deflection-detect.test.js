const { test } = require("node:test");
const assert = require("node:assert");
const { findDeflection } = require("../stop-deflection-detect.js");

// The move this hook forbids: a KNOWN defect framed as "pre-existing / not mine /
// out of scope" COUPLED with an explicit refusal to fix it this turn. The age of a
// defect is irrelevant when you are touching the file. Built 2026-10-06 after the
// assistant committed exactly this ("not mine to fix in this commit… pre-existing…
// I'll flag it, not fold it in"), repeatedly corrected by the user.

const BLOCK = [
  // The verbatim offense that motivated the hook.
  "So: not mine to fix in this commit. Changing the raw NUL to the escape is a reasonable cleanup, but it is an independent change to a gate hook, pre-existing, and touching it risks changing the hash. I will flag it, not fold it in.",
  "This is pre-existing so I am not gonna fix it.",
  "That is a pre-existing bug in the shared classifier. It is not mine to fix here, so I will leave it alone and note it for later.",
];

const PASS = [
  // Fixing it now — the age is mentioned but the defect is being handled.
  "This NUL byte is pre-existing, but I am fixing it now in the same pass since I am touching the file.",
  "The bug is pre-existing; I am folding it in to this commit since I am here.",
  // Valid deferral: a real feature, user-decided, with a receiving line.
  "This is out of scope for this commit; deferred with a receiving [ ] line in the roadmap + a tripwire, per the user decision.",
  // Merely naming a bug's age while acting on it.
  "The bug predates this session; I have root-caused it and the fix is below.",
  "This file has a pre-existing NUL byte. Fixing it.",
  // A refusal with no deflection framing is not this hook's concern.
  "I will leave it as-is because the tests already cover it.",
  // Deflection and an unrelated refusal in SEPARATE paragraphs must not couple.
  "This feature is out of scope for this commit.\n\nSeparately, I will leave the formatting as-is since it is fine.",
];

for (const [i, txt] of BLOCK.entries()) {
  test(`blocks the pre-existing/not-mine deflection #${i + 1}`, () => {
    assert.ok(findDeflection(txt), `expected a block, got none for: ${txt.slice(0, 80)}`);
  });
}

for (const [i, txt] of PASS.entries()) {
  test(`does not false-positive on legitimate text #${i + 1}`, () => {
    assert.strictEqual(findDeflection(txt), null, `false positive on: ${txt.slice(0, 80)}`);
  });
}

// The specific regression: "not fold it in" is a REFUSAL, and must not be read as
// the "fold it in" ACQUITTAL (the negation trap that let the real offense through).
test("negation trap: 'not fold it in' is a refusal, not an acquittal", () => {
  assert.ok(
    findDeflection(
      "It is pre-existing and not mine to fix, so I will flag it, not fold it in.",
    ),
    "'not fold it in' must not be acquitted by the bare 'fold it in' pattern",
  );
});
