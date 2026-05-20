Invoke the `verify-image-claims` skill via the Skill tool. Then, for the user's IMMEDIATELY PREVIOUS message (which presumably included one or more screenshots), follow the skill's discipline before stating any finding:

1. **Enumerate every image the user attached**, by absolute path. The harness surfaces them as `[Image #N]` blocks with a `source:` path comment — extract each path. If you cannot find the path, ask the user where the images are.
2. **Read each image file**, one Read tool call per image, in the same turn. No findings before all images have been Read.
3. **State findings only after Read.** Anchor each finding to a specific image number AND a specific region you observed (e.g. "bottom-right OTP cell in #60", "top stepper labels in #66"). Do not aggregate ("F1–F5 all show the same problem") without having Read every cited image.
4. **Distinguish observation from prediction.** If your preflight prediction matches what you see, say so. If it doesn't, name that explicitly — "predicted X; image shows the opposite, prediction wrong."

If the previous user message contained no image, say so and stop — do not invent findings.

Required structure for the audited finding list:

1. **What I Read** — list every image path you opened with Read, with a one-line description of what's actually visible in each.
2. **Findings (only after step 1)** — numbered list F1..F_N. Each finding cites the image number AND the specific region. State whether the finding matches your preflight prediction or contradicts it.
3. **What's NOT findings** — list any preflight predictions that did NOT survive the Read. This is the load-bearing accountability line. The 2026-05-20 incident shipped nine F-claims that weren't really there; calling out "I predicted X, the image disproves it" prevents that from recurring.

Do not propose fixes in this command — the goal is accurate findings only. Fixes route through the regular planning flow once the findings are verified.
