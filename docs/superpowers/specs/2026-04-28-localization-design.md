# Localization Design — Project Ceres

**Date:** 2026-04-28
**Phase:** 3 (Hosted Beta)
**Status:** Approved

---

## Scope

Full UI localization in English (`en`) and Spanish (`es`). Covers:

- All static UI strings (navigation, buttons, headings, form labels, error messages, empty states)
- System-seeded category names (looked up by key at render time, not re-stored per language)
- Transactional emails (password reset, new session alert, weekly digest)
- Generated report content (column headers, section titles, labels)

User-entered strings (transaction descriptions, account names, custom category names) are stored as-is and never translated.

---

## Translation Layer Architecture

Two layers, each idiomatic to its runtime.

### React SPA — `react-i18next` + JSON

- **Files:** `ProjectCeres.Client/src/locales/en.json`, `es.json`
- **Initialization:** `i18next` configured in `main.tsx`
- **Language detection order:** `localStorage` → `lang` cookie → `navigator.language` → fallback `en`
- **All static strings:** `useTranslation` hook (`t('key')`) or `Trans` component for interpolated strings
- **System category names:** keyed by stable slug (e.g. `category.opening_balance`, `category.salary`). The `Name` column in the DB stores the English base value as a fallback only.
- **Language change:** `i18n.changeLanguage(code)` — swaps all strings in place instantly, no reload. Also updates `<html lang>`, writes to `localStorage` and the `lang` cookie, and PATCHes `Settings.Language` if the user is authenticated.

### Server — `.resx` files

- **Files:**
  - `Resources/Emails.en.resx`, `Resources/Emails.es.resx`
  - `Resources/Reports.en.resx`, `Resources/Reports.es.resx`
- **Injection:** `IStringLocalizer<Emails>` and `IStringLocalizer<Reports>` in the relevant services
- **Culture resolution:** read `Settings.Language` for the authenticated user before rendering any email or report

---

## Language Resolution

### Unauthenticated requests

```
Request arrives
  → lang cookie present? → use it
  → else: parse Accept-Language header → best match (en | es) → write lang cookie
  → fallback: en
```

### Authenticated requests

```
User logs in
  → read Settings.Language
  → call i18n.changeLanguage(code)
  → overwrite lang cookie with Settings.Language value
```

The user's stored preference always wins over any cookie or browser signal after login.

---

## `lang` Cookie

| Property | Value |
|----------|-------|
| Name | `lang` |
| Values | `en`, `es` |
| `SameSite` | `Lax` |
| `HttpOnly` | false — must be readable by JS for pre-auth detection |
| Expiry | Session (no `Max-Age`) for unauthenticated users; overwritten on login |

---

## Data Model

Two new columns on `Settings` (land in the Phase 3 per-user preferences migration):

| Column | Type | Constraints | Notes |
|--------|------|-------------|-------|
| `Language` | varchar(5) | NOT NULL DEFAULT `'en'` | BCP 47 tag. Supported: `en`, `es`. Validated at service layer — reject unsupported codes. |
| `Country` | varchar(2) | nullable | ISO 3166-1 alpha-2. `ES`, `US`, `GB`, `CO`, `AR`, `VE`, or any valid code entered via "Other". Nullable — users who skip or select Other without specifying are stored as null. |

**No `Country` lookup table.** Country is a display/legal preference label, not a relational entity. A `varchar(2)` column is sufficient until country drives data logic (tax rules, jurisdiction features). Migration to a FK at that point is straightforward.

**No currency cascade on country change.** All five preference fields are fully independent. Users in Latin America commonly operate in USD regardless of their country — auto-changing currency on country selection would actively work against them.

---

## Auth Screens — Language Toggle

Every auth screen (login, register, password reset, TOTP) includes a language toggle at the **bottom of the auth card**.

- Renders as a small globe icon + current language label + chevron
- Clicking opens a minimal dropdown: `English` / `Español`
- Selecting a language calls `i18n.changeLanguage(code)` and writes the `lang` cookie immediately
- The auth card re-renders in the new language instantly — no page reload

---

## Onboarding Wizard — Preferences Step

The Preferences step is the first step of the onboarding wizard (before account setup). It groups five locale fields, all pre-filled from the detected language/region, all independently overridable.

**Fields (in display order):**

| Field | Source of default | Options |
|-------|-------------------|---------|
| Language | `lang` cookie | English, Español |
| Country | IP geolocation or `Accept-Language` region hint | ES, US, GB, CO, AR, VE, Other |
| Default currency | Country-to-currency hint from the supported countries table (e.g. ES → EUR, US → USD). Applied only as the initial seed on first visit — not a cascade. User selection is always independent. | All supported currencies |
| Number format | Locale detection | `1,234.56` (period decimal), `1.234,56` (comma decimal) |
| Date format | Locale detection | `DD/MM/YYYY`, `MM/DD/YYYY`, `YYYY-MM-DD` |

**Live preview row** below the fields shows a formatted example: e.g. `€1.234,56 · 28/04/2026`, updating as the user changes fields.

**UX pattern:** smart defaults — fields arrive pre-filled, user only corrects what's wrong. One "Continue →" button advances to the next step.

**On save:** all five values write to `Settings`. Language change also calls `i18n.changeLanguage()` so the rest of the wizard renders immediately in the chosen language.

---

## Authenticated App Shell — Language Switcher

- Location: **user avatar dropdown** (top-right of the top bar)
- Renders as a `EN / ES` pill toggle inline within the dropdown
- Selecting a language: calls `i18n.changeLanguage(code)`, updates `<html lang>`, writes `lang` cookie, PATCHes `Settings.Language`
- Instant in-place string swap — no reload, no confirmation dialog
- The same setting is also accessible on the Settings page (Preferences section) for discoverability

---

## Supported Languages at Launch

| Code | Name | Notes |
|------|------|-------|
| `en` | English | Default fallback |
| `es` | Spanish | Primary secondary language |

Additional languages are added by dropping a new JSON file (`fr.json`, etc.) and a new `.resx` pair, plus registering the code in the supported-languages list. No schema change required.

---

## Supported Countries (Short List)

| Code | Country | Default currency |
|------|---------|-----------------|
| `ES` | Spain | EUR |
| `US` | United States | USD |
| `GB` | United Kingdom | GBP |
| `CO` | Colombia | COP |
| `AR` | Argentina | ARS |
| `VE` | Venezuela | VED |
| — | Other | (user keeps whatever is pre-filled) |

The "default currency" column is informational only — it is used as the initial seed when the user first arrives at the Preferences step, not as a cascade triggered by country selection.

---

## Translation Key Structure

Keys use dot-notation namespaced by feature area. Examples:

```json
{
  "nav.dashboard": "Dashboard",
  "nav.transactions": "Transactions",
  "account.balance": "Balance",
  "transaction.form.amount": "Amount",
  "transaction.form.amount_error": "Amount must be greater than zero",
  "category.opening_balance": "Opening Balance",
  "category.salary": "Salary",
  "report.net_worth.title": "Net Worth Statement",
  "onboarding.prefs.title": "Your preferences",
  "onboarding.prefs.preview": "Preview"
}
```

Spanish file mirrors the same keys with translated values.

**Pluralization:** use `i18next` built-in plural suffixes (`_one`, `_other`) for any count-dependent strings.

**Interpolation:** use named parameters — `t('transaction.count', { count: 42 })` — never string concatenation.

---

## Accessibility

- `<html lang>` attribute is updated whenever the active language changes (covers screen reader language announcement)
- Auth screen toggle includes `aria-label="Change language"` on the trigger button
- Avatar dropdown language toggle uses `aria-pressed` or `aria-checked` on the active option
- All translated strings must meet the same WCAG 2.1 AA requirements as English strings (contrast, label association, error text)

---

## Out of Scope

- Right-to-left (RTL) layout support
- More than two languages at launch
- Machine translation — all strings are human-translated
- Translating user-entered data (transaction descriptions, custom category names, account names)
- Currency conversion (remains explicitly out of scope per existing design decisions)
- Locale-specific number/date formatting beyond the existing `NumberFormat` and `DateFormat` settings (those are already handled by the existing `Settings` columns)
