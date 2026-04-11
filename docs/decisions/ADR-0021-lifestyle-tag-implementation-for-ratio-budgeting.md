# ADR-0021 — LifestyleTag Implementation for Ratio-Based Budgeting (50/30/20)

**Status:** Accepted (Phase 1 schema, Phase 2 feature)

**Context:**

Ratio-based budgeting frameworks like 50/30/20 (50% needs, 30% wants, 20% savings) require each expense category to be classified as a "need" or a "want." This is inherently subjective — the app cannot classify categories automatically.

Three design questions had to be resolved:

1. **How do users assign tags?** Options ranged from a dedicated onboarding/setup page to a per-category field to an opt-in bulk tagging flow.
2. **What happens to categories the user hasn't tagged?** Options: exclude them silently (risks making the report look misleadingly "complete"), show a warning, or make untagged a visible bucket.
3. **When is the column added?** Adding it in Phase 2 (when the feature ships) would require a schema migration against an already-populated database.

**Decision:**

`LifestyleTag` is a nullable `varchar` column added to `Category` in Phase 1. Its values are `"Needs"`, `"Wants"`, or `null` (untagged). The 50/30/20 report is built in Phase 2 using this column.

**Tag assignment approach:**
- **Starter categories ship with suggested default tags** — system-seeded categories have reasonable defaults (e.g. Rent → Needs, Dining Out → Wants). These are defaults, not enforced; users can change them.
- **Creation-time prompt** — when a user creates a custom Expense category, the form includes a "Needs / Wants / Skip for now" prompt. Income and system categories do not show the prompt.
- No dedicated tagging setup page — users encounter the prompt organically at the point of creating a category.

**Untagged handling:**
The Financial Health report shows four sections: Needs %, Wants %, Savings %, and Untagged %. Untagged spend is never silently excluded. This means the report is immediately useful even if the user has not tagged everything — an untagged bucket that is large is itself a signal to the user.

**Consequences:**

- Schema is ready in Phase 1 so Phase 2 requires no migration — only application code
- Users with existing categories from Phase 1 will have system-seeded categories pre-tagged; custom categories will be untagged until the user sets them (prompted at creation, changeable any time)
- The visible untagged bucket prevents a false sense of completeness in the report — a user who has not tagged their categories sees a large "Untagged" slice rather than a report that appears to account for 100% of spending
- No separate tagging setup page reduces implementation scope and avoids a step users may abandon mid-flow
- `LifestyleTag` is only meaningful for Expense categories — Income and system categories are excluded from all ratio calculations regardless of their tag value
