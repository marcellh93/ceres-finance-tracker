# Phases 2 & 3 — Refining and stripping an existing surface

## Phase 2 — Refining (the dimension is named)

The user said "make this bolder", "tone it down", "the typography feels generic", "this needs motion", "fix the spacing", "make it more delightful", "the color story is flat", or "push this further than conventional limits".

Skip discovery entirely. Go straight to the matching Refine command:

| User says | Reach for |
|---|---|
| "feels weak / too quiet / not enough presence" | `/impeccable bolder` |
| "too loud / shouting / busy" | `/impeccable quieter` |
| "typography is generic / inconsistent / off" | `/impeccable typeset` |
| "color is flat / monochrome / needs life" | `/impeccable colorize` |
| "needs motion / feels static / transitions are abrupt" | `/impeccable animate` |
| "spacing / alignment / rhythm is off" | `/impeccable layout` |
| "functional but forgettable" | `/impeccable delight` |
| "go beyond conventional / make it technically extraordinary" | `/impeccable overdrive` (beta) |

If multiple dimensions need work, use `/impeccable polish` (design-system alignment, 5-dimension scoring with P0–P3) to identify what to refine first, then run the matching Refine command per dimension. Don't refine two dimensions in one pass — the diff becomes unreviewable.

After any Refine command: re-run `/impeccable critique` on the target before claiming done.

## Phase 3 — Stripping / clarifying (the work is "too much" or "confusing")

The user said "this is overwhelming", "strip this down", "I don't understand what this is asking me to do", "the copy is muddled", "this needs to work on mobile / on a 4K monitor / on an embedded screen".

| User says | Reach for |
|---|---|
| "strip / simplify / less / cleaner" | `/impeccable distill` |
| "the copy is unclear / users don't get what this does" | `/impeccable clarify` |
| "doesn't work on mobile / breaks at narrow widths / needs to scale up" | `/impeccable adapt` |

Then critique. Then verify on the device class that triggered the request.
