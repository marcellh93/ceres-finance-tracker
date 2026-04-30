# Movements CRUD — Plan 2: SPA Movements form + routes

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Wire the SPA Movements page against the API surface Plan 1 just shipped. Add a routed Create page (`/app/movements/new`) and Edit page (`/app/movements/:id/edit`) with a shared `MovementForm` component. Add the type filter dropdown to `MovementsFilterBar`. Add a row-level `⋯` menu (Edit / Delete) on the Movements table with `AlertDialog` confirmation. Suppress the global TopBar quick-add button + keyboard shortcut while on `/app/movements*`. Add view-transition CSS hooks (no animation behavior — just naming for a future PR).

**Architecture:** `/app/movements` becomes a routed parent (`MovementsLayout`) that renders the existing list as its main content AND an `<Outlet />` slot. The Create and Edit pages render into the outlet, sitting *over* the list rather than replacing it — so the list never unmounts and back-navigation keeps scroll/filter state. The shared `MovementForm` component takes `mode: 'create' | 'edit'` and `type: MovementType` and renders the right field set for Transaction / Transfer / LiabilityPayment. PUT/POST submit handlers map the project's `error.code = "VALIDATION_ERROR"` body shape (the dual shape documented in `docs/api-contract.md`) to per-field error messages.

**Tech Stack:** React 19, React Router v7, TypeScript, shadcn/ui (`base-nova`, base-ui primitives), Tailwind v4, Vitest + React Testing Library. The SPA already has `react-router-dom` wired (the existing `Movements` page uses `useSearchParams`).

**Spec:** `docs/superpowers/specs/2026-04-30-movements-crud.md`. Read §6 (UI surface), §7 (data flow), and §11 (out of scope) before starting.

**Scope boundary:**
- **In scope:** Routing under `/app/movements*` (parent + Create + Edit), `MovementForm` shared component, type filter dropdown, row `⋯` menu (Edit/Delete + confirm), TopBar quick-add suppression on movements routes, view-transition CSS hooks, all corresponding Vitest tests.
- **Out of scope (Plan 3):** Attachment dropzone client + Create→Edit pending-file hand-off; bulk-cleared and CSV export buttons in the Movements page header; Razor 302 redirects and view deletions; doc sync per spec §10.

After Plan 2 ships, users can fully Create / Edit / Delete movements through the SPA. Attachments still don't work end-to-end (the form will mention them but won't yet upload — Plan 3 fills that gap). Razor still serves the legacy paths in parallel.

---

## File Structure

**Created (React client):**
- `ProjectCeres.Client/src/app/features/movements/MovementsLayout.tsx` — routed parent that renders the existing list + `<Outlet />`. Replaces `pages/Movements.tsx` as the route component.
- `ProjectCeres.Client/src/app/features/movements/MovementsLayout.test.tsx`
- `ProjectCeres.Client/src/app/features/movements/MovementForm.tsx` — the shared form component for Create + Edit, all three movement types.
- `ProjectCeres.Client/src/app/features/movements/MovementForm.test.tsx`
- `ProjectCeres.Client/src/app/features/movements/MovementCreate.tsx` — `/app/movements/new` page. Type-picker landing + `?type=` deep link → renders `MovementForm` in create mode.
- `ProjectCeres.Client/src/app/features/movements/MovementCreate.test.tsx`
- `ProjectCeres.Client/src/app/features/movements/MovementEdit.tsx` — `/app/movements/:id/edit` page. Loads via discriminator + typed GET, renders `MovementForm` in edit mode, includes danger-zone Delete.
- `ProjectCeres.Client/src/app/features/movements/MovementEdit.test.tsx`
- `ProjectCeres.Client/src/app/features/movements/MovementRowMenu.tsx` — `⋯` dropdown with Edit / Delete on each row.
- `ProjectCeres.Client/src/app/features/movements/MovementRowMenu.test.tsx`
- `ProjectCeres.Client/src/app/features/movements/movement-validation.ts` — pure helper that parses the project's 422 error body into `Record<string, string>` per-field errors. Reusable by `MovementForm` and any future PUT/POST surfaces.
- `ProjectCeres.Client/src/app/features/movements/movement-validation.test.ts`
- `ProjectCeres.Client/src/components/ui/alert-dialog.tsx` — shadcn primitive added via `pnpm dlx shadcn add alert-dialog`. Required by Delete confirmations.

**Modified:**
- `ProjectCeres.Client/src/app/features/movements/movements-api.ts` — add typed endpoint URL builders + DTOs for the new endpoints (`GET /api/transactions/:id`, `PUT/DELETE` equivalents, `GET /api/movements/:id` discriminator). The Plan 1 endpoints exist server-side; the client just needs URL constants and DTO types.
- `ProjectCeres.Client/src/app/features/movements/MovementsFilterBar.tsx` — add the type-filter `Select` next to the existing Account combobox.
- `ProjectCeres.Client/src/app/features/movements/MovementsFilterBar.test.tsx` — extend with type-filter assertions.
- `ProjectCeres.Client/src/app/features/movements/MovementsTable.tsx` — add a trailing `⋯` cell that mounts `MovementRowMenu` per row. Drop the `QuickAddModal` import + state (it moves out of the page header in Task 11).
- `ProjectCeres.Client/src/app/features/movements/MovementsTable.test.tsx` — add row-menu rendering test.
- `ProjectCeres.Client/src/app/App.tsx` — register the new nested routes under `/movements`. Replace `<Route path="movements" element={<Movements />} />` with the parent + children structure.
- `ProjectCeres.Client/src/app/App.test.tsx` — add route smoke tests for the three new paths.
- `ProjectCeres.Client/src/app/layout/TopBar.tsx` — add pathname check; hide `+` button + skip the quick-add keyboard shortcut while pathname matches `^/app/movements(/|$)`. (Note: routes don't currently include `/app` prefix in `useLocation().pathname` because `BrowserRouter` is mounted with `basename="/app"`; pathname inside the router is `/movements`. Verify before writing the regex.)
- `ProjectCeres.Client/src/app/layout/TopBar.test.tsx` — add suppression assertions.
- `ProjectCeres.Client/src/app/pages/Movements.tsx` — DELETE this file. The route now points at `MovementsLayout` instead. Audit imports in App.tsx and remove the `Movements` import.
- `ProjectCeres.Client/src/index.css` (or wherever the CSS tokens live) — add view-transition-name CSS for `.movement-row-{id}` and `.movement-form` (one block, ~10 lines of CSS).

**Deleted:**
- `ProjectCeres.Client/src/app/pages/Movements.tsx` — replaced by `features/movements/MovementsLayout.tsx`.

---

## Conventions used throughout this plan

- **Imports:** match the existing client style — alphabetized within groups, `@/` aliases for shadcn primitives, relative paths for sibling modules.
- **Tests:** Vitest + React Testing Library. Wrap components that use `useSearchParams` / `useNavigate` / `useParams` in `<MemoryRouter>` per the existing pattern in `MovementsFilterBar.test.tsx`. Mock `fetch` (or use `mockFetch` like the existing tests).
- **Toasts:** use `sonner` via `import { toast } from 'sonner'` — already wired.
- **Each task ends with one commit.** Don't amend.
- **API contract:** per `docs/api-contract.md`, validation errors come back as `{ error: { code: "VALIDATION_ERROR", message, details: [{field, message}] | [] } }`. The existing `QuickAddModal` parses ASP.NET ProblemDetails (PascalCase keys); that parser is **wrong** for the typed endpoints we're consuming and we will not reuse it. The new `movement-validation.ts` helper is the canonical parser.

---

## Task 1: Add the `alert-dialog` shadcn primitive

**Files:**
- Create (generated): `ProjectCeres.Client/src/components/ui/alert-dialog.tsx`

- [ ] **Step 1: Run the shadcn CLI**

```bash
pnpm --dir ProjectCeres.Client dlx shadcn add alert-dialog
```

If the CLI prompts about overwriting any existing file, choose **No**. We're only adding `alert-dialog.tsx`.

- [ ] **Step 2: Verify the file was created**

```bash
ls ProjectCeres.Client/src/components/ui/alert-dialog.tsx
```

Expected: prints the path.

- [ ] **Step 3: Confirm the existing build still passes**

```bash
pnpm --dir ProjectCeres.Client build
```

Expected: PASS.

- [ ] **Step 4: Commit**

```bash
git add ProjectCeres.Client/src/components/ui/alert-dialog.tsx \
        ProjectCeres.Client/package.json \
        ProjectCeres.Client/pnpm-lock.yaml
git commit -m "feat(design-system): add shadcn alert-dialog primitive"
```

---

## Task 2: Extend `movements-api.ts` with typed endpoint URLs and DTOs

**Files:**
- Modify: `ProjectCeres.Client/src/app/features/movements/movements-api.ts`

- [ ] **Step 1: Append URL builders and DTOs**

Open `movements-api.ts` and append at the end of the file:

```ts
// ---------- Typed CRUD endpoints (Plan 1 server work) ----------

export const TRANSACTION_BY_ID_URL      = (id: string) => `/api/transactions/${id}`;
export const TRANSFER_BY_ID_URL         = (id: string) => `/api/transfers/${id}`;
export const LIABILITY_PAYMENT_BY_ID_URL = (id: string) => `/api/liability-payments/${id}`;

export const MOVEMENT_TYPE_URL = (id: string) => `/api/movements/${id}`;

// ---------- Edit DTOs (responses for GET /:id) ----------

export type TransactionEditDto = {
  id: string;
  date: string;
  amount: number;
  accountId: string;
  categoryId: string;
  description: string | null;
  isCleared: boolean;
  attachments: AttachmentDto[];
};

export type TransferEditDto = {
  id: string;
  date: string;
  amount: number;
  sourceAccountId: string;
  destAccountId: string;
  description: string | null;
  isCleared: boolean;
  attachments: AttachmentDto[];
};

export type LiabilityPaymentEditDto = {
  id: string;
  date: string;
  amount: number;
  assetAccountId: string;
  liabilityAccountId: string;
  description: string | null;
  isCleared: boolean;
};

export type AttachmentDto = {
  id: string;
  fileName: string;
  sizeBytes: number;
  contentType: string;
  uploadedAt: string;
};

export type MovementTypeDto = {
  id: string;
  movementType: MovementType;
};

// ---------- Update request bodies (PUT /:id) ----------

export type UpdateTransactionRequest = {
  date: string;
  amount: number;
  accountId: string;
  categoryId: string;
  description: string | null;
  isCleared: boolean;
  budgetId: string | null;
  needsReview: boolean;
};

export type UpdateTransferRequest = {
  date: string;
  amount: number;
  sourceAccountId: string;
  destAccountId: string;
  description: string | null;
  isCleared: boolean;
};

export type UpdateLiabilityPaymentRequest = {
  date: string;
  amount: number;
  assetAccountId: string;
  liabilityAccountId: string;
  description: string | null;
  isCleared: boolean;
};

// ---------- Project's standard error envelope ----------
// (See docs/api-contract.md — supersedes the ProblemDetails-shaped parser
// used by QuickAddModal for the legacy endpoints.)

export type ApiErrorEnvelope = {
  error: {
    code: string;
    message: string;
    details: Array<{ field?: string; message: string }>;
  };
};
```

- [ ] **Step 2: Confirm build still passes**

```bash
pnpm --dir ProjectCeres.Client build
```

Expected: PASS.

- [ ] **Step 3: Commit**

```bash
git add ProjectCeres.Client/src/app/features/movements/movements-api.ts
git commit -m "feat(spa): add typed Movements CRUD URL builders and DTOs"
```

---

## Task 3: Add `movement-validation.ts` helper (parses the 422 envelope)

**Files:**
- Create: `ProjectCeres.Client/src/app/features/movements/movement-validation.ts`
- Create: `ProjectCeres.Client/src/app/features/movements/movement-validation.test.ts`

This helper parses the project's 422 envelope into `Record<string, string>` for per-field errors. The dual shape (ModelState `details: [{field, message}]` vs. business-rule `details: []` with message in `error.message`) is documented in `docs/api-contract.md`. The helper handles both:
- ModelState 422 → field-keyed errors map.
- Business-rule 422 → empty field map + a top-level `_form` key with `error.message`.

Consumers can render `_form` as a banner above the form and field errors inline.

- [ ] **Step 1: Write the failing test**

Create `movement-validation.test.ts`:

```ts
import { describe, expect, it } from 'vitest';
import { parseValidationErrors } from './movement-validation';

describe('parseValidationErrors', () => {
  it('returns field-keyed errors for ModelState shape', () => {
    const envelope = {
      error: {
        code: 'VALIDATION_ERROR',
        message: 'One or more fields are invalid.',
        details: [
          { field: 'Amount', message: 'Amount must be greater than zero.' },
          { field: 'AccountId', message: 'Please select an account.' },
        ],
      },
    };
    const result = parseValidationErrors(envelope);
    expect(result.amount).toBe('Amount must be greater than zero.');
    expect(result.accountId).toBe('Please select an account.');
    expect(result._form).toBeUndefined();
  });

  it('returns _form key for business-rule shape (empty details)', () => {
    const envelope = {
      error: {
        code: 'VALIDATION_ERROR',
        message: 'Source and destination accounts must be different.',
        details: [],
      },
    };
    const result = parseValidationErrors(envelope);
    expect(result._form).toBe('Source and destination accounts must be different.');
  });

  it('lowercases the first character of field names (PascalCase → camelCase)', () => {
    const envelope = {
      error: {
        code: 'VALIDATION_ERROR',
        message: '...',
        details: [{ field: 'SourceAccountId', message: 'Required.' }],
      },
    };
    const result = parseValidationErrors(envelope);
    expect(result.sourceAccountId).toBe('Required.');
  });

  it('returns empty object for non-error responses', () => {
    expect(parseValidationErrors(null)).toEqual({});
    expect(parseValidationErrors({})).toEqual({});
    expect(parseValidationErrors({ error: null })).toEqual({});
  });
});
```

- [ ] **Step 2: Run — expect FAIL**

```bash
pnpm --dir ProjectCeres.Client test movement-validation
```

- [ ] **Step 3: Implement**

Create `movement-validation.ts`:

```ts
import type { ApiErrorEnvelope } from './movements-api';

/**
 * Parse the project's 422 error envelope into per-field error messages.
 * Per docs/api-contract.md, two shapes share the VALIDATION_ERROR code:
 *  - ModelState: details[] = [{ field: "Amount", message: "..." }]
 *  - Business-rule: details[] = [] (message lives in error.message)
 *
 * Returns a flat record where keys are camelCased field names. The special
 * key "_form" carries the top-level message when there are no per-field details.
 */
export function parseValidationErrors(body: unknown): Record<string, string> {
  const result: Record<string, string> = {};
  if (!body || typeof body !== 'object') return result;

  const envelope = body as Partial<ApiErrorEnvelope>;
  const error = envelope.error;
  if (!error || typeof error !== 'object') return result;

  if (Array.isArray(error.details) && error.details.length > 0) {
    for (const item of error.details) {
      if (!item.field || !item.message) continue;
      const camelKey = item.field.charAt(0).toLowerCase() + item.field.slice(1);
      result[camelKey] = item.message;
    }
    return result;
  }

  if (error.message) {
    result._form = error.message;
  }
  return result;
}
```

- [ ] **Step 4: Run — expect PASS**

```bash
pnpm --dir ProjectCeres.Client test movement-validation
```

- [ ] **Step 5: Commit**

```bash
git add ProjectCeres.Client/src/app/features/movements/movement-validation.ts \
        ProjectCeres.Client/src/app/features/movements/movement-validation.test.ts
git commit -m "feat(spa): add 422 error-envelope parser for Movements CRUD"
```

---

## Task 4: Add the `MovementsLayout` parent route component

**Files:**
- Create: `ProjectCeres.Client/src/app/features/movements/MovementsLayout.tsx`
- Create: `ProjectCeres.Client/src/app/features/movements/MovementsLayout.test.tsx`

This component takes over the responsibility currently in `pages/Movements.tsx`: render the list + filters. It additionally renders an `<Outlet />` that the Create and Edit child routes will mount into. The list stays mounted underneath the outlet, so Create/Edit feel like a slide-over — but for now there's no animation; the form just renders below the list visually.

Layout behavior: the list is the *page*. When a child route is active, the form mounts in a `<dialog>`-like overlay region using a CSS Grid that places the outlet over the list. Concretely: a single CSS Grid with two row templates — when a child is active, the outlet panel fills the viewport; when not, only the list is visible. Implementation detail: use a state-derived class based on `useMatch('/movements/new')` and `useMatch('/movements/:id/edit')` to toggle the overlay class.

Simpler initial implementation (Task 4): just render `<Outlet />` *below* the list. The overlay layout can come later as a polish PR. The locked spec says the list "stays mounted" — placing the outlet below it satisfies that without complicating Plan 2.

- [ ] **Step 1: Write the failing test**

Create `MovementsLayout.test.tsx`:

```tsx
import { render, screen } from '@testing-library/react';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { MovementsLayout } from './MovementsLayout';

const mockFetch = vi.fn();

beforeEach(() => {
  global.fetch = mockFetch as unknown as typeof fetch;
  mockFetch.mockResolvedValue({
    ok: true,
    json: async () => ({ items: [], totalCount: 0, page: 1, pageSize: 50 }),
  });
});

afterEach(() => { vi.resetAllMocks(); });

describe('MovementsLayout', () => {
  it('renders the Movements heading and the list area', () => {
    render(
      <MemoryRouter initialEntries={['/movements']}>
        <Routes>
          <Route path="/movements" element={<MovementsLayout />}>
            <Route index element={null} />
          </Route>
        </Routes>
      </MemoryRouter>,
    );
    expect(screen.getByRole('heading', { name: /movements/i })).toBeInTheDocument();
  });

  it('renders the outlet content for child routes', () => {
    render(
      <MemoryRouter initialEntries={['/movements/new']}>
        <Routes>
          <Route path="/movements" element={<MovementsLayout />}>
            <Route path="new" element={<div data-testid="child-route">CHILD</div>} />
          </Route>
        </Routes>
      </MemoryRouter>,
    );
    expect(screen.getByTestId('child-route')).toHaveTextContent('CHILD');
    // List heading still present (parent stays mounted).
    expect(screen.getByRole('heading', { name: /movements/i })).toBeInTheDocument();
  });
});
```

- [ ] **Step 2: Run — expect FAIL**

```bash
pnpm --dir ProjectCeres.Client test MovementsLayout
```

- [ ] **Step 3: Implement**

Create `MovementsLayout.tsx`. Mirror the structure of the deleted `pages/Movements.tsx` (you'll delete that file in Task 5) but add the outlet. The existing page handles: heading focus, `useApi` loading state, error state, empty state, table + pagination. Replicate that, then append `<Outlet />` at the end.

```tsx
import { useEffect, useRef } from 'react';
import { Outlet, useSearchParams } from 'react-router-dom';
import { Skeleton } from '@/components/ui/skeleton';
import { CardError } from '../../components/CardError';
import { MovementsFilterBar } from './MovementsFilterBar';
import { MovementsPagination } from './MovementsPagination';
import { MovementsTable } from './MovementsTable';
import { MOVEMENTS_URL, type MovementsPageDto } from './movements-api';
import { useApi } from '../../lib/use-api';

function buildUrl(params: URLSearchParams): string {
  const search = params.toString();
  return search ? `${MOVEMENTS_URL}?${search}` : MOVEMENTS_URL;
}

export function MovementsLayout() {
  const headingRef = useRef<HTMLHeadingElement>(null);
  useEffect(() => { headingRef.current?.focus(); }, []);

  const [params] = useSearchParams();
  const url = buildUrl(params);
  const { data, error, loading, refetch } = useApi<MovementsPageDto>(url);

  return (
    <div className="space-y-6">
      <div className="flex items-center justify-between">
        <h1 ref={headingRef} tabIndex={-1} className="text-3xl font-semibold outline-none">
          Movements
        </h1>
        {/* + New button is added in Task 12 (route to /movements/new). */}
      </div>

      <MovementsFilterBar />

      {loading && <Skeleton className="h-[400px] w-full" />}
      {error && <CardError section="Movements" onRetry={refetch} />}
      {data && data.items.length === 0 && (
        <p className="text-sm text-muted-foreground">No movements found.</p>
      )}
      {data && data.items.length > 0 && (
        <>
          <MovementsTable items={data.items} />
          <MovementsPagination totalCount={data.totalCount} pageSize={data.pageSize} />
        </>
      )}

      <Outlet context={{ refetch }} />
    </div>
  );
}
```

The `Outlet`'s `context` exposes `refetch` so the Create and Edit pages can refresh the list after a save (`useOutletContext<{ refetch: () => void }>()`).

- [ ] **Step 4: Run — expect PASS**

- [ ] **Step 5: Commit**

```bash
git add ProjectCeres.Client/src/app/features/movements/MovementsLayout.tsx \
        ProjectCeres.Client/src/app/features/movements/MovementsLayout.test.tsx
git commit -m "feat(spa): add MovementsLayout parent route with Outlet"
```

---

## Task 5: Wire the new routes in `App.tsx` and delete the old `pages/Movements.tsx`

**Files:**
- Modify: `ProjectCeres.Client/src/app/App.tsx`
- Modify: `ProjectCeres.Client/src/app/App.test.tsx`
- Delete: `ProjectCeres.Client/src/app/pages/Movements.tsx`

- [ ] **Step 1: Update `App.tsx`**

Replace:

```tsx
<Route path="movements" element={<Movements />} />
```

with:

```tsx
<Route path="movements" element={<MovementsLayout />}>
  <Route path="new" element={<MovementCreate />} />
  <Route path=":id/edit" element={<MovementEdit />} />
</Route>
```

Add the new imports at the top:

```tsx
import { MovementsLayout } from './features/movements/MovementsLayout';
import { MovementCreate } from './features/movements/MovementCreate';
import { MovementEdit } from './features/movements/MovementEdit';
```

Remove the old import: `import { Movements } from './pages/Movements';`.

**Note:** `MovementCreate` and `MovementEdit` are placeholders for now — Task 5 just wires the routes; Tasks 7 and 8 implement the components. To keep the build green between tasks, **stub them temporarily**:

Create `ProjectCeres.Client/src/app/features/movements/MovementCreate.tsx`:

```tsx
export function MovementCreate() { return <div>MovementCreate placeholder</div>; }
```

Create `ProjectCeres.Client/src/app/features/movements/MovementEdit.tsx`:

```tsx
export function MovementEdit() { return <div>MovementEdit placeholder</div>; }
```

These stubs will be replaced in Tasks 7 and 8.

- [ ] **Step 2: Delete the old `pages/Movements.tsx`**

```bash
rm ProjectCeres.Client/src/app/pages/Movements.tsx
```

- [ ] **Step 3: Update `App.test.tsx` — add route smoke tests**

Add tests asserting that the three movement routes render without crashing. Append to the existing `App.test.tsx`:

```tsx
it('renders the Movements list at /movements', () => {
  render(<MemoryRouter initialEntries={['/movements']}><App /></MemoryRouter>);
  expect(screen.getByRole('heading', { name: /movements/i })).toBeInTheDocument();
});

it('renders the Movement Create page at /movements/new', () => {
  render(<MemoryRouter initialEntries={['/movements/new']}><App /></MemoryRouter>);
  expect(screen.getByText(/MovementCreate placeholder/)).toBeInTheDocument();
});

it('renders the Movement Edit page at /movements/:id/edit', () => {
  render(<MemoryRouter initialEntries={['/movements/some-id/edit']}><App /></MemoryRouter>);
  expect(screen.getByText(/MovementEdit placeholder/)).toBeInTheDocument();
});
```

(The existing test file already imports `App`, `MemoryRouter`, etc. Add to the existing `describe` block.)

- [ ] **Step 4: Build + run tests**

```bash
pnpm --dir ProjectCeres.Client build
pnpm --dir ProjectCeres.Client test
```

Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add ProjectCeres.Client/src/app/App.tsx \
        ProjectCeres.Client/src/app/App.test.tsx \
        ProjectCeres.Client/src/app/features/movements/MovementCreate.tsx \
        ProjectCeres.Client/src/app/features/movements/MovementEdit.tsx
git rm ProjectCeres.Client/src/app/pages/Movements.tsx
git commit -m "feat(spa): wire /movements/new and /movements/:id/edit routes; remove legacy pages/Movements.tsx"
```

---

## Task 6: Build the shared `MovementForm` component

**Files:**
- Create: `ProjectCeres.Client/src/app/features/movements/MovementForm.tsx`
- Create: `ProjectCeres.Client/src/app/features/movements/MovementForm.test.tsx`

`MovementForm` is the meat of Plan 2. It renders one of three field sets (Transaction / Transfer / LiabilityPayment) based on `props.type`, manages local form state, handles submit via `props.onSubmit`, and surfaces 422 field errors via `parseValidationErrors`.

The component is **stateless about `mode`** — `props.initialValues` carries the right defaults whether for Create (empty / today's date) or Edit (loaded values). The parent (`MovementCreate` / `MovementEdit`) is responsible for fetching, providing initial values, and supplying the `onSubmit` callback that calls the right typed endpoint.

**Props contract:**

```ts
type MovementFormProps = {
  type: MovementType;
  mode: 'create' | 'edit';
  initialValues: MovementFormValues;
  accounts: AccountOptionDto[];
  categories: CategoryOptionDto[];
  onSubmit: (values: MovementFormValues) => Promise<{ ok: true } | { ok: false; errors: Record<string, string> }>;
  onCancel: () => void;
  /** Edit mode only — renders the danger-zone Delete button. */
  onDelete?: () => void;
};

type MovementFormValues = {
  date: string;
  amount: string; // string in form state for `<Input type="number">` compat; converted on submit
  description: string;
  accountId: string | null;
  categoryId: string | null;
  sourceAccountId: string | null;
  destAccountId: string | null;
  assetAccountId: string | null;
  liabilityAccountId: string | null;
  isCleared: boolean;
  // Transaction-only edit-mode fields. Carried through silently.
  budgetId: string | null;
  needsReview: boolean;
};
```

Why one wide `MovementFormValues` shape rather than three discriminated unions? Two reasons: (1) the form re-uses fields across types (date, amount, description), and (2) the discriminator `props.type` already gates which fields are rendered/required. Per-type narrowing happens in the `onSubmit` callback in the parent.

**Tests to write:**

1. Create-Transaction mode: renders Date, Amount, Account, Category, Description, Cleared switch (default off), Save button. No Delete button.
2. Create-Transfer mode: renders Date, Amount, Source account, Dest account, Description. No Category, no Asset/Liability fields.
3. Create-LiabilityPayment mode: renders Date, Amount, Asset account (filtered to non-Liability), Liability account (filtered to Liability), Description.
4. Edit mode for Transaction: shows Delete button in danger zone; calls `onDelete` when clicked + confirmed.
5. 422 error mapping: when `onSubmit` returns `{ok: false, errors: {amount: 'Must be > 0'}}`, the Amount field shows the error message.
6. Business-rule 422 (`_form` key): rendered as a banner above the form.
7. Cancel: clicking Cancel calls `onCancel`.

**Implementation guidance:** mirror `QuickAddModal`'s field-rendering closely (it's already a good template for the field set + AccountCombobox + CategoryCombobox usage). The differences from `QuickAddModal`:

- Not a `Dialog` — renders inline in the page.
- Uses `parseValidationErrors` from Task 3, not the legacy ProblemDetails parser.
- Has a `Cleared` switch (Edit mode primarily, but also visible in Create for symmetry — defaults to `false`).
- Has a Delete button in Edit mode that opens an `AlertDialog` confirmation.

The full component is ~250 lines; the engineer implementing this task should treat `QuickAddModal.tsx` as the structural reference (component composition, field component, submit pattern) but write fresh code that conforms to the contract above. Don't try to share code with `QuickAddModal` — they have meaningfully different jobs and unifying them would create the long-form-modal trap the brainstorm explicitly rejected.

- [ ] **Step 1: Write the failing tests** (all 7 from the list above)
- [ ] **Step 2: Run — expect 7 FAILs**
- [ ] **Step 3: Implement `MovementForm.tsx`**
- [ ] **Step 4: Run — expect 7 PASS**
- [ ] **Step 5: Build the whole project** to confirm no TS issues elsewhere

```bash
pnpm --dir ProjectCeres.Client build
```

- [ ] **Step 6: Commit**

```bash
git add ProjectCeres.Client/src/app/features/movements/MovementForm.tsx \
        ProjectCeres.Client/src/app/features/movements/MovementForm.test.tsx
git commit -m "feat(spa): MovementForm shared component for Create + Edit"
```

---

## Task 7: Implement `MovementCreate.tsx` (replaces the placeholder)

**Files:**
- Modify: `ProjectCeres.Client/src/app/features/movements/MovementCreate.tsx`
- Create: `ProjectCeres.Client/src/app/features/movements/MovementCreate.test.tsx`

Behavior per spec §6.2:
1. If no `?type=` query param, render a 3-card chooser (Transaction / Transfer / Liability Payment). Selecting one updates the URL via `setSearchParams({ type })`.
2. Once `?type=` is present, render `<MovementForm type={...} mode="create" ...>` with the right defaults.
3. On successful save, navigate to `/movements/:id/edit?created=1` with `replace: true` and call the outlet context's `refetch` to refresh the list.
4. Show a `sonner` toast on success and on the destination page (the `?created=1` flag triggers the toast on Edit, not here — to avoid double-toast).
5. Cancel returns to `/movements`.

Tests:
- Renders the 3-card picker at `/movements/new` (no query).
- Clicking a card updates the URL to `?type=transaction`.
- With `?type=transaction`, renders the `MovementForm` (assert one Transaction-specific field is present).
- Successful POST → navigates to `/movements/<id>/edit?created=1`.
- 422 → field errors visible.

`MovementCreate` is responsible for:
- Fetching accounts and categories via `useApi`.
- Providing `initialValues` to `MovementForm` (today's date, empty fields).
- Mapping `MovementFormValues` → the right typed POST body.
- Calling `POST /api/transactions`, `/api/transfers`, or `/api/liability-payments`.
- On 201, parsing `body.id` and navigating.
- On 422, parsing the envelope via `parseValidationErrors` and returning `{ok: false, errors}` from `onSubmit`.

(The implementation follows the same fetch-and-handle-status pattern as `QuickAddModal.submit`, but uses the new validation parser.)

- [ ] **Step 1: Write failing tests**
- [ ] **Step 2: Run — expect FAIL**
- [ ] **Step 3: Implement** (replace the placeholder)
- [ ] **Step 4: Run — expect PASS**
- [ ] **Step 5: Commit**

```bash
git add ProjectCeres.Client/src/app/features/movements/MovementCreate.tsx \
        ProjectCeres.Client/src/app/features/movements/MovementCreate.test.tsx
git commit -m "feat(spa): MovementCreate page (type picker + create form)"
```

---

## Task 8: Implement `MovementEdit.tsx` (replaces the placeholder)

**Files:**
- Modify: `ProjectCeres.Client/src/app/features/movements/MovementEdit.tsx`
- Create: `ProjectCeres.Client/src/app/features/movements/MovementEdit.test.tsx`

Behavior per spec §6.3:
1. Read `:id` from `useParams`.
2. Fetch the type discriminator via `GET /api/movements/:id`. If 404, show "movement not found" and a back link.
3. Based on the resolved type, fetch the typed entity (`/api/transactions/:id` etc.) via `useApi`.
4. Fetch accounts + categories.
5. Render `<MovementForm type={resolvedType} mode="edit" initialValues={loadedDto} ...>`.
6. Hook `onSubmit` to PUT the typed endpoint.
7. Hook `onDelete` (passed to `MovementForm`) to DELETE the typed endpoint with `AlertDialog` confirmation handled inside `MovementForm` (Task 6).
8. On successful save → toast + stay on page (form reset to pristine).
9. On successful delete → toast + navigate to `/movements` and call outlet `refetch`.
10. If `?created=1` query param is present on initial render → show "Created!" toast and clean the param.

Tests:
- Loads discriminator, then typed entity, then renders the form prefilled with loaded values.
- Save → PUT called → success toast.
- Delete → DELETE called → navigates back.
- 422 on save → field errors visible.
- `?created=1` → Created toast on initial mount.

- [ ] **Step 1: Write failing tests**
- [ ] **Step 2: Run — expect FAIL**
- [ ] **Step 3: Implement**
- [ ] **Step 4: Run — expect PASS**
- [ ] **Step 5: Commit**

```bash
git add ProjectCeres.Client/src/app/features/movements/MovementEdit.tsx \
        ProjectCeres.Client/src/app/features/movements/MovementEdit.test.tsx
git commit -m "feat(spa): MovementEdit page (load + save + delete)"
```

---

## Task 9: Add the type filter dropdown to `MovementsFilterBar`

**Files:**
- Modify: `ProjectCeres.Client/src/app/features/movements/MovementsFilterBar.tsx`
- Modify: `ProjectCeres.Client/src/app/features/movements/MovementsFilterBar.test.tsx`

Add a single-select shadcn `Select` next to the existing Account combobox. Options: `All`, `Transactions`, `Transfers`, `Liability payments`. Bound to URL param `type` with values `transaction|transfer|liabilitypayment`. Clearing resets to "All" (no `type` param). Resets `page=1` on change.

If the project doesn't already have shadcn `select`, run `pnpm --dir ProjectCeres.Client dlx shadcn add select` first. (Audit `src/components/ui/select.tsx` first to avoid double-installing.)

- [ ] **Step 1: Audit the shadcn select primitive**

```bash
ls ProjectCeres.Client/src/components/ui/select.tsx
```

If missing, install it: `pnpm --dir ProjectCeres.Client dlx shadcn add select` and commit it as Task 9 step 1.5 with message `feat(design-system): add shadcn select primitive`.

- [ ] **Step 2: Write the failing test**
- [ ] **Step 3: Run — expect FAIL**
- [ ] **Step 4: Implement**
- [ ] **Step 5: Run — expect PASS**
- [ ] **Step 6: Commit**

```bash
git add ProjectCeres.Client/src/app/features/movements/MovementsFilterBar.tsx \
        ProjectCeres.Client/src/app/features/movements/MovementsFilterBar.test.tsx
git commit -m "feat(spa): type filter on Movements list"
```

---

## Task 10: Build `MovementRowMenu` and wire it into `MovementsTable`

**Files:**
- Create: `ProjectCeres.Client/src/app/features/movements/MovementRowMenu.tsx`
- Create: `ProjectCeres.Client/src/app/features/movements/MovementRowMenu.test.tsx`
- Modify: `ProjectCeres.Client/src/app/features/movements/MovementsTable.tsx`
- Modify: `ProjectCeres.Client/src/app/features/movements/MovementsTable.test.tsx`

`MovementRowMenu` is the `⋯` `DropdownMenu` with two items: Edit (navigates to `/movements/:id/edit`) and Delete (opens `AlertDialog` confirmation, calls the right typed `DELETE` endpoint, calls `onDeleted` to refresh list).

Props:

```ts
type MovementRowMenuProps = {
  movementId: string;
  movementType: MovementType;
  onDeleted: () => void;
};
```

Tests:
- `⋯` button is keyboard-reachable and opens the menu.
- Edit item navigates to `/movements/<id>/edit`.
- Delete item opens an `AlertDialog`. Confirming calls the right DELETE endpoint and `onDeleted`.
- Cancelling the dialog does not delete.
- 422 on delete (e.g., business-rule rejection) → toast.error with the message; `onDeleted` not called.

`MovementsTable` change: add a trailing column header (empty label) and a trailing `<td>` per row that mounts `MovementRowMenu`. Pass `onDeleted={refetch}` from up the tree — this requires the table to receive `refetch` as a prop, which means `MovementsLayout` must pass it down. (Adjust the `<MovementsTable items={data.items} />` call site in `MovementsLayout` to also pass `onRefetch={refetch}`.)

- [ ] **Step 1: Write failing tests for `MovementRowMenu`**
- [ ] **Step 2: Run — expect FAIL**
- [ ] **Step 3: Implement `MovementRowMenu`**
- [ ] **Step 4: Run — expect PASS**
- [ ] **Step 5: Modify `MovementsTable` to wire the menu**
- [ ] **Step 6: Update `MovementsTable.test.tsx`** to assert the row menu renders
- [ ] **Step 7: Modify `MovementsLayout` to pass `refetch` down**
- [ ] **Step 8: Build + full client test pass**
- [ ] **Step 9: Commit**

```bash
git add ProjectCeres.Client/src/app/features/movements/MovementRowMenu.tsx \
        ProjectCeres.Client/src/app/features/movements/MovementRowMenu.test.tsx \
        ProjectCeres.Client/src/app/features/movements/MovementsTable.tsx \
        ProjectCeres.Client/src/app/features/movements/MovementsTable.test.tsx \
        ProjectCeres.Client/src/app/features/movements/MovementsLayout.tsx \
        ProjectCeres.Client/src/app/features/movements/MovementsLayout.test.tsx
git commit -m "feat(spa): row ⋯ menu (Edit / Delete) on Movements table"
```

---

## Task 11: Replace the legacy `+ New` button with a route to `/movements/new`

**Files:**
- Modify: `ProjectCeres.Client/src/app/features/movements/MovementsLayout.tsx`
- Modify: `ProjectCeres.Client/src/app/features/movements/MovementsLayout.test.tsx`

Currently the page header has nothing in the right slot (Task 4 deferred this). Add a `+ New` button that navigates to `/movements/new`. **Critically:** the local `QuickAddModal` instance from the old `pages/Movements.tsx` is gone (already deleted in Task 5) — this task confirms the page header replacement.

- [ ] **Step 1: Update test**

Add an assertion to `MovementsLayout.test.tsx` that the `+ New` button is present and links to `/movements/new`.

- [ ] **Step 2: Implement**

In `MovementsLayout.tsx`, the header div should now contain:

```tsx
<div className="flex items-center justify-between">
  <h1 ref={headingRef} tabIndex={-1} className="text-3xl font-semibold outline-none">
    Movements
  </h1>
  <Button asChild>
    <Link to="new">
      <Plus className="h-4 w-4" />
      New
    </Link>
  </Button>
</div>
```

Use `<Link to="new">` (relative) so the route works correctly inside the nested route tree. Imports: `Link` from `react-router-dom`, `Plus` from `lucide-react`, `Button` from `@/components/ui/button`.

- [ ] **Step 3: Run — expect PASS**
- [ ] **Step 4: Commit**

```bash
git add ProjectCeres.Client/src/app/features/movements/MovementsLayout.tsx \
        ProjectCeres.Client/src/app/features/movements/MovementsLayout.test.tsx
git commit -m "feat(spa): + New button on Movements page routes to /movements/new"
```

---

## Task 12: Suppress quick-add on `/movements*` routes in `TopBar`

**Files:**
- Modify: `ProjectCeres.Client/src/app/layout/TopBar.tsx`
- Modify: `ProjectCeres.Client/src/app/layout/TopBar.test.tsx`

Per the locked spec, the global `+` button and its keyboard shortcut are hidden / no-op on `/movements*`. The global search (⌘K) is unaffected.

Implementation:

```tsx
import { useLocation } from 'react-router-dom';

const location = useLocation();
const onMovements = /^\/movements(\/|$)/.test(location.pathname);
```

Hide the `+` button when `onMovements`. In `useKeyboardShortcut('mod+n', ...)` (or whatever the existing shortcut is — verify by reading the file), guard with `if (onMovements) return;`.

If the keyboard shortcut isn't on a `mod+n`-style binding today (the existing TopBar only has `mod+k` for search per the file inspection), then there's no quick-add shortcut to suppress yet. In that case: only the button is hidden; document a one-line comment explaining the keyboard suppression hook is in place for when the shortcut is added.

- [ ] **Step 1: Read the file to confirm what to suppress**

```bash
grep -n "QuickAddModal\|useKeyboardShortcut" ProjectCeres.Client/src/app/layout/TopBar.tsx
```

Report findings before implementing — there may not be a shortcut to suppress.

- [ ] **Step 2: Write failing test**

Test that on `/movements`, the `aria-label="Quick add"` button is not in the DOM, and that on `/`, it is.

- [ ] **Step 3: Run — expect FAIL**
- [ ] **Step 4: Implement** the pathname-derived gate
- [ ] **Step 5: Run — expect PASS**
- [ ] **Step 6: Commit**

```bash
git add ProjectCeres.Client/src/app/layout/TopBar.tsx \
        ProjectCeres.Client/src/app/layout/TopBar.test.tsx
git commit -m "feat(spa): suppress quick-add on /movements routes"
```

---

## Task 13: Add view-transition CSS hooks

**Files:**
- Modify: `ProjectCeres.Client/src/index.css` (or wherever the global CSS lives — grep for `@layer base` to find it)
- Modify: `ProjectCeres.Client/src/app/features/movements/MovementsTable.tsx` (apply `view-transition-name` per row)
- Modify: `ProjectCeres.Client/src/app/features/movements/MovementForm.tsx` (apply `view-transition-name: movement-form` to the form container)

Per spec §4: "View-transition CSS hooks (no animation behaviour today, just named layers for a future polish PR): each Movements row: `view-transition-name: movement-row-{id}`. Form container: `view-transition-name: movement-form`."

These are pure CSS additions. No JS animation controller. Modern browsers ignore `view-transition-name` outside of an active view transition, so the hooks are inert today.

- [ ] **Step 1: Add inline `style` to row elements** in `MovementsTable.tsx`:

```tsx
<tr
  key={`${item.movementType}-${item.id}`}
  className="border-t border-border"
  style={{ viewTransitionName: `movement-row-${item.id}` }}
>
```

(TypeScript will complain about the camelCase `viewTransitionName`. If so, add `as React.CSSProperties` cast on the style object, or use `style={{ ['view-transition-name' as string]: ... }}`. Use whichever the existing codebase prefers.)

- [ ] **Step 2: Add `style` to the `MovementForm` outer container**:

```tsx
<form
  onSubmit={handleSubmit}
  className="space-y-4"
  style={{ viewTransitionName: 'movement-form' }}
>
```

- [ ] **Step 3: Build + tests**

```bash
pnpm --dir ProjectCeres.Client build
pnpm --dir ProjectCeres.Client test
```

Expected: PASS (no behavior change, just CSS hooks).

- [ ] **Step 4: Commit**

```bash
git add ProjectCeres.Client/src/app/features/movements/MovementsTable.tsx \
        ProjectCeres.Client/src/app/features/movements/MovementForm.tsx
git commit -m "feat(spa): view-transition-name hooks on movement rows and form (no animation yet)"
```

---

## Task 14: Final regression and manual smoke

**Files:** none (verification).

- [ ] **Step 1: Full client test suite**

```bash
pnpm --dir ProjectCeres.Client test
```

Expected: all-green.

- [ ] **Step 2: Build production bundle**

```bash
pnpm --dir ProjectCeres.Client build
```

Expected: PASS, both `dist/index.html` and `dist/design-system.html` produced.

- [ ] **Step 3: Manual smoke** (the user runs this — list the steps to walk through)

Start the .NET app and Vite dev server, navigate to `/app/movements`, and verify:

- The list loads.
- The type filter dropdown filters correctly.
- The row `⋯` menu opens; Edit navigates; Delete confirms then removes.
- `+ New` routes to `/app/movements/new`.
- The type-picker landing renders three cards.
- Selecting a card brings up the form; saving creates a row and lands on the Edit page with a "Created" toast.
- Editing a row saves correctly; the Cleared switch round-trips.
- Danger-zone Delete on the Edit page works.
- The TopBar `+` button is hidden on `/app/movements*` and present on other routes (e.g. `/app/dashboard`).
- ⌘K search works on `/app/movements`.

If anything fails, fix in the smallest possible commit and re-run.

---

## What Plan 2 ships

After Task 14, the SPA fully supports Movements CRUD via the API surface Plan 1 built. Users can:

- Filter the Movements list by type.
- Create a movement via the routed Create page (with type picker + deep-link support).
- Edit any movement via the row `⋯` menu or by clicking `+ New` and navigating.
- Delete a movement from the row menu or the danger zone on Edit.
- The TopBar quick-add stays out of the way on movements routes.

What's still pending (Plan 3):
- Attachments client (drop-zone + Create→Edit pending-file hand-off).
- Bulk-cleared and CSV export buttons in the Movements page header.
- Razor 302 redirects on `TransactionsController` and `TransfersController`, view deletions.
- Doc sync per spec §10.
