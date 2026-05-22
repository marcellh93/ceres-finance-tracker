---
name: verify-image-claims
description: >
  Use BEFORE making any observation or finding about a user-provided screenshot
  or image. Catches the failure mode where the assistant asserts what is "in"
  an image (overflow, clipping, missing buttons, collisions, layout breaks)
  while having only anchored on a preflight expectation, not on the actual
  pixels — i.e. the harness surfaced the screenshot but the assistant never
  Read'd the image file before forming the finding. Triggers on phrases like
  "I see", "the image shows", "appears to", "clips at", "overflows", "is
  missing", or any finding-list syntax (F1, Finding N) referencing an image.
  Sibling to verify-runtime-state at the visual-evidence surface — both
  enforce direct-evidence-before-claim discipline. HARD-enforced via a Stop
  hook; this doc explains the rule so the hook is satisfied in advance.
---

# verify-image-claims

This skill exists because of one real incident on 2026-05-20: during the
Section E mobile-viewport walkthrough, I listed eleven findings (F1–F11)
about user-provided screenshots — calling out OTP overflow, stepper-label
collisions ("DoneCancel"), missing buttons. When the user pushed back asking
where I saw the overflow, I had to re-Read the images and discover that
**of the eleven findings, only two were real and the other nine were me
anchoring on my preflight script and reading the screenshots through that
lens instead of reading what was actually there.** I had treated the harness
showing me eleven image labels as having examined eleven images.

The failure is structurally identical to the runtime-state-claim failure
(`verify-runtime-state`): claiming a state value from inference instead of
direct read. There, the inference was code-grep substituting for a DB query.
Here, it was preflight-expectations substituting for image pixels. The fix
is the same shape: surface the rule before the claim, hook-enforce it after.

## When this skill applies

Before any assertion in user-facing text about the contents of a
user-provided screenshot or image:

- Visual layout claims: "the card is centered", "the cells overflow", "the
  button is cropped", "the stepper labels collide", "the QR code is too
  small"
- Element-presence claims: "X is missing", "the Copy button doesn't appear",
  "there's no language toggle"
- Element-relationship claims: "X is touching Y", "the chevron pushes into
  the input", "the label wraps mid-word"
- Quantitative claims: "I count six cells", "the gap is ~8px", "the button
  is at the bottom of the visible card"

For each such claim, I must FIRST have Read the image file by absolute path
in this turn. The user-message screenshot block tells me an image exists;
it does not constitute me having examined it.

If multiple images are referenced in a finding list, each image must have
its own Read call. One Read per image — not "Read once, comment on five."

## What does NOT trigger this skill

- Asking the user a clarifying question about what they see ("did the
  button appear below the screenshot crop?") — that's solicitation, not
  assertion.
- Referencing an image's presence without making a content claim ("you
  attached images #59 through #71").
- Pure metadata about an image ("Image #58 is the network-tab screenshot")
  — no pixel-level observation.
- Claims about NON-screenshot content the user provided as text (logs,
  diffs, JSON dumps).

## The rule

**Every image finding goes inside an explicit `<image-finding>` marker block, and every block must be paired with a Read of the cited image in the same turn.**

The marker block carries the image's absolute path as an attribute:

```
<image-finding image="/Users/.../image-cache/<session>/60.png">
F2 — `/login/totp` last OTP cell clips at the card's right padding.
</image-finding>
```

The same turn must contain a tool call equivalent to:

```
Read(file_path: "/Users/.../image-cache/<session>/60.png")
```

The Stop hook (2026-05-22 rewrite, convention-scanning) extracts every `<image-finding>` block from the assistant's last message, then checks the recent tool-call history for a matching `Read` on the declared path. If any cited image was not Read, the Stop is denied — with the missing path(s) named in the deny reason.

### Why a marker block, not prose-scanning

The prior version of this hook scanned assistant prose for verbs like "overflows", "is missing", "F1 —" combined with bare numeric tokens. It had a 100% production misfire rate — backend prose about wrapped APIs, missing validation, or audit findings numbered F1/F2 kept triggering it. The marker block is unambiguous: if you didn't emit `<image-finding>`, the hook won't fire; if you did, the hook knows exactly which image to check.

Findings phrased outside the marker — observations during conversation, descriptive references — don't trigger the hook. The marker is the contract: emit it when you're making a verifiable visual claim that needs evidence backing.

## Recovery options when the hook fires

1. **Read the cited images and re-state the findings**, anchored to specific
   regions you observed in the pixels. Distinguish "what the image shows"
   from "what I predicted" — if your preflight expected a problem and the
   image doesn't show it, name that explicitly ("predicted OTP overflow at
   375px; image shows comfortable fit, prediction wrong").
2. **Rewrite the claim as a question to the user** ("can you confirm whether
   the Copy all button appears below the screenshot crop?") — solicitation
   doesn't trigger.
3. **Mark the claim as an explicit expectation** rather than an observation
   ("the spec says X should be visible — please confirm in the actual
   render") — this is the visual-domain analogue of the runtime-state
   `ASSUMPTION_MARKERS` guard.

## Anchoring discipline (when several images need claims)

When walking a multi-image set:

- Read **each** image, in the same turn, before any findings.
- Emit one `<image-finding>` block per image. The hook checks each block's declared path against the turn's Read calls.
- If your preflight prediction matches what you see in an image, say so with an anchor inside the block:
  ```
  <image-finding image="/abs/path/60.png">
  F2 confirmed: rightmost cell visibly clipped at the card's right border.
  </image-finding>
  ```
  If it doesn't match, say so with an anchor:
  ```
  <image-finding image="/abs/path/60.png">
  F2 predicted; cells fit inside the card with whitespace to the right — prediction wrong.
  </image-finding>
  ```

## Bypass

Session-scoped: `CERES_SKIP_IMAGE_CLAIMS_HOOK=1` in the assistant's
environment. Use only when claims are about widely-known reference images
(brand logos, framework screenshots, public docs) where a Read provides
no additional ground truth.

## Linked memory

- `[[feedback_trust_bash_output_not_narration]]` — sibling at the
  shell-output surface (don't narrate what `find` returned without
  re-reading the data).
- `[[feedback_research_before_confident_claims]]` — sibling at the
  technical-claim surface (don't make confident framework claims without
  research).
- `[[feedback_proofread_repeated_block_patterns]]` — adjacent
  copy-paste-then-skim-the-result failure mode.

## Why this is a hook, not just a memory entry

Memory alone has the same failure mode this incident demonstrated: I would
have read the new memory line, agreed in principle, and then anchored on
the preflight script anyway next time a screenshot turn arrived. The
mechanical Stop-hook is what makes the discipline non-bypassable. The
skill text is for me to read; the hook is for me to be stopped by when
I forget.
