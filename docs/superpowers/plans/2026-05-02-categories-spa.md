# Categories SPA Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking. **Single commit at the end (Task 19); every earlier task explicitly does NOT commit.**

**Goal:** Replace the placeholder `/app/categories` route with a real, search-first list-and-form page, and slim the Razor `CategoriesController` to a 302 redirect — all in one commit.

**Architecture:** New `features/categories/` folder following the Settings template (commit e842250) plus the Budgets layout precedent. `CategoriesLayout` owns the list-side data fetch and renders Tabs (Income/Expense) + filter bar + a single Card with the active tab's table. `CategoryCreate` and `CategoryEdit` mount at nested routes (`/categories/new`, `/categories/:id/edit`) and reuse a shared `CategoryForm`. Search is debounced 200 ms and filters client-side (the dataset is bounded to ≤30 rows).

**Tech Stack:** React 19 + TypeScript + Vite, shadcn/ui, base-ui Popover/Command primitives, Vitest + React Testing Library, ASP.NET Core (Razor controller cutover).

**Spec:** `docs/superpowers/specs/2026-05-02-categories-spa-design.md`

---

## File structure (locked from spec, with one correction)

**Create:**
- `ProjectCeres.Client/src/app/features/categories/categories-api.ts` — URL constants + DTO types
- `ProjectCeres.Client/src/app/features/categories/CategoriesTable.tsx` — pure UI table
- `ProjectCeres.Client/src/app/features/categories/CategoriesTable.test.tsx`
- `ProjectCeres.Client/src/app/features/categories/CategoryRowMenu.tsx` — pure UI ⋯ menu + AlertDialog
- `ProjectCeres.Client/src/app/features/categories/CategoryRowMenu.test.tsx`
- `ProjectCeres.Client/src/app/features/categories/CategoryForm.tsx` — pure UI form (Create + Edit)
- `ProjectCeres.Client/src/app/features/categories/CategoryForm.test.tsx`
- `ProjectCeres.Client/src/app/features/categories/CategoriesLayout.tsx` — page glue: GET + Tabs + filter
- `ProjectCeres.Client/src/app/features/categories/CategoriesLayout.test.tsx`
- `ProjectCeres.Client/src/app/features/categories/CategoryCreate.tsx` — page glue for `/new` (POST + toast)
- `ProjectCeres.Client/src/app/features/categories/CategoryCreate.test.tsx`
- `ProjectCeres.Client/src/app/features/categories/CategoryEdit.tsx` — page glue for `/:id/edit` (GET + PATCH + toast)
- `ProjectCeres.Client/src/app/features/categories/CategoryEdit.test.tsx`

**Modify:**
- `ProjectCeres.Client/src/app/pages/Categories.tsx` — replace placeholder with one-line re-export
- `ProjectCeres.Client/src/app/App.tsx` — add nested routes
- `ProjectCeres/Controllers/CategoriesController.cs` — slim to 4 redirects
- `ProjectCeres/Services/ICategoryService.cs` — remove Razor-throwing method declarations
- `ProjectCeres/Services/CategoryService.cs` — remove Razor-throwing method bodies
- `ProjectCeres.Tests/Integration/CategoryServiceTests.cs` — remove Razor-throwing-method tests

**Delete:**
- `ProjectCeres/Views/Categories/Index.cshtml`
- `ProjectCeres/Views/Categories/Create.cshtml`
- `ProjectCeres/Views/Categories/Edit.cshtml`
- `ProjectCeres/Views/Categories/Deactivate.cshtml`
- `ProjectCeres/ViewModels/CategoryCreateViewModel.cs`
- `ProjectCeres/ViewModels/CategoryEditViewModel.cs`

**Correction vs spec:** the spec mentions `<ConfirmDialog>` for the archive confirm. The actual project pattern (see `BudgetRowMenu.tsx`) is `<AlertDialog>` from `@/components/ui/alert-dialog`. The existing `ConfirmDialog` component in `src/components/ConfirmDialog.tsx` is a Razor-style submit-form confirm and isn't used by SPA features. **The plan uses AlertDialog.**

**SPA-page template constraints (locked from Settings commit e842250):**
- `features/<area>/` folder. `<area>-api.ts` is logic-free.
- Page+Form split: pure UI components don't fetch or toast.
- Popover+Command for any list picker — never shadcn `<Select>`.
- `useApi` for GET, hand-rolled `fetch` for mutations.
- `sonner` for toasts. Mock with `vi.mock('sonner', ...)`.
- Tests: `global.fetch = mockFetch as unknown as typeof fetch`. No MSW.
- CategoryType field on Edit is server-immutable (the API DTO doesn't bind it). The form's read-only display is a UX hint; the structural guarantee is the API. Document inline.

---

## Task 1: categories-api.ts (URL builders + DTOs)

**Files:**
- Create: `ProjectCeres.Client/src/app/features/categories/categories-api.ts`

This file is logic-free. Mirrors `settings-api.ts` shape.

- [ ] **Step 1: Write the file**

```typescript
// ---------- URL builders ----------

export const CATEGORIES_URL = '/api/categories';
export const CATEGORY_BY_ID_URL = (id: string) => `/api/categories/${id}`;
export const CATEGORY_ARCHIVE_URL = (id: string) => `/api/categories/${id}/archive`;
export const CATEGORY_TYPES_URL = '/api/category-types';

export function buildListUrl(includeInactive: boolean): string {
  return includeInactive ? `${CATEGORIES_URL}?includeInactive=true` : CATEGORIES_URL;
}

// ---------- DTOs ----------

export type LifestyleTag = 'Needs' | 'Wants' | 'Savings';

export type CategoryListItemDto = {
  id: string;
  name: string;
  categoryTypeId: number;
  categoryTypeName: string;       // "Income" | "Expense"
  lifestyleTag: LifestyleTag | null;
  isActive: boolean;
  isSystem: boolean;
};

export type CategoryDetailDto = CategoryListItemDto;

export type CategoryTypeDto = {
  id: number;
  name: string;                   // "Income" | "Expense"
};

export type CreateCategoryRequest = {
  name: string;
  categoryTypeId: number;
  lifestyleTag: LifestyleTag | null;
};

/**
 * The PATCH body deliberately omits categoryTypeId. The server's
 * UpdateCategoryRequest does not bind that field — verified in
 * ProjectCeres/ViewModels/CategoryDtos.cs on 2026-05-02. CategoryType
 * is structurally immutable on the wire; the form's read-only display
 * on Edit is a UX hint, not a security boundary.
 */
export type UpdateCategoryRequest = {
  name: string;
  lifestyleTag: LifestyleTag | null;
};

// ---------- Form values (UI layer) ----------

export type CategoryFormValues = {
  name: string;
  categoryTypeId: number;         // editable on Create, read-only display on Edit
  lifestyleTag: LifestyleTag | null;
};

// ---------- Reserved system-category ids ----------

/**
 * IDs the API marks as system OR reserved fallback categories. Used by
 * the SPA to suppress row actions and render the System badge. Mirrors
 * the server-side rule in CategoryPolicies.IsReserved.
 */
export const RESERVED_UNCATEGORIZED_IDS = new Set<string>([
  '20000000-0000-0000-0000-000000000025',  // Uncategorized Income
  '20000000-0000-0000-0000-000000000026',  // Uncategorized Expense
]);

export function isLockedCategory(c: { id: string; isSystem: boolean }): boolean {
  return c.isSystem || RESERVED_UNCATEGORIZED_IDS.has(c.id);
}

// ---------- Error envelope ----------

export type ApiErrorEnvelope = {
  error: {
    code: string;
    message: string;
    details: unknown[];
  };
};
```

- [ ] **Step 2: Verify typescript compiles**

Run: `cd ProjectCeres.Client && pnpm tsc --noEmit`
Expected: no errors.

- [ ] **Step 3: DO NOT COMMIT.**

Verify with: `cd <repo> && git status --short`
Expected: only an untracked `ProjectCeres.Client/src/app/features/categories/` directory. Nothing staged, nothing committed.

---

## Task 2: CategoriesTable — failing tests first

**Files:**
- Create: `ProjectCeres.Client/src/app/features/categories/CategoriesTable.test.tsx`

`CategoriesTable` is pure UI. Receives a list of rows + a row-menu render prop. Knows nothing about fetch or toast.

- [ ] **Step 1: Write the test file**

```tsx
import { fireEvent, render, screen, within } from '@testing-library/react';
import { describe, expect, it, vi } from 'vitest';
import { CategoriesTable } from './CategoriesTable';
import type { CategoryListItemDto } from './categories-api';

const SYSTEM_CATEGORY_ID = '20000000-0000-0000-0000-000000000001'; // Opening Balance (isSystem true)
const UNCATEGORIZED_INCOME_ID = '20000000-0000-0000-0000-000000000025';

const rows: CategoryListItemDto[] = [
  { id: 'a-1', name: 'Salary',     categoryTypeId: 1, categoryTypeName: 'Income', lifestyleTag: null,    isActive: true,  isSystem: false },
  { id: 'a-2', name: 'Freelance',  categoryTypeId: 1, categoryTypeName: 'Income', lifestyleTag: null,    isActive: true,  isSystem: false },
  { id: 'a-3', name: 'Old Income', categoryTypeId: 1, categoryTypeName: 'Income', lifestyleTag: null,    isActive: false, isSystem: false },
  { id: SYSTEM_CATEGORY_ID,       name: 'Opening Balance',     categoryTypeId: 1, categoryTypeName: 'Income', lifestyleTag: null, isActive: true, isSystem: true },
  { id: UNCATEGORIZED_INCOME_ID,  name: 'Uncategorized Income', categoryTypeId: 1, categoryTypeName: 'Income', lifestyleTag: null, isActive: true, isSystem: false },
];

function renderTable(props?: Partial<React.ComponentProps<typeof CategoriesTable>>) {
  return render(
    <CategoriesTable
      rows={rows}
      onChanged={vi.fn()}
      {...props}
    />,
  );
}

describe('CategoriesTable', () => {
  it('renders one row per category', () => {
    renderTable();
    expect(screen.getByText('Salary')).toBeInTheDocument();
    expect(screen.getByText('Freelance')).toBeInTheDocument();
    expect(screen.getByText('Opening Balance')).toBeInTheDocument();
  });

  it('shows the System badge with tooltip text on isSystem rows', async () => {
    renderTable();
    const systemBadges = screen.getAllByText('System');
    expect(systemBadges.length).toBeGreaterThanOrEqualTo(1);
    // The tooltip text is in an aria-label or sr-only; the visible badge says "System".
    // The tooltip content itself only renders on hover — we verify the badge is present.
  });

  it('shows the System badge on RESERVED_UNCATEGORIZED rows even when isSystem=false', () => {
    renderTable();
    // Find the row containing "Uncategorized Income" and assert the System badge is in it.
    const uncategorizedRow = screen.getByText('Uncategorized Income').closest('tr')!;
    expect(within(uncategorizedRow).getByText('System')).toBeInTheDocument();
  });

  it('shows the Archived badge on rows where isActive=false', () => {
    renderTable();
    const archivedRow = screen.getByText('Old Income').closest('tr')!;
    expect(within(archivedRow).getByText('Archived')).toBeInTheDocument();
  });

  it('renders no row-menu trigger for system or reserved rows', () => {
    renderTable();
    const systemRow = screen.getByText('Opening Balance').closest('tr')!;
    expect(within(systemRow).queryByRole('button', { name: /row actions/i })).toBeNull();
    const uncategorizedRow = screen.getByText('Uncategorized Income').closest('tr')!;
    expect(within(uncategorizedRow).queryByRole('button', { name: /row actions/i })).toBeNull();
  });

  it('renders a row-menu trigger for active user rows', () => {
    renderTable();
    const userRow = screen.getByText('Salary').closest('tr')!;
    expect(within(userRow).getByRole('button', { name: /row actions/i })).toBeInTheDocument();
  });

  it('renders the empty state when rows array is empty', () => {
    renderTable({ rows: [] });
    expect(screen.getByText(/no categories/i)).toBeInTheDocument();
  });
});
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `cd ProjectCeres.Client && pnpm test CategoriesTable`
Expected: FAIL — `CategoriesTable` not exported / module not found.

- [ ] **Step 3: DO NOT COMMIT.**

---

## Task 3: CategoriesTable — implementation

**Files:**
- Create: `ProjectCeres.Client/src/app/features/categories/CategoriesTable.tsx`

Pure UI. Renders a `<Table>` with one row per category. Sort order is the caller's responsibility — table renders rows in the order it receives them.

- [ ] **Step 1: Write the implementation**

```tsx
import { Info } from 'lucide-react';
import { Badge } from '@/components/ui/badge';
import {
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from '@/components/ui/table';
import {
  Tooltip,
  TooltipContent,
  TooltipProvider,
  TooltipTrigger,
} from '@/components/ui/tooltip';
import { CategoryRowMenu } from './CategoryRowMenu';
import {
  isLockedCategory,
  type CategoryListItemDto,
} from './categories-api';

const SYSTEM_TOOLTIP =
  'Created by the system. These are required for imports and accounting and cannot be edited or archived.';

type Props = {
  rows: CategoryListItemDto[];
  /** Called after a successful archive so the parent layout can refetch. */
  onChanged: () => void;
};

export function CategoriesTable({ rows, onChanged }: Props) {
  if (rows.length === 0) {
    return (
      <div className="px-3 py-8 text-center text-sm text-muted-foreground italic">
        No categories.
      </div>
    );
  }

  return (
    <TooltipProvider delay={200}>
      <Table>
        <TableHeader>
          <TableRow>
            <TableHead>Name</TableHead>
            <TableHead className="w-32">Lifestyle</TableHead>
            <TableHead className="w-32" />
            <TableHead className="w-12" />
          </TableRow>
        </TableHeader>
        <TableBody>
          {rows.map((row) => {
            const locked = isLockedCategory(row);
            return (
              <TableRow
                key={row.id}
                className={!row.isActive ? 'opacity-60' : undefined}
              >
                <TableCell className="text-sm">{row.name}</TableCell>
                <TableCell>
                  {row.lifestyleTag ? (
                    <Badge variant="secondary">{row.lifestyleTag}</Badge>
                  ) : null}
                </TableCell>
                <TableCell>
                  <div className="flex items-center gap-1.5">
                    {locked ? (
                      <Tooltip>
                        <TooltipTrigger
                          render={
                            <Badge
                              variant="outline"
                              className="gap-1 cursor-help"
                            >
                              System
                              <Info className="h-3 w-3" aria-hidden="true" />
                            </Badge>
                          }
                        />
                        <TooltipContent className="max-w-xs">
                          {SYSTEM_TOOLTIP}
                        </TooltipContent>
                      </Tooltip>
                    ) : null}
                    {!row.isActive ? (
                      <Badge variant="secondary">Archived</Badge>
                    ) : null}
                  </div>
                </TableCell>
                <TableCell>
                  {locked ? null : (
                    <CategoryRowMenu
                      category={row}
                      onChanged={onChanged}
                    />
                  )}
                </TableCell>
              </TableRow>
            );
          })}
        </TableBody>
      </Table>
    </TooltipProvider>
  );
}
```

- [ ] **Step 2: Run tests to verify they pass**

Run: `cd ProjectCeres.Client && pnpm test CategoriesTable`
Expected: PASS — all 7 tests green.

`CategoryRowMenu` doesn't exist yet, but `CategoriesTable` imports it. The test file mocks nothing — it just renders the table. The build will succeed because TypeScript module resolution is lazy at runtime in Vitest. **If the test fails with "CategoryRowMenu is undefined" instead of an import error, that's a real failure and Task 4 must run before this passes.** In that case, Task 3's tests will be unblocked once Task 5 (CategoryRowMenu impl) is in place.

If Task 3 step 2 actually produces a failure because of the missing CategoryRowMenu, **proceed to Task 4 anyway** and re-run Task 3 step 2 after Task 5 completes. Most likely it will pass at first because the only test that would render `CategoryRowMenu` is "renders a row-menu trigger for active user rows" — and even that test uses `getByRole('button', { name: /row actions/i })` which will fail if the import is broken. Adjust expectations accordingly.

- [ ] **Step 3: DO NOT COMMIT.**

---

## Task 4: CategoryRowMenu — failing tests first

**Files:**
- Create: `ProjectCeres.Client/src/app/features/categories/CategoryRowMenu.test.tsx`

The row menu owns the AlertDialog confirm and the archive PATCH. Tests use `global.fetch = mockFetch` and `vi.mock('sonner', …)`.

- [ ] **Step 1: Write the test file**

```tsx
import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import { toast } from 'sonner';
import { CategoryRowMenu } from './CategoryRowMenu';
import type { CategoryListItemDto } from './categories-api';

vi.mock('sonner', () => ({
  toast: { success: vi.fn(), error: vi.fn() },
}));

const mockFetch = vi.fn();

const activeRow: CategoryListItemDto = {
  id: 'a-1',
  name: 'Groceries',
  categoryTypeId: 2,
  categoryTypeName: 'Expense',
  lifestyleTag: 'Needs',
  isActive: true,
  isSystem: false,
};

const archivedRow: CategoryListItemDto = { ...activeRow, id: 'a-2', name: 'Old', isActive: false };

beforeEach(() => {
  global.fetch = mockFetch as unknown as typeof fetch;
  mockFetch.mockReset();
});

afterEach(() => vi.resetAllMocks());

function renderMenu(category: CategoryListItemDto, onChanged = vi.fn()) {
  return render(
    <MemoryRouter initialEntries={['/categories']}>
      <Routes>
        <Route path="/categories" element={<CategoryRowMenu category={category} onChanged={onChanged} />} />
        <Route path="/categories/:id/edit" element={<div data-testid="edit-page">EDIT</div>} />
      </Routes>
    </MemoryRouter>,
  );
}

describe('CategoryRowMenu', () => {
  it('shows Edit and Archive items for active user rows', async () => {
    renderMenu(activeRow);
    fireEvent.click(screen.getByRole('button', { name: /row actions/i }));
    expect(await screen.findByText('Edit')).toBeInTheDocument();
    expect(screen.getByText('Archive…')).toBeInTheDocument();
  });

  it('shows only Edit for archived rows', async () => {
    renderMenu(archivedRow);
    fireEvent.click(screen.getByRole('button', { name: /row actions/i }));
    expect(await screen.findByText('Edit')).toBeInTheDocument();
    expect(screen.queryByText('Archive…')).toBeNull();
  });

  it('clicking Edit navigates to /categories/:id/edit', async () => {
    renderMenu(activeRow);
    fireEvent.click(screen.getByRole('button', { name: /row actions/i }));
    fireEvent.click(await screen.findByText('Edit'));
    expect(await screen.findByTestId('edit-page')).toBeInTheDocument();
  });

  it('clicking Archive opens the confirm dialog with the category name', async () => {
    renderMenu(activeRow);
    fireEvent.click(screen.getByRole('button', { name: /row actions/i }));
    fireEvent.click(await screen.findByText('Archive…'));
    expect(await screen.findByText(/archive 'groceries'\?/i)).toBeInTheDocument();
  });

  it('archive 204 fires toast.success and onChanged', async () => {
    mockFetch.mockResolvedValue({ ok: true, status: 204, json: async () => null });
    const onChanged = vi.fn();
    renderMenu(activeRow, onChanged);
    fireEvent.click(screen.getByRole('button', { name: /row actions/i }));
    fireEvent.click(await screen.findByText('Archive…'));
    fireEvent.click(await screen.findByRole('button', { name: 'Archive' }));
    await waitFor(() => expect(toast.success).toHaveBeenCalledWith('Archived.'));
    expect(onChanged).toHaveBeenCalledTimes(1);
  });

  it('archive 409 fires toast.error with the in-use message', async () => {
    mockFetch.mockResolvedValue({
      ok: false,
      status: 409,
      json: async () => ({ error: { code: 'CATEGORY_IN_USE', message: 'This category has transactions. Reassign them before archiving.', details: [] } }),
    });
    renderMenu(activeRow);
    fireEvent.click(screen.getByRole('button', { name: /row actions/i }));
    fireEvent.click(await screen.findByText('Archive…'));
    fireEvent.click(await screen.findByRole('button', { name: 'Archive' }));
    await waitFor(() =>
      expect(toast.error).toHaveBeenCalledWith('This category has transactions. Reassign them before archiving.'),
    );
  });

  it('archive other-error fires the generic toast', async () => {
    mockFetch.mockResolvedValue({ ok: false, status: 500, json: async () => null });
    renderMenu(activeRow);
    fireEvent.click(screen.getByRole('button', { name: /row actions/i }));
    fireEvent.click(await screen.findByText('Archive…'));
    fireEvent.click(await screen.findByRole('button', { name: 'Archive' }));
    await waitFor(() =>
      expect(toast.error).toHaveBeenCalledWith("Couldn't archive. Try again."),
    );
  });
});
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `cd ProjectCeres.Client && pnpm test CategoryRowMenu`
Expected: FAIL — `CategoryRowMenu` not exported / module not found.

- [ ] **Step 3: DO NOT COMMIT.**

---

## Task 5: CategoryRowMenu — implementation

**Files:**
- Create: `ProjectCeres.Client/src/app/features/categories/CategoryRowMenu.tsx`

Mirrors `BudgetRowMenu` shape (DropdownMenu trigger + AlertDialog confirm). No reactivate path — Categories API doesn't expose one.

- [ ] **Step 1: Write the implementation**

```tsx
import { useState } from 'react';
import { useNavigate } from 'react-router-dom';
import { toast } from 'sonner';
import { MoreHorizontal } from 'lucide-react';
import { Button } from '@/components/ui/button';
import {
  AlertDialog,
  AlertDialogAction,
  AlertDialogCancel,
  AlertDialogContent,
  AlertDialogDescription,
  AlertDialogFooter,
  AlertDialogHeader,
  AlertDialogTitle,
} from '@/components/ui/alert-dialog';
import {
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuItem,
  DropdownMenuTrigger,
} from '@/components/ui/dropdown-menu';
import {
  CATEGORY_ARCHIVE_URL,
  type ApiErrorEnvelope,
  type CategoryListItemDto,
} from './categories-api';

type Props = {
  category: CategoryListItemDto;
  /** Called after a successful archive so the parent layout can refetch. */
  onChanged: () => void;
};

export function CategoryRowMenu({ category, onChanged }: Props) {
  const navigate = useNavigate();
  const [confirmOpen, setConfirmOpen] = useState(false);

  async function handleArchive() {
    setConfirmOpen(false);
    try {
      const response = await fetch(CATEGORY_ARCHIVE_URL(category.id), {
        method: 'PATCH',
      });
      if (response.ok) {
        toast.success('Archived.');
        onChanged();
        return;
      }
      // 409 CATEGORY_IN_USE is the meaningful error path. Surface the server's
      // message verbatim. Any other non-2xx falls back to a generic toast.
      if (response.status === 409) {
        const body = (await response.json().catch(() => null)) as ApiErrorEnvelope | null;
        toast.error(body?.error.message ?? "Couldn't archive. Try again.");
        return;
      }
      toast.error("Couldn't archive. Try again.");
    } catch {
      toast.error("Couldn't archive. Try again.");
    }
  }

  return (
    <>
      <DropdownMenu>
        <DropdownMenuTrigger
          render={
            <Button variant="ghost" size="icon" aria-label="Row actions">
              <MoreHorizontal className="h-4 w-4" />
            </Button>
          }
        />
        <DropdownMenuContent align="end">
          <DropdownMenuItem
            onClick={() => navigate(`/categories/${category.id}/edit`)}
          >
            Edit
          </DropdownMenuItem>
          {category.isActive ? (
            <DropdownMenuItem onClick={() => setConfirmOpen(true)}>
              Archive…
            </DropdownMenuItem>
          ) : null}
        </DropdownMenuContent>
      </DropdownMenu>

      <AlertDialog open={confirmOpen} onOpenChange={setConfirmOpen}>
        <AlertDialogContent>
          <AlertDialogHeader>
            <AlertDialogTitle>Archive '{category.name}'?</AlertDialogTitle>
            <AlertDialogDescription>
              You can still see archived categories with the toggle. This won't
              affect existing transactions.
            </AlertDialogDescription>
          </AlertDialogHeader>
          <AlertDialogFooter>
            <AlertDialogCancel>Cancel</AlertDialogCancel>
            <AlertDialogAction onClick={handleArchive}>
              Archive
            </AlertDialogAction>
          </AlertDialogFooter>
        </AlertDialogContent>
      </AlertDialog>
    </>
  );
}
```

- [ ] **Step 2: Run tests to verify they pass**

Run: `cd ProjectCeres.Client && pnpm test CategoryRowMenu`
Expected: PASS — all 7 tests green.

- [ ] **Step 3: Re-run Task 3 step 2 to verify CategoriesTable now passes**

Run: `cd ProjectCeres.Client && pnpm test CategoriesTable`
Expected: PASS — 7 tests green.

- [ ] **Step 4: DO NOT COMMIT.**

---

## Task 6: CategoryForm — failing tests first

**Files:**
- Create: `ProjectCeres.Client/src/app/features/categories/CategoryForm.test.tsx`

`CategoryForm` is pure UI (Settings precedent). Owns local state, computes `isDirty` against initialValues, calls `props.onSubmit`. Knows nothing about fetch or toast.

- [ ] **Step 1: Write the test file**

```tsx
import { fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import { describe, expect, it, vi } from 'vitest';
import { CategoryForm } from './CategoryForm';
import type { CategoryFormValues, CategoryTypeDto } from './categories-api';

const initialCreate: CategoryFormValues = {
  name: '',
  categoryTypeId: 2,        // Expense default
  lifestyleTag: null,
};

const initialEdit: CategoryFormValues = {
  name: 'Groceries',
  categoryTypeId: 2,
  lifestyleTag: 'Needs',
};

const categoryTypes: CategoryTypeDto[] = [
  { id: 1, name: 'Income' },
  { id: 2, name: 'Expense' },
];

function renderForm(overrides?: {
  mode?: 'create' | 'edit';
  initialValues?: CategoryFormValues;
  onSubmit?: ReturnType<typeof vi.fn>;
}) {
  const mode = overrides?.mode ?? 'create';
  const initialValues = overrides?.initialValues ?? (mode === 'create' ? initialCreate : initialEdit);
  const onSubmit = overrides?.onSubmit ?? vi.fn().mockResolvedValue({ ok: true });
  const utils = render(
    <CategoryForm
      mode={mode}
      initialValues={initialValues}
      categoryTypes={categoryTypes}
      onSubmit={onSubmit}
    />,
  );
  return { ...utils, onSubmit };
}

describe('CategoryForm', () => {
  it('renders Name, CategoryType, LifestyleTag with initial values (Edit)', () => {
    renderForm({ mode: 'edit' });
    expect(screen.getByLabelText(/name/i)).toHaveValue('Groceries');
    // CategoryType on Edit is read-only — verify the static label text shows.
    expect(within(screen.getByLabelText(/type/i)).getByText('Expense')).toBeInTheDocument();
    // LifestyleTag picker shows current value.
    expect(within(screen.getByLabelText(/lifestyle/i)).getByText('Needs')).toBeInTheDocument();
  });

  it('CategoryType picker is editable on Create', () => {
    renderForm({ mode: 'create' });
    expect(screen.getByLabelText(/type/i)).toHaveAttribute('aria-expanded');
    // The trigger is a button (Popover combobox).
  });

  it('CategoryType is read-only on Edit (no Popover trigger)', () => {
    renderForm({ mode: 'edit' });
    // The Edit-mode trigger renders as a static span, not a button with aria-expanded.
    const trigger = screen.getByLabelText(/type/i);
    expect(trigger).not.toHaveAttribute('aria-expanded');
  });

  it('Save and Reset are disabled when nothing has changed (Edit)', () => {
    renderForm({ mode: 'edit' });
    expect(screen.getByRole('button', { name: /save/i })).toBeDisabled();
    expect(screen.getByRole('button', { name: /reset/i })).toBeDisabled();
  });

  it('Save and Reset enable when Name changes', () => {
    renderForm({ mode: 'edit' });
    fireEvent.change(screen.getByLabelText(/name/i), { target: { value: 'Food' } });
    expect(screen.getByRole('button', { name: /save/i })).toBeEnabled();
    expect(screen.getByRole('button', { name: /reset/i })).toBeEnabled();
  });

  it('Reset restores values to the snapshot and re-disables the buttons', () => {
    renderForm({ mode: 'edit' });
    fireEvent.change(screen.getByLabelText(/name/i), { target: { value: 'Food' } });
    fireEvent.click(screen.getByRole('button', { name: /reset/i }));
    expect(screen.getByLabelText(/name/i)).toHaveValue('Groceries');
    expect(screen.getByRole('button', { name: /save/i })).toBeDisabled();
  });

  it('Save shows "Saving…" while onSubmit is pending', () => {
    let resolveSubmit: (v: { ok: true }) => void = () => {};
    const onSubmit = vi.fn(
      () => new Promise<{ ok: true }>((resolve) => { resolveSubmit = resolve; }),
    );
    renderForm({ mode: 'edit', onSubmit });
    fireEvent.change(screen.getByLabelText(/name/i), { target: { value: 'Food' } });
    fireEvent.click(screen.getByRole('button', { name: /save/i }));
    expect(screen.getByRole('button', { name: /saving/i })).toBeDisabled();
    resolveSubmit({ ok: true });
  });

  it('Save greys back out after onSubmit returns ok:true', async () => {
    const onSubmit = vi.fn().mockResolvedValue({ ok: true });
    renderForm({ mode: 'edit', onSubmit });
    fireEvent.change(screen.getByLabelText(/name/i), { target: { value: 'Food' } });
    fireEvent.click(screen.getByRole('button', { name: /save/i }));
    await waitFor(() =>
      expect(screen.getByRole('button', { name: /save/i })).toBeDisabled(),
    );
  });

  it('inputs retain edits when onSubmit returns ok:false', async () => {
    const onSubmit = vi.fn().mockResolvedValue({ ok: false });
    renderForm({ mode: 'edit', onSubmit });
    fireEvent.change(screen.getByLabelText(/name/i), { target: { value: 'Food' } });
    fireEvent.click(screen.getByRole('button', { name: /save/i }));
    await waitFor(() =>
      expect(screen.getByRole('button', { name: /save/i })).toBeEnabled(),
    );
    expect(screen.getByLabelText(/name/i)).toHaveValue('Food');
  });
});
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `cd ProjectCeres.Client && pnpm test CategoryForm`
Expected: FAIL — `CategoryForm` not exported / module not found.

- [ ] **Step 3: DO NOT COMMIT.**

---

## Task 7: CategoryForm — implementation

**Files:**
- Create: `ProjectCeres.Client/src/app/features/categories/CategoryForm.tsx`

Pure UI. Three fields. CategoryType becomes a static read-only label on Edit mode.

- [ ] **Step 1: Write the implementation**

```tsx
import { useState } from 'react';
import { Check, ChevronsUpDown, Info } from 'lucide-react';
import { Button } from '@/components/ui/button';
import {
  Command,
  CommandGroup,
  CommandItem,
  CommandList,
} from '@/components/ui/command';
import { Input } from '@/components/ui/input';
import { Label } from '@/components/ui/label';
import {
  Popover,
  PopoverContent,
  PopoverTrigger,
} from '@/components/ui/popover';
import {
  Tooltip,
  TooltipContent,
  TooltipProvider,
  TooltipTrigger,
} from '@/components/ui/tooltip';
import { cn } from '@/lib/utils';
import type {
  CategoryFormValues,
  CategoryTypeDto,
  LifestyleTag,
} from './categories-api';

type SubmitResult = { ok: true } | { ok: false };

type Props = {
  mode: 'create' | 'edit';
  initialValues: CategoryFormValues;
  categoryTypes: CategoryTypeDto[];
  onSubmit: (values: CategoryFormValues) => Promise<SubmitResult>;
};

const LIFESTYLE_OPTIONS: { value: LifestyleTag | null; label: string }[] = [
  { value: null,      label: 'None' },
  { value: 'Needs',   label: 'Needs' },
  { value: 'Wants',   label: 'Wants' },
  { value: 'Savings', label: 'Savings' },
];

const TYPE_LOCKED_TOOLTIP =
  'The type cannot be changed after creation. Create a new category if you need a different type.';

function shallowEqual(a: CategoryFormValues, b: CategoryFormValues): boolean {
  return (
    a.name           === b.name           &&
    a.categoryTypeId === b.categoryTypeId &&
    a.lifestyleTag   === b.lifestyleTag
  );
}

export function CategoryForm({ mode, initialValues, categoryTypes, onSubmit }: Props) {
  const [snapshot, setSnapshot] = useState<CategoryFormValues>(initialValues);
  const [values, setValues] = useState<CategoryFormValues>(initialValues);
  const [submitting, setSubmitting] = useState(false);

  const isDirty = !shallowEqual(values, snapshot);

  async function handleSubmit(e: React.FormEvent<HTMLFormElement>) {
    e.preventDefault();
    if (!isDirty || submitting) return;
    setSubmitting(true);
    const result = await onSubmit(values);
    setSubmitting(false);
    if (result.ok) {
      setSnapshot(values);
    }
  }

  function handleReset() {
    if (submitting) return;
    setValues(snapshot);
  }

  const selectedType = categoryTypes.find((t) => t.id === values.categoryTypeId);
  const selectedLifestyleLabel =
    LIFESTYLE_OPTIONS.find((o) => o.value === values.lifestyleTag)?.label ?? 'None';

  return (
    <TooltipProvider delay={200}>
      <form onSubmit={handleSubmit} className="space-y-6">
        <div className="space-y-1.5">
          <Label htmlFor="categoryName">Name</Label>
          <Input
            id="categoryName"
            aria-label="Name"
            value={values.name}
            onChange={(e) => setValues((cur) => ({ ...cur, name: e.target.value }))}
            required
            maxLength={100}
          />
        </div>

        <div className="space-y-1.5">
          <Label htmlFor="categoryType">Type</Label>
          {mode === 'edit' ? (
            // CategoryType is server-immutable — see categories-api.ts comment on
            // UpdateCategoryRequest. The disabled UI is a hint; the structural
            // guarantee is the API DTO.
            <div
              id="categoryType"
              aria-label="Type"
              className="flex h-9 w-full items-center justify-between rounded-md border border-input bg-muted/40 px-3 text-sm"
            >
              <span>{selectedType?.name ?? '—'}</span>
              <Tooltip>
                <TooltipTrigger
                  render={
                    <button
                      type="button"
                      aria-label="About type immutability"
                      className="text-muted-foreground hover:text-foreground transition-colors"
                    >
                      <Info className="h-3.5 w-3.5" />
                    </button>
                  }
                />
                <TooltipContent className="max-w-xs">
                  {TYPE_LOCKED_TOOLTIP}
                </TooltipContent>
              </Tooltip>
            </div>
          ) : (
            <TypeCombobox
              id="categoryType"
              value={values.categoryTypeId}
              types={categoryTypes}
              onChange={(id) =>
                setValues((cur) => ({ ...cur, categoryTypeId: id }))
              }
            />
          )}
        </div>

        <div className="space-y-1.5">
          <Label htmlFor="lifestyleTag">Lifestyle tag</Label>
          <LifestyleCombobox
            id="lifestyleTag"
            value={values.lifestyleTag}
            renderTrigger={() => selectedLifestyleLabel}
            onChange={(v) => setValues((cur) => ({ ...cur, lifestyleTag: v }))}
          />
          <p className="text-xs text-muted-foreground">
            Optional. Used by the Expense Breakdown report.
          </p>
        </div>

        <div className="flex items-center gap-2 pt-2">
          <Button type="submit" disabled={!isDirty || submitting}>
            {submitting ? 'Saving…' : 'Save'}
          </Button>
          <Button
            type="button"
            variant="ghost"
            onClick={handleReset}
            disabled={!isDirty || submitting}
          >
            Reset
          </Button>
        </div>
      </form>
    </TooltipProvider>
  );
}

// ---------------------------------------------------------------------------
// Local pickers — Popover+Command (locked SPA idiom; no shadcn <Select>).
// ---------------------------------------------------------------------------

function TypeCombobox({
  id,
  value,
  types,
  onChange,
}: {
  id: string;
  value: number;
  types: CategoryTypeDto[];
  onChange: (id: number) => void;
}) {
  const [open, setOpen] = useState(false);
  const selected = types.find((t) => t.id === value);
  return (
    <Popover open={open} onOpenChange={setOpen}>
      <PopoverTrigger
        render={
          <Button
            id={id}
            type="button"
            variant="outline"
            role="combobox"
            aria-label="Type"
            aria-expanded={open}
            className="w-full justify-between"
          >
            <span>{selected?.name ?? 'Select type'}</span>
            <ChevronsUpDown className="ml-2 h-4 w-4 shrink-0 opacity-50" />
          </Button>
        }
      />
      <PopoverContent
        className="p-0 w-(--anchor-width) min-w-(--anchor-width)"
        align="start"
      >
        <Command>
          <CommandList>
            <CommandGroup>
              {types.map((t) => (
                <CommandItem
                  key={t.id}
                  value={t.name}
                  className="whitespace-nowrap"
                  onSelect={() => {
                    onChange(t.id);
                    setOpen(false);
                  }}
                >
                  <Check
                    className={cn(
                      'mr-2 h-4 w-4',
                      t.id === value ? 'opacity-100' : 'opacity-0',
                    )}
                  />
                  {t.name}
                </CommandItem>
              ))}
            </CommandGroup>
          </CommandList>
        </Command>
      </PopoverContent>
    </Popover>
  );
}

function LifestyleCombobox({
  id,
  value,
  renderTrigger,
  onChange,
}: {
  id: string;
  value: LifestyleTag | null;
  renderTrigger: () => string;
  onChange: (v: LifestyleTag | null) => void;
}) {
  const [open, setOpen] = useState(false);
  return (
    <Popover open={open} onOpenChange={setOpen}>
      <PopoverTrigger
        render={
          <Button
            id={id}
            type="button"
            variant="outline"
            role="combobox"
            aria-label="Lifestyle tag"
            aria-expanded={open}
            className="w-full justify-between"
          >
            <span>{renderTrigger()}</span>
            <ChevronsUpDown className="ml-2 h-4 w-4 shrink-0 opacity-50" />
          </Button>
        }
      />
      <PopoverContent
        className="p-0 w-(--anchor-width) min-w-(--anchor-width)"
        align="start"
      >
        <Command>
          <CommandList>
            <CommandGroup>
              {LIFESTYLE_OPTIONS.map((opt) => (
                <CommandItem
                  key={opt.label}
                  value={opt.label}
                  className="whitespace-nowrap"
                  onSelect={() => {
                    onChange(opt.value);
                    setOpen(false);
                  }}
                >
                  <Check
                    className={cn(
                      'mr-2 h-4 w-4',
                      opt.value === value ? 'opacity-100' : 'opacity-0',
                    )}
                  />
                  {opt.label}
                </CommandItem>
              ))}
            </CommandGroup>
          </CommandList>
        </Command>
      </PopoverContent>
    </Popover>
  );
}
```

- [ ] **Step 2: Run tests to verify they pass**

Run: `cd ProjectCeres.Client && pnpm test CategoryForm`
Expected: PASS — all 9 tests green.

- [ ] **Step 3: DO NOT COMMIT.**

---

## Task 8: CategoriesLayout — failing tests first

**Files:**
- Create: `ProjectCeres.Client/src/app/features/categories/CategoriesLayout.test.tsx`

The layout owns the GET, the URL state (tab/search/include-archived), and renders `CategoriesTable`. Tests use `vi.stubGlobal` and `MemoryRouter`.

- [ ] **Step 1: Write the test file**

```tsx
import { fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import { CategoriesLayout } from './CategoriesLayout';

vi.mock('sonner', () => ({
  toast: { success: vi.fn(), error: vi.fn() },
}));

const mockFetch = vi.fn();

const SYSTEM_ID = '20000000-0000-0000-0000-000000000001';
const UNCAT_INCOME_ID = '20000000-0000-0000-0000-000000000025';

const allRows = [
  // Income
  { id: 'i-1', name: 'Salary',    categoryTypeId: 1, categoryTypeName: 'Income',  lifestyleTag: null,    isActive: true,  isSystem: false },
  { id: 'i-2', name: 'Freelance', categoryTypeId: 1, categoryTypeName: 'Income',  lifestyleTag: null,    isActive: true,  isSystem: false },
  { id: SYSTEM_ID,                name: 'Opening Balance',     categoryTypeId: 1, categoryTypeName: 'Income',  lifestyleTag: null, isActive: true, isSystem: true },
  { id: UNCAT_INCOME_ID,          name: 'Uncategorized Income', categoryTypeId: 1, categoryTypeName: 'Income',  lifestyleTag: null, isActive: true, isSystem: false },
  // Expense
  { id: 'e-1', name: 'Groceries', categoryTypeId: 2, categoryTypeName: 'Expense', lifestyleTag: 'Needs', isActive: true,  isSystem: false },
  { id: 'e-2', name: 'Coffee',    categoryTypeId: 2, categoryTypeName: 'Expense', lifestyleTag: 'Wants', isActive: true,  isSystem: false },
  { id: 'e-3', name: 'Old',       categoryTypeId: 2, categoryTypeName: 'Expense', lifestyleTag: null,    isActive: false, isSystem: false },
];

beforeEach(() => {
  global.fetch = mockFetch as unknown as typeof fetch;
  mockFetch.mockImplementation((url: string) => {
    if (url === '/api/categories') {
      return Promise.resolve({
        ok: true,
        status: 200,
        json: async () => allRows.filter((r) => r.isActive),
      });
    }
    if (url === '/api/categories?includeInactive=true') {
      return Promise.resolve({ ok: true, status: 200, json: async () => allRows });
    }
    return Promise.resolve({ ok: false, status: 404, json: async () => null });
  });
});

afterEach(() => vi.resetAllMocks());

function renderAt(path: string) {
  return render(
    <MemoryRouter initialEntries={[path]}>
      <Routes>
        <Route path="/categories" element={<CategoriesLayout />}>
          <Route path="new" element={<div data-testid="new-page">NEW</div>} />
          <Route path=":id/edit" element={<div data-testid="edit-page">EDIT</div>} />
        </Route>
      </Routes>
    </MemoryRouter>,
  );
}

describe('CategoriesLayout', () => {
  it('renders skeleton while loading', () => {
    mockFetch.mockImplementation(() => new Promise(() => {}));
    renderAt('/categories');
    expect(screen.getByTestId('categories-skeleton')).toBeInTheDocument();
  });

  it('defaults to the Expense tab', async () => {
    renderAt('/categories');
    await screen.findByText('Groceries');
    expect(screen.queryByText('Salary')).toBeNull();
  });

  it('switching to Income tab updates the URL and shows Income rows', async () => {
    renderAt('/categories');
    await screen.findByText('Groceries');
    fireEvent.click(screen.getByRole('tab', { name: /income/i }));
    await screen.findByText('Salary');
    expect(screen.queryByText('Groceries')).toBeNull();
  });

  it('search filters rows in the active tab', async () => {
    renderAt('/categories');
    await screen.findByText('Groceries');
    fireEvent.change(screen.getByPlaceholderText(/filter categories/i), {
      target: { value: 'cof' },
    });
    await waitFor(() => {
      expect(screen.queryByText('Groceries')).toBeNull();
      expect(screen.getByText('Coffee')).toBeInTheDocument();
    });
  });

  it('shows search-empty state with Clear search link when no rows match', async () => {
    renderAt('/categories');
    await screen.findByText('Groceries');
    fireEvent.change(screen.getByPlaceholderText(/filter categories/i), {
      target: { value: 'zzznomatch' },
    });
    await waitFor(() => {
      expect(screen.getByText(/no categories match/i)).toBeInTheDocument();
    });
    expect(screen.getByRole('button', { name: /clear search/i })).toBeInTheDocument();
  });

  it('Include archived toggle adds archived rows', async () => {
    renderAt('/categories');
    await screen.findByText('Groceries');
    expect(screen.queryByText('Old')).toBeNull();
    fireEvent.click(screen.getByLabelText(/include archived/i));
    await screen.findByText('Old');
  });

  it('archived rows render the Archived badge and opacity treatment', async () => {
    renderAt('/categories?includeInactive=true');
    const oldRow = (await screen.findByText('Old')).closest('tr')!;
    expect(within(oldRow).getByText('Archived')).toBeInTheDocument();
    expect(oldRow.className).toContain('opacity-60');
  });

  it('system rows render the System badge and no row-actions menu', async () => {
    renderAt('/categories?type=income');
    const systemRow = (await screen.findByText('Opening Balance')).closest('tr')!;
    expect(within(systemRow).getByText('System')).toBeInTheDocument();
    expect(within(systemRow).queryByRole('button', { name: /row actions/i })).toBeNull();
  });

  it('reserved Uncategorized rows are treated as system (badge + no menu)', async () => {
    renderAt('/categories?type=income');
    const uncatRow = (await screen.findByText('Uncategorized Income')).closest('tr')!;
    expect(within(uncatRow).getByText('System')).toBeInTheDocument();
    expect(within(uncatRow).queryByRole('button', { name: /row actions/i })).toBeNull();
  });

  it('system rows sort to the bottom of their tab', async () => {
    renderAt('/categories?type=income');
    await screen.findByText('Opening Balance');
    const allRowsRendered = screen.getAllByRole('row');
    const names = allRowsRendered.slice(1).map((r) => within(r).getByRole('cell').textContent);
    // Header is row 0; data rows start at 1. The last two rows should be the system + reserved rows.
    const last = names[names.length - 1];
    const secondLast = names[names.length - 2];
    expect([last, secondLast]).toEqual(expect.arrayContaining(['Opening Balance', 'Uncategorized Income']));
  });

  it('GET error renders CardError with Retry', async () => {
    mockFetch.mockImplementation(() =>
      Promise.resolve({ ok: false, status: 500, json: async () => null }),
    );
    renderAt('/categories');
    await waitFor(() =>
      expect(screen.getByText(/Categories/)).toBeInTheDocument(),
    );
    expect(screen.getByRole('button', { name: /retry/i })).toBeInTheDocument();
  });

  it('clicking New category navigates to /categories/new', async () => {
    renderAt('/categories');
    await screen.findByText('Groceries');
    fireEvent.click(screen.getByRole('link', { name: /new category/i }));
    expect(await screen.findByTestId('new-page')).toBeInTheDocument();
  });
});
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `cd ProjectCeres.Client && pnpm test CategoriesLayout`
Expected: FAIL — `CategoriesLayout` not exported / module not found.

- [ ] **Step 3: DO NOT COMMIT.**

---

## Task 9: CategoriesLayout — implementation

**Files:**
- Create: `ProjectCeres.Client/src/app/features/categories/CategoriesLayout.tsx`

Page glue. Owns the data fetch, tabs, search input, archived toggle, and renders `CategoriesTable` with the filtered + sorted rows. The Outlet renders Create/Edit when those routes are active.

- [ ] **Step 1: Write the implementation**

```tsx
import { useEffect, useMemo, useRef, useState } from 'react';
import { Link, Outlet, useMatch, useSearchParams } from 'react-router-dom';
import { Plus } from 'lucide-react';
import { Button } from '@/components/ui/button';
import { Card, CardContent } from '@/components/ui/card';
import { Input } from '@/components/ui/input';
import { Label } from '@/components/ui/label';
import { Skeleton } from '@/components/ui/skeleton';
import { Switch } from '@/components/ui/switch';
import { Tabs, TabsList, TabsTrigger } from '@/components/ui/tabs';
import { CardError } from '../../components/CardError';
import { useApi } from '../../lib/use-api';
import { useDebounced } from '../../lib/use-debounced';
import { CategoriesTable } from './CategoriesTable';
import {
  buildListUrl,
  isLockedCategory,
  type CategoryListItemDto,
} from './categories-api';

type Tab = 'income' | 'expense';

function asTab(raw: string | null): Tab {
  return raw === 'income' ? 'income' : 'expense';
}

function tabToTypeName(tab: Tab): 'Income' | 'Expense' {
  return tab === 'income' ? 'Income' : 'Expense';
}

export function CategoriesLayout() {
  const [params, setParams] = useSearchParams();

  const onCreate = !!useMatch('/categories/new');
  const onEdit   = !!useMatch('/categories/:id/edit');
  const childActive = onCreate || onEdit;

  const tab = asTab(params.get('type'));
  const includeInactive = params.get('includeInactive') === 'true';
  const queryParam = params.get('q') ?? '';

  const headingRef = useRef<HTMLHeadingElement>(null);
  useEffect(() => { headingRef.current?.focus(); }, []);

  // Local search input + debounced URL push.
  const [searchInput, setSearchInput] = useState(queryParam);
  const debouncedSearch = useDebounced(searchInput, 200);

  useEffect(() => {
    const next = new URLSearchParams(params);
    if (debouncedSearch) next.set('q', debouncedSearch);
    else next.delete('q');
    setParams(next, { replace: true });
  }, [debouncedSearch]); // eslint-disable-line react-hooks/exhaustive-deps

  const list = useApi<CategoryListItemDto[]>(buildListUrl(includeInactive));

  // Outlet branch: create/edit mounts here.
  if (childActive) {
    return (
      <div className="mx-auto max-w-3xl space-y-6">
        <Outlet context={{ refetch: list.refetch }} />
      </div>
    );
  }

  function setTab(next: Tab) {
    const p = new URLSearchParams(params);
    p.set('type', next);
    setParams(p, { replace: true });
  }

  function setIncludeInactive(checked: boolean) {
    const p = new URLSearchParams(params);
    if (checked) p.set('includeInactive', 'true');
    else p.delete('includeInactive');
    setParams(p, { replace: true });
  }

  function clearSearch() {
    setSearchInput('');
  }

  return (
    <div className="mx-auto max-w-3xl space-y-6">
      <header>
        <h1
          ref={headingRef}
          tabIndex={-1}
          className="text-2xl font-semibold outline-none"
        >
          Categories
        </h1>
        <p className="mt-2 text-muted-foreground">
          Manage how transactions are classified. System categories used by
          imports and accounting cannot be edited.
        </p>
      </header>

      <Card>
        <CardContent className="pt-6 space-y-4">
          <div className="flex flex-col gap-3 sm:flex-row sm:items-end">
            <Tabs value={tab} onValueChange={(v) => setTab(asTab(v))}>
              <TabsList>
                <TabsTrigger value="income">Income</TabsTrigger>
                <TabsTrigger value="expense">Expense</TabsTrigger>
              </TabsList>
            </Tabs>
            <div className="flex-1 space-y-1.5">
              <Label htmlFor="cat-search" className="sr-only">Filter categories</Label>
              <Input
                id="cat-search"
                placeholder="Filter categories…"
                value={searchInput}
                onChange={(e) => setSearchInput(e.target.value)}
              />
            </div>
            <Button asChild>
              <Link to="new">
                <Plus className="h-4 w-4 mr-1" />
                New category
              </Link>
            </Button>
          </div>
          <div className="flex items-center gap-2">
            <Switch
              id="include-archived"
              checked={includeInactive}
              onCheckedChange={setIncludeInactive}
            />
            <Label htmlFor="include-archived" className="text-sm font-normal">
              Include archived
            </Label>
          </div>

          <CategoriesBody
            list={list}
            tab={tab}
            includeInactive={includeInactive}
            query={debouncedSearch}
            onClearSearch={clearSearch}
          />
        </CardContent>
      </Card>
    </div>
  );
}

function CategoriesBody({
  list,
  tab,
  includeInactive,
  query,
  onClearSearch,
}: {
  list: ReturnType<typeof useApi<CategoryListItemDto[]>>;
  tab: Tab;
  includeInactive: boolean;
  query: string;
  onClearSearch: () => void;
}) {
  if (list.loading) {
    return (
      <div data-testid="categories-skeleton" className="space-y-2 py-2">
        <Skeleton className="h-9 w-full" />
        <Skeleton className="h-9 w-full" />
        <Skeleton className="h-9 w-full" />
        <Skeleton className="h-9 w-full" />
      </div>
    );
  }

  if (list.error || !list.data) {
    return (
      <CardError section="Categories" onRetry={list.refetch} />
    );
  }

  const typeName = tabToTypeName(tab);
  const lower = query.toLowerCase();

  const filtered = useMemo(() => {
    return list.data!
      .filter((row) => row.categoryTypeName === typeName)
      .filter((row) => row.name.toLowerCase().includes(lower));
  }, [list.data, typeName, lower]);

  const sorted = useMemo(() => {
    // 1) Active user rows A-Z, 2) Archived user rows A-Z, 3) System rows A-Z.
    const userActive   = filtered.filter((r) => !isLockedCategory(r) && r.isActive)
                                 .sort((a, b) => a.name.localeCompare(b.name));
    const userArchived = filtered.filter((r) => !isLockedCategory(r) && !r.isActive)
                                 .sort((a, b) => a.name.localeCompare(b.name));
    const system       = filtered.filter((r) => isLockedCategory(r))
                                 .sort((a, b) => a.name.localeCompare(b.name));
    return [...userActive, ...userArchived, ...system];
  }, [filtered]);

  if (sorted.length === 0 && query) {
    return (
      <div className="px-3 py-8 text-center text-sm text-muted-foreground italic space-y-3">
        <p>No categories match '{query}'.</p>
        <Button type="button" variant="link" onClick={onClearSearch}>
          Clear search
        </Button>
      </div>
    );
  }

  const hasArchivedToggleNoArchivedRows =
    includeInactive && !sorted.some((r) => !r.isActive && !isLockedCategory(r));

  return (
    <div className="space-y-3">
      <CategoriesTable rows={sorted} onChanged={list.refetch} />
      {hasArchivedToggleNoArchivedRows ? (
        <p className="text-xs italic text-muted-foreground">
          No archived categories in this tab.
        </p>
      ) : null}
    </div>
  );
}
```

- [ ] **Step 2: Run tests to verify they pass**

Run: `cd ProjectCeres.Client && pnpm test CategoriesLayout`
Expected: PASS — all 12 tests green.

- [ ] **Step 3: DO NOT COMMIT.**

---

## Task 10: CategoryCreate — failing tests first

**Files:**
- Create: `ProjectCeres.Client/src/app/features/categories/CategoryCreate.test.tsx`

- [ ] **Step 1: Write the test file**

```tsx
import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import { toast } from 'sonner';
import { CategoryCreate } from './CategoryCreate';

vi.mock('sonner', () => ({
  toast: { success: vi.fn(), error: vi.fn() },
}));

const mockFetch = vi.fn();

const categoryTypesResponse = [
  { id: 1, name: 'Income' },
  { id: 2, name: 'Expense' },
];

beforeEach(() => {
  global.fetch = mockFetch as unknown as typeof fetch;
  mockFetch.mockImplementation((url: string, init?: RequestInit) => {
    if (url === '/api/category-types') {
      return Promise.resolve({ ok: true, status: 200, json: async () => categoryTypesResponse });
    }
    if (url === '/api/categories' && init?.method === 'POST') {
      return Promise.resolve({ ok: true, status: 201, json: async () => ({ id: 'new-1' }) });
    }
    return Promise.resolve({ ok: false, status: 404, json: async () => null });
  });
});

afterEach(() => vi.resetAllMocks());

function renderPage() {
  return render(
    <MemoryRouter initialEntries={['/categories/new']}>
      <Routes>
        <Route path="/categories" element={<div data-testid="list-page">LIST</div>} />
        <Route path="/categories/new" element={<CategoryCreate />} />
      </Routes>
    </MemoryRouter>,
  );
}

describe('CategoryCreate', () => {
  it('renders the form when category-types load', async () => {
    renderPage();
    await screen.findByLabelText(/name/i);
    expect(screen.getByLabelText(/type/i)).toBeInTheDocument();
  });

  it('POST 2xx fires toast.success and navigates back', async () => {
    renderPage();
    const nameInput = await screen.findByLabelText(/name/i);
    fireEvent.change(nameInput, { target: { value: 'Coffee' } });
    fireEvent.click(screen.getByRole('button', { name: /save/i }));
    await waitFor(() => expect(toast.success).toHaveBeenCalledWith('Created.'));
    expect(await screen.findByTestId('list-page')).toBeInTheDocument();
  });

  it('POST 422 fires toast.error and form retains values', async () => {
    mockFetch.mockImplementation((url: string, init?: RequestInit) => {
      if (url === '/api/category-types') {
        return Promise.resolve({ ok: true, status: 200, json: async () => categoryTypesResponse });
      }
      if (url === '/api/categories' && init?.method === 'POST') {
        return Promise.resolve({ ok: false, status: 422, json: async () => ({ error: { code: 'VALIDATION_ERROR', message: 'Bad', details: [] } }) });
      }
      return Promise.resolve({ ok: false, status: 404, json: async () => null });
    });
    renderPage();
    const nameInput = await screen.findByLabelText(/name/i);
    fireEvent.change(nameInput, { target: { value: 'Coffee' } });
    fireEvent.click(screen.getByRole('button', { name: /save/i }));
    await waitFor(() =>
      expect(toast.error).toHaveBeenCalledWith("Couldn't save. Try again."),
    );
    expect(screen.getByLabelText(/name/i)).toHaveValue('Coffee');
  });
});
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `cd ProjectCeres.Client && pnpm test CategoryCreate`
Expected: FAIL — `CategoryCreate` not exported.

- [ ] **Step 3: DO NOT COMMIT.**

---

## Task 11: CategoryCreate — implementation

**Files:**
- Create: `ProjectCeres.Client/src/app/features/categories/CategoryCreate.tsx`

- [ ] **Step 1: Write the implementation**

```tsx
import { toast } from 'sonner';
import { useNavigate, useOutletContext } from 'react-router-dom';
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card';
import { Skeleton } from '@/components/ui/skeleton';
import { useApi } from '../../lib/use-api';
import { CategoryForm } from './CategoryForm';
import {
  CATEGORIES_URL,
  CATEGORY_TYPES_URL,
  type CategoryFormValues,
  type CategoryTypeDto,
  type CreateCategoryRequest,
} from './categories-api';

type LayoutContext = { refetch: () => void };

const initialValues: CategoryFormValues = {
  name: '',
  categoryTypeId: 2,    // Expense default — most-frequent type.
  lifestyleTag: null,
};

export function CategoryCreate() {
  const navigate = useNavigate();
  const ctx = useOutletContext<LayoutContext>();
  const types = useApi<CategoryTypeDto[]>(CATEGORY_TYPES_URL);

  if (types.loading) {
    return (
      <Card>
        <CardHeader><CardTitle>New category</CardTitle></CardHeader>
        <CardContent className="space-y-4">
          <Skeleton className="h-9 w-full" />
          <Skeleton className="h-9 w-full" />
          <Skeleton className="h-9 w-full" />
        </CardContent>
      </Card>
    );
  }

  if (types.error || !types.data) {
    return (
      <Card>
        <CardHeader><CardTitle>New category</CardTitle></CardHeader>
        <CardContent>
          <div className="rounded-md border border-destructive/30 bg-destructive/10 px-4 py-3 text-sm text-destructive">
            Couldn't load category types.
          </div>
        </CardContent>
      </Card>
    );
  }

  async function handleSubmit(values: CategoryFormValues) {
    const body: CreateCategoryRequest = {
      name: values.name.trim(),
      categoryTypeId: values.categoryTypeId,
      lifestyleTag: values.lifestyleTag,
    };
    try {
      const response = await fetch(CATEGORIES_URL, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify(body),
      });
      if (!response.ok) {
        toast.error("Couldn't save. Try again.");
        return { ok: false as const };
      }
      toast.success('Created.');
      ctx?.refetch();
      navigate('/categories');
      return { ok: true as const };
    } catch {
      toast.error("Couldn't save. Try again.");
      return { ok: false as const };
    }
  }

  return (
    <Card>
      <CardHeader><CardTitle>New category</CardTitle></CardHeader>
      <CardContent>
        <CategoryForm
          mode="create"
          initialValues={initialValues}
          categoryTypes={types.data}
          onSubmit={handleSubmit}
        />
      </CardContent>
    </Card>
  );
}
```

- [ ] **Step 2: Run tests to verify they pass**

Run: `cd ProjectCeres.Client && pnpm test CategoryCreate`
Expected: PASS — all 3 tests green.

- [ ] **Step 3: DO NOT COMMIT.**

---

## Task 12: CategoryEdit — failing tests first

**Files:**
- Create: `ProjectCeres.Client/src/app/features/categories/CategoryEdit.test.tsx`

- [ ] **Step 1: Write the test file**

```tsx
import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import { toast } from 'sonner';
import { CategoryEdit } from './CategoryEdit';

vi.mock('sonner', () => ({
  toast: { success: vi.fn(), error: vi.fn() },
}));

const mockFetch = vi.fn();

const categoryDetail = {
  id: 'cat-1',
  name: 'Groceries',
  categoryTypeId: 2,
  categoryTypeName: 'Expense',
  lifestyleTag: 'Needs',
  isActive: true,
  isSystem: false,
};

const categoryTypesResponse = [
  { id: 1, name: 'Income' },
  { id: 2, name: 'Expense' },
];

beforeEach(() => {
  global.fetch = mockFetch as unknown as typeof fetch;
  mockFetch.mockImplementation((url: string, init?: RequestInit) => {
    if (url === '/api/category-types') {
      return Promise.resolve({ ok: true, status: 200, json: async () => categoryTypesResponse });
    }
    if (url === '/api/categories/cat-1' && (!init || init.method === undefined)) {
      return Promise.resolve({ ok: true, status: 200, json: async () => categoryDetail });
    }
    if (url === '/api/categories/cat-missing') {
      return Promise.resolve({ ok: false, status: 404, json: async () => null });
    }
    if (url === '/api/categories/cat-1' && init?.method === 'PATCH') {
      return Promise.resolve({ ok: true, status: 200, json: async () => categoryDetail });
    }
    return Promise.resolve({ ok: false, status: 404, json: async () => null });
  });
});

afterEach(() => vi.resetAllMocks());

function renderPage(path: string) {
  return render(
    <MemoryRouter initialEntries={[path]}>
      <Routes>
        <Route path="/categories" element={<div data-testid="list-page">LIST</div>} />
        <Route path="/categories/:id/edit" element={<CategoryEdit />} />
      </Routes>
    </MemoryRouter>,
  );
}

describe('CategoryEdit', () => {
  it('renders the form pre-populated with the category', async () => {
    renderPage('/categories/cat-1/edit');
    await waitFor(() => expect(screen.getByLabelText(/name/i)).toHaveValue('Groceries'));
  });

  it('GET 404 renders the not-found banner with a link back', async () => {
    renderPage('/categories/cat-missing/edit');
    await waitFor(() =>
      expect(screen.getByText(/that category doesn't exist/i)).toBeInTheDocument(),
    );
    expect(screen.getByRole('link', { name: /back to categories/i })).toBeInTheDocument();
  });

  it('PATCH success fires toast.success and navigates back', async () => {
    renderPage('/categories/cat-1/edit');
    await waitFor(() => expect(screen.getByLabelText(/name/i)).toHaveValue('Groceries'));
    fireEvent.change(screen.getByLabelText(/name/i), { target: { value: 'Food' } });
    fireEvent.click(screen.getByRole('button', { name: /save/i }));
    await waitFor(() => expect(toast.success).toHaveBeenCalledWith('Saved.'));
    expect(await screen.findByTestId('list-page')).toBeInTheDocument();
  });

  it('PATCH 422 fires toast.error and form retains values', async () => {
    mockFetch.mockImplementation((url: string, init?: RequestInit) => {
      if (url === '/api/category-types') {
        return Promise.resolve({ ok: true, status: 200, json: async () => categoryTypesResponse });
      }
      if (url === '/api/categories/cat-1' && (!init || init.method === undefined)) {
        return Promise.resolve({ ok: true, status: 200, json: async () => categoryDetail });
      }
      if (url === '/api/categories/cat-1' && init?.method === 'PATCH') {
        return Promise.resolve({ ok: false, status: 422, json: async () => ({ error: { code: 'VALIDATION_ERROR', message: 'Bad', details: [] } }) });
      }
      return Promise.resolve({ ok: false, status: 404, json: async () => null });
    });
    renderPage('/categories/cat-1/edit');
    await waitFor(() => expect(screen.getByLabelText(/name/i)).toHaveValue('Groceries'));
    fireEvent.change(screen.getByLabelText(/name/i), { target: { value: 'Food' } });
    fireEvent.click(screen.getByRole('button', { name: /save/i }));
    await waitFor(() =>
      expect(toast.error).toHaveBeenCalledWith("Couldn't save. Try again."),
    );
    expect(screen.getByLabelText(/name/i)).toHaveValue('Food');
  });
});
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `cd ProjectCeres.Client && pnpm test CategoryEdit`
Expected: FAIL — `CategoryEdit` not exported.

- [ ] **Step 3: DO NOT COMMIT.**

---

## Task 13: CategoryEdit — implementation

**Files:**
- Create: `ProjectCeres.Client/src/app/features/categories/CategoryEdit.tsx`

- [ ] **Step 1: Write the implementation**

```tsx
import { toast } from 'sonner';
import { Link, useNavigate, useOutletContext, useParams } from 'react-router-dom';
import { Button } from '@/components/ui/button';
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card';
import { Skeleton } from '@/components/ui/skeleton';
import { useApi } from '../../lib/use-api';
import { CategoryForm } from './CategoryForm';
import {
  CATEGORY_BY_ID_URL,
  CATEGORY_TYPES_URL,
  type CategoryDetailDto,
  type CategoryFormValues,
  type CategoryTypeDto,
  type UpdateCategoryRequest,
} from './categories-api';

type LayoutContext = { refetch: () => void };

export function CategoryEdit() {
  const { id } = useParams<{ id: string }>();
  const navigate = useNavigate();
  const ctx = useOutletContext<LayoutContext>();

  const detail = useApi<CategoryDetailDto>(id ? CATEGORY_BY_ID_URL(id) : '/api/categories/__missing__');
  const types = useApi<CategoryTypeDto[]>(CATEGORY_TYPES_URL);

  if (detail.loading || types.loading) {
    return (
      <Card>
        <CardHeader><CardTitle>Edit category</CardTitle></CardHeader>
        <CardContent className="space-y-4">
          <Skeleton className="h-9 w-full" />
          <Skeleton className="h-9 w-full" />
          <Skeleton className="h-9 w-full" />
        </CardContent>
      </Card>
    );
  }

  if (detail.error || !detail.data) {
    return (
      <Card>
        <CardHeader><CardTitle>Edit category</CardTitle></CardHeader>
        <CardContent>
          <div className="rounded-md border border-destructive/30 bg-destructive/10 px-4 py-3 text-sm text-destructive">
            That category doesn't exist.
          </div>
          <Button asChild variant="outline" className="mt-4">
            <Link to="/categories">Back to Categories</Link>
          </Button>
        </CardContent>
      </Card>
    );
  }

  if (types.error || !types.data) {
    return (
      <Card>
        <CardHeader><CardTitle>Edit category</CardTitle></CardHeader>
        <CardContent>
          <div className="rounded-md border border-destructive/30 bg-destructive/10 px-4 py-3 text-sm text-destructive">
            Couldn't load category types.
          </div>
        </CardContent>
      </Card>
    );
  }

  const initialValues: CategoryFormValues = {
    name: detail.data.name,
    categoryTypeId: detail.data.categoryTypeId,
    lifestyleTag: detail.data.lifestyleTag,
  };

  async function handleSubmit(values: CategoryFormValues) {
    const body: UpdateCategoryRequest = {
      name: values.name.trim(),
      lifestyleTag: values.lifestyleTag,
    };
    try {
      const response = await fetch(CATEGORY_BY_ID_URL(id!), {
        method: 'PATCH',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify(body),
      });
      if (!response.ok) {
        toast.error("Couldn't save. Try again.");
        return { ok: false as const };
      }
      toast.success('Saved.');
      ctx?.refetch();
      navigate('/categories');
      return { ok: true as const };
    } catch {
      toast.error("Couldn't save. Try again.");
      return { ok: false as const };
    }
  }

  return (
    <Card>
      <CardHeader><CardTitle>Edit category</CardTitle></CardHeader>
      <CardContent>
        <CategoryForm
          mode="edit"
          initialValues={initialValues}
          categoryTypes={types.data}
          onSubmit={handleSubmit}
        />
      </CardContent>
    </Card>
  );
}
```

- [ ] **Step 2: Run tests to verify they pass**

Run: `cd ProjectCeres.Client && pnpm test CategoryEdit`
Expected: PASS — all 4 tests green.

- [ ] **Step 3: DO NOT COMMIT.**

---

## Task 14: Wire the route + Categories.tsx re-export

**Files:**
- Modify: `ProjectCeres.Client/src/app/pages/Categories.tsx`
- Modify: `ProjectCeres.Client/src/app/App.tsx`

- [ ] **Step 1: Replace pages/Categories.tsx with a one-line re-export**

Open `ProjectCeres.Client/src/app/pages/Categories.tsx`, replace its entire contents with:

```typescript
export { CategoriesLayout as Categories } from '../features/categories/CategoriesLayout';
```

- [ ] **Step 2: Add the new imports + nested routes in App.tsx**

Open `ProjectCeres.Client/src/app/App.tsx`. Add these imports near the other feature imports:

```typescript
import { CategoryCreate } from './features/categories/CategoryCreate';
import { CategoryEdit } from './features/categories/CategoryEdit';
```

Then change the `<Route path="categories" …>` line. The current line is:

```tsx
        <Route path="categories" element={<Categories />} />
```

Replace it with:

```tsx
        <Route path="categories" element={<Categories />}>
          <Route path="new" element={<CategoryCreate />} />
          <Route path=":id/edit" element={<CategoryEdit />} />
        </Route>
```

- [ ] **Step 3: Verify the SPA still type-checks**

Run: `cd ProjectCeres.Client && pnpm tsc --noEmit`
Expected: no errors.

- [ ] **Step 4: Run the full client test suite**

Run: `cd ProjectCeres.Client && pnpm test`
Expected: PASS — all tests including the new ~40 categories tests plus all pre-existing client tests.

- [ ] **Step 5: DO NOT COMMIT.**

---

## Task 15: Razor cutover — slim CategoriesController to redirects

**Files:**
- Modify: `ProjectCeres/Controllers/CategoriesController.cs`

- [ ] **Step 1: Replace the file contents**

Open `ProjectCeres/Controllers/CategoriesController.cs` and replace its entire contents with:

```csharp
using Microsoft.AspNetCore.Mvc;

namespace ProjectCeres.Controllers;

/// <summary>
/// Razor categories UI is replaced by the SPA at /app/categories. This
/// controller exists only to 302-redirect any in-flight bookmarks. Use 302
/// (not 301) so browsers don't aggressively cache during the SPA migration
/// window. The redirect is removed entirely in the final SPA-cutover
/// cleanup batch.
/// </summary>
public class CategoriesController : Controller
{
    public IActionResult Index() => Redirect("/app/categories");
    public IActionResult Create() => Redirect("/app/categories/new");
    public IActionResult Edit(Guid id) => Redirect($"/app/categories/{id}/edit");
    public IActionResult Deactivate(Guid id) => Redirect("/app/categories");
}
```

- [ ] **Step 2: Verify the build catches the dangling references that Task 16 will fix**

Run: `dotnet build ProjectCeres/ProjectCeres.csproj --nologo -v quiet`
Expected: PASS or FAIL. Either is acceptable — it depends on whether `CategoryService.CreateAsync(CategoryCreateViewModel)` is referenced anywhere outside this controller.

If it fails with errors in `CategoryService.cs` or `ICategoryService.cs` referencing `CategoryCreateViewModel` / `CategoryEditViewModel`, that's the expected handoff to Tasks 16 + 17.

- [ ] **Step 3: DO NOT COMMIT.**

---

## Task 16: Razor cutover — delete views + ViewModels

**Files:**
- Delete: `ProjectCeres/Views/Categories/Index.cshtml`
- Delete: `ProjectCeres/Views/Categories/Create.cshtml`
- Delete: `ProjectCeres/Views/Categories/Edit.cshtml`
- Delete: `ProjectCeres/Views/Categories/Deactivate.cshtml`
- Delete: `ProjectCeres/ViewModels/CategoryCreateViewModel.cs`
- Delete: `ProjectCeres/ViewModels/CategoryEditViewModel.cs`

- [ ] **Step 1: Delete the files**

Run from the repo root:

```bash
git rm ProjectCeres/Views/Categories/Index.cshtml \
       ProjectCeres/Views/Categories/Create.cshtml \
       ProjectCeres/Views/Categories/Edit.cshtml \
       ProjectCeres/Views/Categories/Deactivate.cshtml \
       ProjectCeres/ViewModels/CategoryCreateViewModel.cs \
       ProjectCeres/ViewModels/CategoryEditViewModel.cs
```

This stages the deletions. Do NOT commit.

- [ ] **Step 2: Verify the build now fails on dangling service references**

Run: `dotnet build ProjectCeres/ProjectCeres.csproj --nologo -v quiet`
Expected: FAIL — `CategoryService.CreateAsync(CategoryCreateViewModel)`, `CategoryService.UpdateAsync(CategoryEditViewModel)`, and the `ICategoryService` declarations reference the deleted types.

- [ ] **Step 3: DO NOT COMMIT.**

---

## Task 17: Razor cutover — remove throwing methods from CategoryService

**Files:**
- Modify: `ProjectCeres/Services/ICategoryService.cs`
- Modify: `ProjectCeres/Services/CategoryService.cs`

- [ ] **Step 1: Remove the interface methods**

Open `ProjectCeres/Services/ICategoryService.cs`. Delete these three method declarations and any preceding XML doc comments:

```csharp
Task<Category> CreateAsync(CategoryCreateViewModel vm);
Task UpdateAsync(CategoryEditViewModel vm);
Task DeactivateAsync(Guid id);
```

Keep:
- `Task<IEnumerable<Category>> GetAllAsync(bool includeInactive = false);`
- `Task<Category?> GetByIdAsync(Guid id);`
- `Task<Result<Category>> TryCreateAsync(CreateCategoryRequest request);`
- `Task<Result<Category>> TryUpdateAsync(Guid id, UpdateCategoryRequest request);`
- `Task<Result> TryDeactivateAsync(Guid id);`

If `using ProjectCeres.ViewModels;` becomes dead (no other type from that namespace is referenced in the file — verify by reading the file), remove the using.

- [ ] **Step 2: Remove the implementation methods**

Open `ProjectCeres/Services/CategoryService.cs`. Delete the three methods:

- `public async Task<Category> CreateAsync(CategoryCreateViewModel vm)` — full method body.
- `public async Task UpdateAsync(CategoryEditViewModel vm)` — full method body.
- `public async Task DeactivateAsync(Guid id)` — full method body. (Note: the `Try*` versions stay.)

If the `using ProjectCeres.ViewModels;` becomes dead, remove it. Leave it if `CreateCategoryRequest` / `UpdateCategoryRequest` are imported via that namespace — verify by reading the file.

- [ ] **Step 3: Verify the project builds**

Run: `dotnet build ProjectCeres/ProjectCeres.csproj --nologo -v quiet`
Expected: PASS — no errors.

- [ ] **Step 4: DO NOT COMMIT.**

---

## Task 18: Delete the Razor-only test methods

**Files:**
- Modify: `ProjectCeres.Tests/Integration/CategoryServiceTests.cs`

The current file contains these test methods (verified 2026-05-02):

1. `CreateAsync_PersistsCategory` — exercises throwing `CreateAsync(vm)`. Delete.
2. `UpdateAsync_SystemCategory_Throws` — exercises throwing `UpdateAsync(vm)`. Delete.
3. `DeactivateAsync_SystemCategory_Throws` — exercises throwing `DeactivateAsync(id)`. Delete.
4. `DeactivateAsync_NonSystemCategory_SetsIsActiveFalse` — exercises throwing `DeactivateAsync(id)`. Delete.
5. `UpdateAsync_UncategorizedIncomeCategory_Throws` — exercises throwing `UpdateAsync(vm)`. Delete.
6. `DeactivateAsync_UncategorizedExpenseCategory_Throws` — exercises throwing `DeactivateAsync(id)`. Delete.

All six tests cover behaviour that's already covered at the API level by `CategoriesCrudApiTests` (verified — see the `Patch_rejects_*`, `Archive_rejects_*`, `Post_creates_category` tests there).

- [ ] **Step 1: Read the current file**

Run: `cat ProjectCeres.Tests/Integration/CategoryServiceTests.cs`

Confirm the test methods listed above are all present. If any other test methods exist (e.g. that exercise `GetAllAsync`, `GetByIdAsync`, or the `Try*` methods directly), keep those.

- [ ] **Step 2: Delete the six test methods**

Open `ProjectCeres.Tests/Integration/CategoryServiceTests.cs`. Delete the six methods listed in Step 1, including any preceding section comments (e.g. `// CreateAsync` / `// UpdateAsync` / `// DeactivateAsync`).

If after the deletions there are no remaining test methods in the file, **delete the file entirely** with `git rm ProjectCeres.Tests/Integration/CategoryServiceTests.cs`.

If `using ProjectCeres.ViewModels;` becomes dead (the file no longer references `CategoryCreateViewModel` or `CategoryEditViewModel`), remove the using.

- [ ] **Step 3: Build the test project**

Run: `dotnet build ProjectCeres.Tests/ProjectCeres.Tests.csproj --nologo -v quiet`
Expected: PASS — no errors.

- [ ] **Step 4: Run the full server test suite**

Run: `dotnet test --nologo`
Expected: PASS — every test in the suite passes.

- [ ] **Step 5: DO NOT COMMIT.**

---

## Task 19: Verification gate — full build + tests + manual

This is the gate before commit. Both stacks must be green and a manual click-through must work.

- [ ] **Step 1: Full server test suite**

Run: `dotnet test --nologo`
Expected: PASS — every test green. (Settings/Categories cutover should leave the server-side test count unchanged or down by 6 tests; either is correct.)

- [ ] **Step 2: Full client test suite**

Run: `cd ProjectCeres.Client && pnpm test`
Expected: PASS — every test green, including ~42 new categories tests.

- [ ] **Step 3: Client production build**

Run: `cd ProjectCeres.Client && pnpm build`
Expected: PASS.

- [ ] **Step 4: Manual click-through**

Start the app:

```bash
dotnet run --project ProjectCeres
```

In a browser:

1. Visit `https://localhost:7001/app/categories`. Confirm the Expense tab is active by default and shows skeleton then real rows.
2. Click the Income tab. Confirm `?type=income` appears in the URL and Income rows render.
3. Type `gro` in the filter input. Confirm rows filter to "Groceries". Clear the input. Confirm rows return.
4. Toggle "Include archived". Confirm the URL gains `?includeInactive=true`. Confirm the hint text appears (no archived rows in seed data).
5. Hover the System badge on the Opening Balance row. Confirm the tooltip shows the exact text: `"Created by the system. These are required for imports and accounting and cannot be edited or archived."`
6. Confirm there is no `⋯` menu on system rows or on Uncategorized Income/Expense rows.
7. Click `⋯ → Edit` on a user row (e.g. "Groceries"). Confirm navigation to `/app/categories/<guid>/edit`. Confirm the form pre-populates. Change the name to "Food", click Save, confirm the toast and return to the list.
8. Click "New category" in the filter bar. Confirm navigation to `/app/categories/new`. Pick Type = Expense, Name = "Test Category", LifestyleTag = "Wants". Click Save. Confirm the toast and return to the list.
9. Click `⋯ → Archive…` on the new "Test Category" row. Confirm the AlertDialog title says "Archive 'Test Category'?". Click Archive. Confirm the toast and the row disappears from the active list.
10. Toggle "Include archived". Confirm the just-archived row reappears with the Archived badge and reduced opacity. Confirm only "Edit" appears in its `⋯` menu (no Archive item).
11. To test the 409 error path, archive a category that has a transaction referencing it (e.g. add a transaction with category "Food" first via `/app/movements/new`, then attempt to archive "Food"). Confirm the toast says: `"This category has transactions. Reassign them before archiving."`
12. Visit `https://localhost:7001/Categories`. Confirm 302 → `/app/categories`. Same for `/Categories/Create`, `/Categories/Edit/<guid>`, `/Categories/Deactivate/<guid>`.

If any step fails, stop and fix before committing.

- [ ] **Step 5: DO NOT COMMIT (yet).**

---

## Task 20: Commit

This is the only commit step.

- [ ] **Step 1: Stage and commit**

Run:

```bash
git add ProjectCeres.Client/src/app/features/categories \
        ProjectCeres.Client/src/app/pages/Categories.tsx \
        ProjectCeres.Client/src/app/App.tsx \
        ProjectCeres/Controllers/CategoriesController.cs \
        ProjectCeres/Services/ICategoryService.cs \
        ProjectCeres/Services/CategoryService.cs \
        ProjectCeres.Tests/Integration/CategoryServiceTests.cs

# git rm already staged the deletions in Task 16 + (possibly) Task 18; verify:
git status --short
```

Expected `git status --short` (deletions show as `D`, additions and modifications also visible):

```
A  ProjectCeres.Client/src/app/features/categories/CategoriesLayout.test.tsx
A  ProjectCeres.Client/src/app/features/categories/CategoriesLayout.tsx
A  ProjectCeres.Client/src/app/features/categories/CategoriesTable.test.tsx
A  ProjectCeres.Client/src/app/features/categories/CategoriesTable.tsx
A  ProjectCeres.Client/src/app/features/categories/CategoryCreate.test.tsx
A  ProjectCeres.Client/src/app/features/categories/CategoryCreate.tsx
A  ProjectCeres.Client/src/app/features/categories/CategoryEdit.test.tsx
A  ProjectCeres.Client/src/app/features/categories/CategoryEdit.tsx
A  ProjectCeres.Client/src/app/features/categories/CategoryForm.test.tsx
A  ProjectCeres.Client/src/app/features/categories/CategoryForm.tsx
A  ProjectCeres.Client/src/app/features/categories/CategoryRowMenu.test.tsx
A  ProjectCeres.Client/src/app/features/categories/CategoryRowMenu.tsx
A  ProjectCeres.Client/src/app/features/categories/categories-api.ts
M  ProjectCeres.Client/src/app/App.tsx
M  ProjectCeres.Client/src/app/pages/Categories.tsx
M  ProjectCeres.Tests/Integration/CategoryServiceTests.cs       # OR D if entire file deleted
M  ProjectCeres/Controllers/CategoriesController.cs
M  ProjectCeres/Services/CategoryService.cs
M  ProjectCeres/Services/ICategoryService.cs
D  ProjectCeres/ViewModels/CategoryCreateViewModel.cs
D  ProjectCeres/ViewModels/CategoryEditViewModel.cs
D  ProjectCeres/Views/Categories/Create.cshtml
D  ProjectCeres/Views/Categories/Deactivate.cshtml
D  ProjectCeres/Views/Categories/Edit.cshtml
D  ProjectCeres/Views/Categories/Index.cshtml
```

If anything else is staged that you didn't expect (e.g. `launchSettings.json` from a previous click-through), unstage it: `git restore --staged <path>`.

Then commit:

```bash
git commit -m "$(cat <<'EOF'
feat(spa): Categories page + Razor cutover

Replaces the /app/categories placeholder with a real search-first
list-and-form page (Tabs Income/Expense, debounced filter input,
Include archived toggle, AlertDialog-confirmed archive flow with 409
in-use error surfaced in the toast). Slims the Razor
CategoriesController to four 302 redirects.

New pieces:
- features/categories/{categories-api.ts, CategoriesLayout.tsx,
  CategoriesTable.tsx, CategoryRowMenu.tsx, CategoryForm.tsx,
  CategoryCreate.tsx, CategoryEdit.tsx} + co-located Vitest tests
- Nested routes /categories/new and /categories/:id/edit in App.tsx
- pages/Categories.tsx is a one-line re-export of CategoriesLayout

Locked design choices applied (tabs over stacked tables, persistent
debounced search, system rows always visible with tooltip-bearing
badge sorted to the bottom of their tab, Popover+Command for type +
lifestyle pickers, no shadcn Select). CategoryType field on Edit is
read-only — both as a UX hint and structurally because
UpdateCategoryRequest does not bind CategoryTypeId.

Razor cutover:
- Views/Categories/{Index,Create,Edit,Deactivate}.cshtml: deleted
- ViewModels/CategoryCreateViewModel.cs, CategoryEditViewModel.cs: deleted
- CategoriesController slimmed to 4 redirects (302, not 301 — per
  migration doc §8)
- ICategoryService.{CreateAsync,UpdateAsync,DeactivateAsync}(...) and
  their implementations: deleted (covered at API level by
  CategoriesCrudApiTests.{Post_creates,Patch_*,Archive_*})
- CategoryServiceTests.cs Razor-only tests: deleted

Tests: ~42 new client tests across 7 test files. Full client and
server suites green.

Spec: docs/superpowers/specs/2026-05-02-categories-spa-design.md
Plan: docs/superpowers/plans/2026-05-02-categories-spa.md
EOF
)"
```

- [ ] **Step 2: Verify the commit landed cleanly**

Run: `git log -1 --stat`
Expected: shows the new files added under `features/categories/`, the modifications to `App.tsx`/`pages/Categories.tsx`/`CategoriesController.cs`/`ICategoryService.cs`/`CategoryService.cs`/`CategoryServiceTests.cs`, and the deletions of all four `Views/Categories/*.cshtml` plus both ViewModel files.

---

## Self-review (completed inline)

**Spec coverage:**
- File structure → Tasks 1, 3, 5, 7, 9, 11, 13.
- Routing changes → Task 14.
- Locked decisions (tabs, search, archived toggle, system badge, sort order) → Tasks 8, 9.
- CategoryType server-immutability → categories-api.ts comment (Task 1) + CategoryForm Edit branch (Task 7) + UpdateCategoryRequest payload omission (Task 13).
- Archive flow with 409 handling → Tasks 4, 5.
- Form fields (Name, Type, LifestyleTag, "None" first-class) → Tasks 6, 7.
- Save / Reset (no Cancel) → Task 7.
- Empty states (search empty, archived hint, no-categories) → Task 9.
- Loading / error states → Tasks 9, 11, 13.
- Razor cutover → Tasks 15, 16, 17, 18.
- Verification gate → Task 19.
- Single commit → Task 20.

**Placeholder scan:** No "TBD" / "TODO". Every code block is complete and runnable.

**Type consistency:**
- `CategoryListItemDto`, `CategoryDetailDto`, `CategoryFormValues`, `CategoryTypeDto`, `CreateCategoryRequest`, `UpdateCategoryRequest`, `LifestyleTag`, `ApiErrorEnvelope` defined in Task 1 and used consistently in Tasks 2, 4, 6, 8, 10, 12.
- `isLockedCategory` and `RESERVED_UNCATEGORIZED_IDS` defined in Task 1 and used in Tasks 3, 9.
- `SubmitResult` (`{ ok: true } | { ok: false }`) defined in Task 7, returned by Tasks 11 and 13's `handleSubmit`.
- `LayoutContext` (`{ refetch: () => void }`) defined in Tasks 11 and 13. The provider in Task 9 uses the same shape.
- Field labels in tests (`/name/i`, `/type/i`, `/lifestyle/i`) match the `<Label>` text + `aria-label` in the implementation.

**Spec deviation noted:** spec mentions `<ConfirmDialog>` but the project's actual SPA pattern (per `BudgetRowMenu.tsx`) is `<AlertDialog>`. The plan uses AlertDialog. Documented at the top of the file structure section.

No contradictions; the plan is internally consistent.
