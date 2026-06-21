# Product

> Strategic design context for Project Ceres, consumed by the `impeccable` skill (via the `frontend-orchestrator`).
> The visual contract lives in [`docs/design-system.md`](design-system.md) — that file is this project's `DESIGN.md`.
> This file answers who / what / why; `design-system.md` answers how it looks.

## Register

product

## Users

**Primary — individuals tracking personal finances.** People who currently live in a spreadsheet or notes app and want a clearer picture of their money without learning accounting. They want to know their net worth, where money goes each month, and whether they're on budget. Their mental model is "accounts I hold" and "things I spend on", not debits and credits. Context is mixed: quick entry on the go (mobile, 375px is a real target) and periodic sit-down review sessions.

**Secondary — Spanish freelancers (autónomos).** Everything the primary user needs, plus tax-facing work: IVA/VAT tracking, quarterly tax summaries (Modelo 130 / 303), client tracking, and invoice references on transactions. These features carry direct financial value and are gated behind the premium tier.

**Phase-1 dogfooder — the developer.** Phase 1 was built to be used daily by its author first, so real usage surfaces data-model and UX issues before the app opens to others. This is intentional, not just a constraint; the product earns trust by being lived in.

## Product Purpose

Project Ceres is a personal finance tracker that replaces spreadsheets. It tracks accounts (assets and liabilities), transactions (income and expense, classified by category), transfers, recurring-transaction reminders, goal budgets, and financial reports. Net worth and account balances are always derived, never stored. Bookkeeping is single-entry; reports filter by currency rather than converting.

It exists because spreadsheets are fragile, manual, and don't model recurring money or budgets well, and because off-the-shelf finance apps either drown individuals in accounting jargon or don't serve the Spanish autónomo's tax obligations. Success looks like a user trusting the numbers enough to abandon their spreadsheet, and an autónomo producing a quarterly tax summary without reaching for a second tool.

The app is moving from a local single-user MVP (Phases 1–2, complete) to a hosted, authenticated, multi-user beta (Phase 3, current).

## Brand Personality

**Calm, trustworthy, precise.**

- **Voice:** plain language. Name the thing before the jargon; one new term at a time, defined where it appears. Never make the user decode accounting to read their own money.
- **Tone:** quiet confidence. The numbers are the hero; the interface gets out of the way. No hype, no urgency theatre, no celebration of routine actions.
- **Emotional goal:** low-anxiety money handling. Looking at your finances should feel like checking a reliable instrument, not bracing for bad news.

## Anti-references

This should NOT look like:

- **Generic fintech SaaS.** No navy-plus-gradient-blue palette, no hero-metric template (giant number / tiny label / gradient accent), no walls of identical icon-heading-text cards. The brand teal is deliberately not the default SaaS blue, and not a literal money-green.
- **Cluttered legacy accounting software** (QuickBooks / Sage lineage). No debit/credit jargon surfaced to users, no overwhelming toolbars, no accountant-first density. The single-entry model is a feature: keep accounting complexity out of the user's way.
- **Loud crypto / neon dashboards.** No neon-on-black, no aggressive gradients, no hype-driven data viz. Money handling reads as steady, not as a trading floor.
- **Playful / gamified budgeting apps.** No cartoon mascots, confetti, emoji-heavy copy, or streaks-and-badges gamification. Respect the user's money and attention.

## Design Principles

1. **Keep accounting complexity out of the way.** Model money the way individuals think about it (accounts, categories, net worth) — never surface double-entry, debits/credits, or derived-column mechanics. Single-entry and derived balances are deliberate simplicity, not a shortcut.
2. **Quiet confidence over flash.** The data is the product. Hierarchy, spacing, and restraint do the work; decoration earns its place or is cut. If a screen feels loud, it's wrong.
3. **Trust through legibility.** Numbers are tabular and unambiguous; color is never the only signal (income/expense and status always carry a text label); state (cleared, pending, needs review) is always visible. The user should never have to guess what they're looking at.
4. **Accessibility is a baseline, not a feature.** Semantic HTML, keyboard navigation, and WCAG 2.1 AA contrast (EN 301 549 for the EU Accessibility Act) apply from the first component, not as a late audit. Layout survives text-spacing and reduced-motion overrides.
5. **One way to do each thing.** `docs/design-system.md` is the single contract. Reuse existing tokens and recipes; a missing token is added there first, then consumed. No parallel patterns, no one-off values — consistency is how a small app feels trustworthy.

## Accessibility & Inclusion

- **Target:** WCAG 2.1 Level AA, via EN 301 549 v3.2.1 (EU Accessibility Act). A third-party audit is planned before public launch.
- **Contrast:** 4.5:1 for body text, 3:1 for large text and UI components; every token's ratio is verifiable live on the internal `/design-system.html#/colors` page. Amber (`--warning`) is AA-Large only — not for body copy.
- **Color independence:** color is never the sole differentiator. Income/expense and all status badges pair color with a text label (WCAG 1.4.1).
- **Motion:** all motion runs through the three motion-duration tokens and honors reduced-motion preferences; never animate layout properties.
- **Resilience:** interactive rows/cards use `min-h-*` not fixed `h-*` so layouts survive WCAG 1.4.12 text-spacing overrides.
- **Responsive:** mobile (375px) is a first-class verification target, not an afterthought.
