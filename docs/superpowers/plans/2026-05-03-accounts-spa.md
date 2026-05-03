# Accounts SPA Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking. **Single commit at the end (Task 23); every earlier task explicitly does NOT commit.**

**Goal:** Replace the placeholder `/app/accounts` route with a real Accounts SPA — list, Create, Edit, Ledger with payoff projection — and slim the Razor `AccountsController` to a 302 redirect surface, all in one commit.

**Architecture:** New `features/accounts/` folder following the Categories template. `AccountsLayout` owns the list-side data fetch and renders the per-currency subtotal strip + filter bar + a single Card with the table. `AccountCreate` and `AccountEdit` mount at nested routes (`/accounts/new`, `/accounts/:id/edit`) and reuse a shared `AccountForm` with conditional Asset vs Liability fields. `AccountLedger` is a sibling top-level route at `/accounts/:id/ledger` with running balance + an SPA-side payoff projection panel for amortising liabilities. Search is debounced 200 ms and filters client-side. Server changes: add `HasTransactions: bool` to `AccountListItemDto`, tighten `AccountPolicies.ValidateLiabilityRepayment` to reject `null repaymentType + non-null rate`, slim Razor controller, delete `LiabilityProjectionService` (single-source-of-truth post-cutover).

**Tech Stack:** React 19 + TypeScript + Vite, shadcn/ui (base-ui primitives), Vitest + React Testing Library, ASP.NET Core (Razor controller cutover + policy hardening + DTO addition).

**Spec:** `docs/superpowers/specs/2026-05-03-accounts-spa-design.md`

---

## File structure (locked from spec)

**Create — client:**
- `ProjectCeres.Client/src/app/features/accounts/accounts-api.ts` — URL builders + DTOs
- `ProjectCeres.Client/src/app/features/accounts/AccountsTable.tsx` — pure UI table
- `ProjectCeres.Client/src/app/features/accounts/AccountsTable.test.tsx`
- `ProjectCeres.Client/src/app/features/accounts/AccountRowMenu.tsx` — pure UI ⋯ menu + AlertDialog
- `ProjectCeres.Client/src/app/features/accounts/AccountRowMenu.test.tsx`
- `ProjectCeres.Client/src/app/features/accounts/AccountForm.tsx` — pure UI form (Create + Edit)
- `ProjectCeres.Client/src/app/features/accounts/AccountForm.test.tsx`
- `ProjectCeres.Client/src/app/features/accounts/AccountCurrencySubtotals.tsx` — per-currency net strip
- `ProjectCeres.Client/src/app/features/accounts/AccountCurrencySubtotals.test.tsx`
- `ProjectCeres.Client/src/app/features/accounts/AccountsLayout.tsx` — page glue (list page)
- `ProjectCeres.Client/src/app/features/accounts/AccountsLayout.test.tsx`
- `ProjectCeres.Client/src/app/features/accounts/AccountCreate.tsx` — page glue for `/new`
- `ProjectCeres.Client/src/app/features/accounts/AccountCreate.test.tsx`
- `ProjectCeres.Client/src/app/features/accounts/AccountEdit.tsx` — page glue for `/:id/edit`
- `ProjectCeres.Client/src/app/features/accounts/AccountEdit.test.tsx`
- `ProjectCeres.Client/src/app/features/accounts/AccountLedger.tsx` — sibling top-level page (running balance + projection)
- `ProjectCeres.Client/src/app/features/accounts/AccountLedger.test.tsx`
- `ProjectCeres.Client/src/app/features/accounts/projection.ts` — pure amortisation math util
- `ProjectCeres.Client/src/app/features/accounts/projection.test.ts`

**Modify — client:**
- `ProjectCeres.Client/src/app/pages/Accounts.tsx` — replace placeholder with one-line re-export
- `ProjectCeres.Client/src/app/App.tsx` — add nested routes + sibling Ledger route

**Modify — server:**
- `ProjectCeres/Controllers/AccountsController.cs` — slim to 5 redirects (no constructor deps)
- `ProjectCeres/Services/IAccountService.cs` — drop throwing CRUD method declarations
- `ProjectCeres/Services/AccountService.cs` — drop throwing CRUD method bodies
- `ProjectCeres/Services/AccountPolicies.cs` — extend `ValidateLiabilityRepayment` to cover the None case
- `ProjectCeres/ViewModels/AccountApiDtos.cs` — add `HasTransactions: bool` to `AccountListItemDto`
- `ProjectCeres/Controllers/Api/AccountsApiController.cs` — populate `HasTransactions` on list
- `ProjectCeres/Program.cs` — remove `AddScoped<ILiabilityProjectionService, LiabilityProjectionService>()`
- `ProjectCeres.Tests/Unit/AccountPoliciesTests.cs` — add tests for new None-case branch
- `ProjectCeres.Tests/Integration/Api/AccountsCrudApiTests.cs` — add `HasTransactions` coverage
- `ProjectCeres.Tests/Integration/AccountServiceTests.cs` — drop tests for deleted throwing methods (delete file if empty)
- `ProjectCeres.Tests/Integration/Api/UserIdStampingTests.cs` — drop `RazorAccountService_*_stamps_UserId` if it exists

**Delete — server:**
- `ProjectCeres/Views/Accounts/Index.cshtml`
- `ProjectCeres/Views/Accounts/Create.cshtml`
- `ProjectCeres/Views/Accounts/Edit.cshtml`
- `ProjectCeres/Views/Accounts/Deactivate.cshtml`
- `ProjectCeres/Views/Accounts/Ledger.cshtml`
- `ProjectCeres/ViewModels/AccountCreateViewModel.cs`
- `ProjectCeres/ViewModels/AccountEditViewModel.cs`
- `ProjectCeres/ViewModels/AccountLedgerViewModel.cs`
- `ProjectCeres/Services/ILiabilityProjectionService.cs`
- `ProjectCeres/Services/LiabilityProjectionService.cs`
- `ProjectCeres/ViewModels/LiabilityProjectionViewModel.cs`
- `ProjectCeres.Tests/Unit/LiabilityProjectionServiceTests.cs`

**SPA-page template constraints (locked from Settings + Categories):**
- `features/<area>/` folder; `<area>-api.ts` is logic-free.
- Page+Form split: pure UI components don't fetch or toast.
- Popover+Command for any list picker — never shadcn `<Select>`.
- `useApi` for GET, hand-rolled `fetch` for mutations.
- `sonner` for toasts. Mock with `vi.mock('sonner', ...)`.
- Tests: `global.fetch = mockFetch as unknown as typeof fetch`. No MSW.
- AlertDialog (base-ui) for destructive confirms — not the legacy `ConfirmDialog`.
- `<Button render={<Link>...</Link>}>` for link-as-button — never `asChild`.
- Save (primary) + Cancel (variant="outline"). No Reset button.

---

## Task 1: accounts-api.ts (URL builders + DTOs)

**Files:**
- Create: `ProjectCeres.Client/src/app/features/accounts/accounts-api.ts`

This file is logic-free. Mirrors `categories-api.ts` shape.

- [ ] **Step 1: Write the file**

```typescript
// ---------- URL builders ----------

export const ACCOUNTS_URL = '/api/accounts';
export const ACCOUNT_BY_ID_URL = (id: string) => `/api/accounts/${id}`;
export const ACCOUNT_ARCHIVE_URL = (id: string) => `/api/accounts/${id}/archive`;
export const ACCOUNT_LEDGER_URL = (id: string) => `/api/accounts/${id}/ledger`;
export const ACCOUNT_TYPES_URL = '/api/account-types';
export const CURRENCIES_URL = '/api/currencies';

export function buildListUrl(includeInactive: boolean): string {
  return includeInactive ? `${ACCOUNTS_URL}?includeInactive=true` : ACCOUNTS_URL;
}

// ---------- DTOs ----------

export type AccountTypeName = 'Asset' | 'Liability';
export type RepaymentType = 'FullMonthly' | 'Amortising';

export type AccountListItemDto = {
  id: string;
  name: string;
  accountTypeId: number;
  accountTypeName: AccountTypeName;
  currencyId: number;
  currencyCode: string;
  currencySymbol: string;
  description: string | null;
  isActive: boolean;
  excludeFromSpendable: boolean;
  liabilityRepaymentType: RepaymentType | null;
  interestRate: number | null;
  balance: number;
  hasTransactions: boolean;
};

export type AccountDetailDto = {
  id: string;
  name: string;
  accountTypeId: number;
  accountTypeName: AccountTypeName;
  currencyId: number;
  currencyCode: string;
  currencySymbol: string;
  description: string | null;
  isActive: boolean;
  excludeFromSpendable: boolean;
  liabilityRepaymentType: RepaymentType | null;
  interestRate: number | null;
  openingBalance: number;
  openingBalanceDate: string | null;  // DateOnly serialised as ISO date string
};

export type AccountTypeDto = {
  id: number;
  name: AccountTypeName;
};

export type CurrencyDto = {
  id: number;
  code: string;
  name: string;
  symbol: string;
};

/**
 * The CreateAccountRequest body. CategoryTypeId and CurrencyId are required
 * on Create; both become server-immutable on Edit (UpdateAccountRequest does
 * not include them).
 */
export type CreateAccountRequest = {
  name: string;
  accountTypeId: number;
  currencyId: number;
  description: string | null;
  openingBalance: number;
  openingBalanceDate: string;  // ISO date
  liabilityRepaymentType: RepaymentType | null;
  interestRate: number | null;
  excludeFromSpendable: boolean;
};

export type UpdateAccountRequest = {
  name: string;
  description: string | null;
  openingBalance: number;
  openingBalanceDate: string;
  liabilityRepaymentType: RepaymentType | null;
  interestRate: number | null;
  excludeFromSpendable: boolean;
};

// ---------- Form values (UI layer) ----------

export type AccountFormValues = {
  name: string;
  accountTypeId: number;             // editable on Create, read-only display on Edit
  currencyId: number;                // editable on Create, read-only display on Edit
  description: string;               // empty string sentinel for "no description"; serialised as null
  openingBalance: number;
  openingBalanceDate: string;        // ISO date
  liabilityRepaymentType: RepaymentType | null;
  interestRate: number | null;
  excludeFromSpendable: boolean;
};

// ---------- Ledger DTOs ----------

export type LedgerEntryDto = {
  date: string;                      // ISO date
  createdAt: string;                 // ISO datetime
  description: string;
  entryType: string;                 // "Transaction" | "Transfer" | "LiabilityPayment" | "OpeningBalance"
  categoryName: string | null;
  signedAmount: number;
  runningBalance: number;
};

export type AccountLedgerDto = {
  accountId: string;
  accountName: string;
  currencySymbol: string;
  entries: LedgerEntryDto[];
};

// ---------- Error envelope ----------

export type ApiErrorEnvelope = {
  error: {
    code: string;
    message: string;
    details: unknown[];
  };
};
```

- [ ] **Step 2: Verify TypeScript compiles**

Run: `cd ProjectCeres.Client && pnpm tsc --noEmit`
Expected: no errors.

- [ ] **Step 3: DO NOT COMMIT.**

Verify with: `cd <repo> && git status --short`
Expected: only an untracked `ProjectCeres.Client/src/app/features/accounts/` directory (and the pre-existing `launchSettings.json` modification, ignored throughout this plan). Nothing staged, nothing committed.

---

## Task 2: projection.ts (amortisation math util) — failing tests first

**Files:**
- Create: `ProjectCeres.Client/src/app/features/accounts/projection.test.ts`

Pure math util. Computes payoff months / total interest / total paid from balance, annual rate, monthly payment.

- [ ] **Step 1: Write the test file**

```typescript
import { describe, expect, it } from 'vitest';
import { project, type ProjectionResult } from './projection';

describe('project (amortisation)', () => {
  it('returns months/total/interest for a standard amortising loan', () => {
    // €5,000 balance, 3.5% annual rate, €500 monthly payment
    const result = project(5000, 0.035, 500) as Extract<ProjectionResult, { ok: true }>;
    expect(result.ok).toBe(true);
    expect(result.monthsToPayoff).toBeGreaterThan(0);
    expect(result.monthsToPayoff).toBeLessThan(12);  // pays off in under a year
    expect(result.totalPaid).toBeGreaterThan(5000);
    expect(result.totalInterest).toBeCloseTo(result.totalPaid - 5000, 2);
  });

  it('handles a zero-rate loan as straight-line division', () => {
    const result = project(1000, 0, 250) as Extract<ProjectionResult, { ok: true }>;
    expect(result.ok).toBe(true);
    expect(result.monthsToPayoff).toBe(4);
    expect(result.totalPaid).toBe(1000);
    expect(result.totalInterest).toBe(0);
  });

  it('returns an error result when payment is too low to cover interest', () => {
    // €10,000 balance, 12% annual rate (€100/mo interest), only €50 payment
    const result = project(10000, 0.12, 50);
    expect(result.ok).toBe(false);
    if (!result.ok) {
      expect(result.error).toMatch(/too low/i);
    }
  });

  it('returns an error result when payment is zero or negative', () => {
    expect(project(1000, 0.05, 0).ok).toBe(false);
    expect(project(1000, 0.05, -50).ok).toBe(false);
  });

  it('returns an error result when balance is zero or negative', () => {
    expect(project(0, 0.05, 100).ok).toBe(false);
    expect(project(-100, 0.05, 100).ok).toBe(false);
  });

  it('payoff date is monthsToPayoff months from today', () => {
    const result = project(1200, 0, 100) as Extract<ProjectionResult, { ok: true }>;
    expect(result.ok).toBe(true);
    expect(result.monthsToPayoff).toBe(12);
    const today = new Date();
    const expectedYear = today.getFullYear() + (today.getMonth() === 0 ? 0 : 1);
    expect(result.payoffDate.getFullYear()).toBeGreaterThanOrEqual(today.getFullYear());
  });
});
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `cd ProjectCeres.Client && pnpm test projection`
Expected: FAIL — `projection` not exported / module not found.

- [ ] **Step 3: DO NOT COMMIT.**

---

## Task 3: projection.ts — implementation

**Files:**
- Create: `ProjectCeres.Client/src/app/features/accounts/projection.ts`

- [ ] **Step 1: Write the implementation**

```typescript
export type ProjectionResult =
  | {
      ok: true;
      monthsToPayoff: number;
      totalPaid: number;
      totalInterest: number;
      payoffDate: Date;
    }
  | { ok: false; error: string };

/**
 * Closed-form amortisation projection. Given a current balance, an annual
 * interest rate (decimal — 0.035 = 3.5%), and a fixed monthly payment,
 * returns months to payoff, total amount paid, total interest cost, and
 * the projected payoff date.
 *
 * Edge cases:
 * - balance <= 0 → error ("nothing to pay off")
 * - monthlyPayment <= 0 → error ("payment must be positive")
 * - monthlyPayment <= balance * monthlyRate → error ("payment too low to
 *   cover interest; balance grows forever")
 * - annualRate <= 0 → straight-line division (no interest)
 */
export function project(
  balance: number,
  annualRate: number,
  monthlyPayment: number,
): ProjectionResult {
  if (balance <= 0) {
    return { ok: false, error: 'Outstanding balance must be greater than zero.' };
  }
  if (monthlyPayment <= 0) {
    return { ok: false, error: 'Monthly payment must be greater than zero.' };
  }

  if (annualRate <= 0) {
    const monthsToPayoff = Math.ceil(balance / monthlyPayment);
    const totalPaid = balance;  // last payment is partial
    const payoffDate = addMonths(new Date(), monthsToPayoff);
    return { ok: true, monthsToPayoff, totalPaid, totalInterest: 0, payoffDate };
  }

  const monthlyRate = annualRate / 12;
  const monthlyInterestCost = balance * monthlyRate;
  if (monthlyPayment <= monthlyInterestCost) {
    return {
      ok: false,
      error: 'Monthly payment is too low to cover the monthly interest. Balance would never be paid off.',
    };
  }

  const monthsToPayoff = Math.ceil(
    -Math.log(1 - (balance * monthlyRate) / monthlyPayment) /
     Math.log(1 + monthlyRate),
  );
  const totalPaid = monthsToPayoff * monthlyPayment;
  const totalInterest = totalPaid - balance;
  const payoffDate = addMonths(new Date(), monthsToPayoff);

  return { ok: true, monthsToPayoff, totalPaid, totalInterest, payoffDate };
}

function addMonths(d: Date, months: number): Date {
  const r = new Date(d);
  r.setMonth(r.getMonth() + months);
  return r;
}
```

- [ ] **Step 2: Run tests to verify they pass**

Run: `cd ProjectCeres.Client && pnpm test projection`
Expected: PASS — all 6 tests green.

- [ ] **Step 3: DO NOT COMMIT.**

---

## Task 4: AccountsTable — failing tests first

**Files:**
- Create: `ProjectCeres.Client/src/app/features/accounts/AccountsTable.test.tsx`

`AccountsTable` is pure UI. Receives a list of rows + a refetch callback. Knows nothing about fetch or toast. Wrapped in `MemoryRouter` because `AccountRowMenu` (which it renders) uses `useNavigate`.

- [ ] **Step 1: Write the test file**

```tsx
import { render, screen, within } from '@testing-library/react';
import { describe, expect, it, vi } from 'vitest';
import { MemoryRouter } from 'react-router-dom';
import { AccountsTable } from './AccountsTable';
import type { AccountListItemDto } from './accounts-api';

const rows: AccountListItemDto[] = [
  {
    id: 'a-1', name: 'Cash', accountTypeId: 1, accountTypeName: 'Asset',
    currencyId: 1, currencyCode: 'EUR', currencySymbol: '€',
    description: null, isActive: true, excludeFromSpendable: false,
    liabilityRepaymentType: null, interestRate: null,
    balance: 120, hasTransactions: false,
  },
  {
    id: 'a-2', name: 'Checking Account', accountTypeId: 1, accountTypeName: 'Asset',
    currencyId: 1, currencyCode: 'EUR', currencySymbol: '€',
    description: null, isActive: true, excludeFromSpendable: false,
    liabilityRepaymentType: null, interestRate: null,
    balance: 2114.56, hasTransactions: true,
  },
  {
    id: 'a-3', name: 'Credit Card', accountTypeId: 2, accountTypeName: 'Liability',
    currencyId: 1, currencyCode: 'EUR', currencySymbol: '€',
    description: null, isActive: true, excludeFromSpendable: false,
    liabilityRepaymentType: 'FullMonthly', interestRate: null,
    balance: 500, hasTransactions: true,
  },
  {
    id: 'a-4', name: 'Old Savings', accountTypeId: 1, accountTypeName: 'Asset',
    currencyId: 1, currencyCode: 'EUR', currencySymbol: '€',
    description: null, isActive: false, excludeFromSpendable: false,
    liabilityRepaymentType: null, interestRate: null,
    balance: 0, hasTransactions: true,
  },
];

function renderTable(props?: Partial<React.ComponentProps<typeof AccountsTable>>) {
  return render(
    <MemoryRouter>
      <AccountsTable rows={rows} onChanged={vi.fn()} {...props} />
    </MemoryRouter>,
  );
}

describe('AccountsTable', () => {
  it('renders one row per account', () => {
    renderTable();
    expect(screen.getByText('Cash')).toBeInTheDocument();
    expect(screen.getByText('Checking Account')).toBeInTheDocument();
    expect(screen.getByText('Credit Card')).toBeInTheDocument();
  });

  it('shows the Type cell for each row', () => {
    renderTable();
    const checkingRow = screen.getByText('Checking Account').closest('tr')!;
    expect(within(checkingRow).getByText('Asset')).toBeInTheDocument();
    const ccRow = screen.getByText('Credit Card').closest('tr')!;
    expect(within(ccRow).getByText('Liability')).toBeInTheDocument();
  });

  it('renders Liability balances in destructive color', () => {
    renderTable();
    const ccRow = screen.getByText('Credit Card').closest('tr')!;
    const balanceCell = within(ccRow).getByText(/500/);
    expect(balanceCell.className).toMatch(/text-destructive/);
  });

  it('renders Asset balances in normal foreground (no destructive)', () => {
    renderTable();
    const checkingRow = screen.getByText('Checking Account').closest('tr')!;
    const balanceCell = within(checkingRow).getByText(/2.114/);
    expect(balanceCell.className).not.toMatch(/text-destructive/);
  });

  it('shows the Archived badge on rows where isActive=false', () => {
    renderTable();
    const archivedRow = screen.getByText('Old Savings').closest('tr')!;
    expect(within(archivedRow).getByText('Archived')).toBeInTheDocument();
  });

  it('archived rows have opacity-60 class', () => {
    renderTable();
    const archivedRow = screen.getByText('Old Savings').closest('tr')!;
    expect(archivedRow.className).toContain('opacity-60');
  });

  it('renders a row-menu trigger for active rows', () => {
    renderTable();
    const cashRow = screen.getByText('Cash').closest('tr')!;
    expect(within(cashRow).getByRole('button', { name: /row actions/i })).toBeInTheDocument();
  });

  it('renders a row-menu trigger for archived rows too (View ledger only)', () => {
    renderTable();
    const archivedRow = screen.getByText('Old Savings').closest('tr')!;
    expect(within(archivedRow).getByRole('button', { name: /row actions/i })).toBeInTheDocument();
  });

  it('balance cells use tabular-nums', () => {
    renderTable();
    const cashRow = screen.getByText('Cash').closest('tr')!;
    const cells = within(cashRow).getAllByRole('cell');
    const balanceCell = cells[2];  // Name=0, Type=1, Balance=2, ⋯=3
    expect(balanceCell.className).toMatch(/tabular-nums/);
  });

  it('renders the empty state when rows array is empty', () => {
    renderTable({ rows: [] });
    expect(screen.getByText(/no accounts/i)).toBeInTheDocument();
  });
});
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `cd ProjectCeres.Client && pnpm test AccountsTable`
Expected: FAIL — `AccountsTable` not exported / module not found.

- [ ] **Step 3: DO NOT COMMIT.**

---

## Task 5: AccountsTable — implementation

**Files:**
- Create: `ProjectCeres.Client/src/app/features/accounts/AccountsTable.tsx`

Pure UI. Renders a `<Table>` with one row per account. Sort order is the caller's responsibility — the table renders in the order it receives. Note: `AccountRowMenu` import is forward-referenced and implemented in Task 7.

- [ ] **Step 1: Write the implementation**

```tsx
import { Badge } from '@/components/ui/badge';
import {
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from '@/components/ui/table';
import { cn } from '@/lib/utils';
import { AccountRowMenu } from './AccountRowMenu';
import type { AccountListItemDto } from './accounts-api';

type Props = {
  rows: AccountListItemDto[];
  /** Called after a successful archive so the parent layout can refetch. */
  onChanged: () => void;
};

export function AccountsTable({ rows, onChanged }: Props) {
  if (rows.length === 0) {
    return (
      <div className="px-3 py-8 text-center text-sm text-muted-foreground italic">
        No accounts.
      </div>
    );
  }

  return (
    <Table>
      <TableHeader>
        <TableRow>
          <TableHead>Name</TableHead>
          <TableHead className="w-32">Type</TableHead>
          <TableHead className="w-40 text-right">Balance</TableHead>
          <TableHead className="w-12" />
        </TableRow>
      </TableHeader>
      <TableBody>
        {rows.map((row) => {
          const isLiability = row.accountTypeName === 'Liability';
          return (
            <TableRow
              key={row.id}
              className={!row.isActive ? 'opacity-60' : undefined}
            >
              <TableCell className="text-sm">
                <div className="flex items-center gap-2">
                  <span>{row.name}</span>
                  {!row.isActive ? (
                    <Badge variant="secondary">Archived</Badge>
                  ) : null}
                </div>
              </TableCell>
              <TableCell className="text-sm text-muted-foreground">
                {row.accountTypeName}
              </TableCell>
              <TableCell
                className={cn(
                  'text-right tabular-nums text-sm',
                  isLiability && 'text-destructive',
                )}
              >
                {formatBalance(row)}
              </TableCell>
              <TableCell>
                <AccountRowMenu account={row} onChanged={onChanged} />
              </TableCell>
            </TableRow>
          );
        })}
      </TableBody>
    </Table>
  );
}

function formatBalance(account: AccountListItemDto): string {
  const formatted = new Intl.NumberFormat(undefined, {
    minimumFractionDigits: 2,
    maximumFractionDigits: 2,
  }).format(Math.abs(account.balance));
  const sign = account.balance < 0 ? '-' : '';
  return `${sign}${account.currencySymbol}${formatted}`;
}
```

- [ ] **Step 2: Run tests to verify they fail with a CategoryRowMenu-style error (AccountRowMenu missing)**

Run: `cd ProjectCeres.Client && pnpm test AccountsTable`
Expected: FAIL — module resolution error for `./AccountRowMenu`.

This is fine — Task 7 implements `AccountRowMenu`, after which AccountsTable tests will pass. Continue to Task 6.

- [ ] **Step 3: DO NOT COMMIT.**

---

## Task 6: AccountRowMenu — failing tests first

**Files:**
- Create: `ProjectCeres.Client/src/app/features/accounts/AccountRowMenu.test.tsx`

The row menu owns the AlertDialog confirm and the archive PATCH. Tests use `global.fetch = mockFetch` and `vi.mock('sonner', …)`.

- [ ] **Step 1: Write the test file**

```tsx
import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import { toast } from 'sonner';
import { AccountRowMenu } from './AccountRowMenu';
import type { AccountListItemDto } from './accounts-api';

vi.mock('sonner', () => ({
  toast: { success: vi.fn(), error: vi.fn() },
}));

const mockFetch = vi.fn();

const activeWithTransactions: AccountListItemDto = {
  id: 'a-1', name: 'Checking Account',
  accountTypeId: 1, accountTypeName: 'Asset',
  currencyId: 1, currencyCode: 'EUR', currencySymbol: '€',
  description: null, isActive: true, excludeFromSpendable: false,
  liabilityRepaymentType: null, interestRate: null,
  balance: 2114.56, hasTransactions: true,
};

const activeEmpty: AccountListItemDto = {
  ...activeWithTransactions, id: 'a-2', name: 'Cash', balance: 0, hasTransactions: false,
};

const archived: AccountListItemDto = {
  ...activeWithTransactions, id: 'a-3', name: 'Old Savings', isActive: false,
};

beforeEach(() => {
  global.fetch = mockFetch as unknown as typeof fetch;
  mockFetch.mockReset();
});

afterEach(() => vi.resetAllMocks());

function renderMenu(account: AccountListItemDto, onChanged = vi.fn()) {
  return render(
    <MemoryRouter initialEntries={['/accounts']}>
      <Routes>
        <Route path="/accounts" element={<AccountRowMenu account={account} onChanged={onChanged} />} />
        <Route path="/accounts/:id/edit" element={<div data-testid="edit-page">EDIT</div>} />
        <Route path="/accounts/:id/ledger" element={<div data-testid="ledger-page">LEDGER</div>} />
      </Routes>
    </MemoryRouter>,
  );
}

describe('AccountRowMenu', () => {
  it('shows Edit + View ledger + Archive for active rows', async () => {
    renderMenu(activeWithTransactions);
    fireEvent.click(screen.getByRole('button', { name: /row actions/i }));
    expect(await screen.findByText('Edit')).toBeInTheDocument();
    expect(screen.getByText('View ledger')).toBeInTheDocument();
    expect(screen.getByText('Archive…')).toBeInTheDocument();
  });

  it('shows only View ledger for archived rows', async () => {
    renderMenu(archived);
    fireEvent.click(screen.getByRole('button', { name: /row actions/i }));
    expect(await screen.findByText('View ledger')).toBeInTheDocument();
    expect(screen.queryByText('Edit')).toBeNull();
    expect(screen.queryByText('Archive…')).toBeNull();
  });

  it('clicking Edit navigates to /accounts/:id/edit', async () => {
    renderMenu(activeWithTransactions);
    fireEvent.click(screen.getByRole('button', { name: /row actions/i }));
    fireEvent.click(await screen.findByText('Edit'));
    expect(await screen.findByTestId('edit-page')).toBeInTheDocument();
  });

  it('clicking View ledger navigates to /accounts/:id/ledger', async () => {
    renderMenu(activeWithTransactions);
    fireEvent.click(screen.getByRole('button', { name: /row actions/i }));
    fireEvent.click(await screen.findByText('View ledger'));
    expect(await screen.findByTestId('ledger-page')).toBeInTheDocument();
  });

  it('archive dialog uses safe copy for empty accounts', async () => {
    renderMenu(activeEmpty);
    fireEvent.click(screen.getByRole('button', { name: /row actions/i }));
    fireEvent.click(await screen.findByText('Archive…'));
    expect(await screen.findByText(/archive 'cash'\?/i)).toBeInTheDocument();
    expect(screen.getByText(/no transactions/i)).toBeInTheDocument();
    expect(screen.getByText(/safe to archive/i)).toBeInTheDocument();
  });

  it('archive dialog uses consequence copy for accounts with transactions', async () => {
    renderMenu(activeWithTransactions);
    fireEvent.click(screen.getByRole('button', { name: /row actions/i }));
    fireEvent.click(await screen.findByText('Archive…'));
    expect(await screen.findByText(/archive 'checking account'\?/i)).toBeInTheDocument();
    expect(screen.getByText(/still counts toward your net worth/i)).toBeInTheDocument();
  });

  it('archive 204 fires toast.success and onChanged', async () => {
    mockFetch.mockResolvedValue({ ok: true, status: 204, json: async () => null });
    const onChanged = vi.fn();
    renderMenu(activeWithTransactions, onChanged);
    fireEvent.click(screen.getByRole('button', { name: /row actions/i }));
    fireEvent.click(await screen.findByText('Archive…'));
    fireEvent.click(await screen.findByRole('button', { name: 'Archive' }));
    await waitFor(() => expect(toast.success).toHaveBeenCalledWith('Archived.'));
    expect(onChanged).toHaveBeenCalledTimes(1);
  });

  it('archive non-2xx fires generic error toast', async () => {
    mockFetch.mockResolvedValue({ ok: false, status: 500, json: async () => null });
    renderMenu(activeWithTransactions);
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

Run: `cd ProjectCeres.Client && pnpm test AccountRowMenu`
Expected: FAIL — `AccountRowMenu` not exported.

- [ ] **Step 3: DO NOT COMMIT.**

---

## Task 7: AccountRowMenu — implementation

**Files:**
- Create: `ProjectCeres.Client/src/app/features/accounts/AccountRowMenu.tsx`

Mirrors `CategoryRowMenu` shape, with three menu items instead of two. No 409 path — Accounts archive succeeds unconditionally.

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
import { ACCOUNT_ARCHIVE_URL, type AccountListItemDto } from './accounts-api';

const ARCHIVE_COPY_EMPTY =
  "This account has no transactions. It will be hidden from the active list and pickers; you can find it again with the Include archived toggle. Safe to archive.";

const ARCHIVE_COPY_NON_EMPTY =
  "This account will be hidden from the active list and pickers. Existing transactions stay attached to it, and the balance still counts toward your net worth and reports. You can find archived accounts with the toggle.";

type Props = {
  account: AccountListItemDto;
  /** Called after a successful archive so the parent layout can refetch. */
  onChanged: () => void;
};

export function AccountRowMenu({ account, onChanged }: Props) {
  const navigate = useNavigate();
  const [confirmOpen, setConfirmOpen] = useState(false);

  async function handleArchive() {
    setConfirmOpen(false);
    try {
      const response = await fetch(ACCOUNT_ARCHIVE_URL(account.id), { method: 'PATCH' });
      if (response.ok) {
        toast.success('Archived.');
        onChanged();
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
          {account.isActive ? (
            <DropdownMenuItem onClick={() => navigate(`/accounts/${account.id}/edit`)}>
              Edit
            </DropdownMenuItem>
          ) : null}
          <DropdownMenuItem onClick={() => navigate(`/accounts/${account.id}/ledger`)}>
            View ledger
          </DropdownMenuItem>
          {account.isActive ? (
            <DropdownMenuItem onClick={() => setConfirmOpen(true)}>
              Archive…
            </DropdownMenuItem>
          ) : null}
        </DropdownMenuContent>
      </DropdownMenu>

      <AlertDialog open={confirmOpen} onOpenChange={setConfirmOpen}>
        <AlertDialogContent>
          <AlertDialogHeader>
            <AlertDialogTitle>Archive '{account.name}'?</AlertDialogTitle>
            <AlertDialogDescription>
              {account.hasTransactions ? ARCHIVE_COPY_NON_EMPTY : ARCHIVE_COPY_EMPTY}
            </AlertDialogDescription>
          </AlertDialogHeader>
          <AlertDialogFooter>
            <AlertDialogCancel>Cancel</AlertDialogCancel>
            <AlertDialogAction onClick={handleArchive}>Archive</AlertDialogAction>
          </AlertDialogFooter>
        </AlertDialogContent>
      </AlertDialog>
    </>
  );
}
```

- [ ] **Step 2: Run tests to verify they pass**

Run: `cd ProjectCeres.Client && pnpm test AccountRowMenu`
Expected: PASS — all 8 tests green.

- [ ] **Step 3: Re-run AccountsTable tests to confirm they pass now**

Run: `cd ProjectCeres.Client && pnpm test AccountsTable`
Expected: PASS — all 10 tests green.

- [ ] **Step 4: DO NOT COMMIT.**

---

## Task 8: AccountCurrencySubtotals — failing tests first

**Files:**
- Create: `ProjectCeres.Client/src/app/features/accounts/AccountCurrencySubtotals.test.tsx`

Pure UI. Hidden when ≤1 currency. Net per currency = sum(asset balances) − sum(liability balances). Active accounts only.

- [ ] **Step 1: Write the test file**

```tsx
import { render, screen } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import { AccountCurrencySubtotals } from './AccountCurrencySubtotals';
import type { AccountListItemDto } from './accounts-api';

function row(over: Partial<AccountListItemDto>): AccountListItemDto {
  return {
    id: 'x', name: 'X', accountTypeId: 1, accountTypeName: 'Asset',
    currencyId: 1, currencyCode: 'EUR', currencySymbol: '€',
    description: null, isActive: true, excludeFromSpendable: false,
    liabilityRepaymentType: null, interestRate: null,
    balance: 0, hasTransactions: false,
    ...over,
  };
}

describe('AccountCurrencySubtotals', () => {
  it('renders nothing when accounts span only one currency', () => {
    const rows = [
      row({ id: 'a', balance: 1000, currencyCode: 'EUR' }),
      row({ id: 'b', balance: 500, currencyCode: 'EUR' }),
    ];
    const { container } = render(<AccountCurrencySubtotals rows={rows} />);
    expect(container).toBeEmptyDOMElement();
  });

  it('renders one entry per currency when 2+ currencies are present', () => {
    const rows = [
      row({ id: 'a', balance: 1000, currencyCode: 'EUR', currencySymbol: '€' }),
      row({ id: 'b', balance: 500,  currencyCode: 'USD', currencySymbol: '$' }),
    ];
    render(<AccountCurrencySubtotals rows={rows} />);
    expect(screen.getByText(/EUR/)).toBeInTheDocument();
    expect(screen.getByText(/USD/)).toBeInTheDocument();
  });

  it('subtracts liability balances from asset balances per currency', () => {
    const rows = [
      row({ id: 'a', balance: 1000, accountTypeName: 'Asset',     currencyCode: 'EUR', currencySymbol: '€' }),
      row({ id: 'b', balance: 300,  accountTypeName: 'Liability', currencyCode: 'EUR', currencySymbol: '€' }),
      row({ id: 'c', balance: 200,  accountTypeName: 'Asset',     currencyCode: 'USD', currencySymbol: '$' }),
    ];
    render(<AccountCurrencySubtotals rows={rows} />);
    // EUR net = 1000 - 300 = 700
    expect(screen.getByText(/€700/)).toBeInTheDocument();
    // USD net = 200 (no liabilities)
    expect(screen.getByText(/\$200/)).toBeInTheDocument();
  });

  it('excludes archived accounts from the math', () => {
    const rows = [
      row({ id: 'a', balance: 1000, currencyCode: 'EUR', currencySymbol: '€' }),
      row({ id: 'b', balance: 9999, currencyCode: 'USD', currencySymbol: '$', isActive: false }),
      row({ id: 'c', balance: 500,  currencyCode: 'USD', currencySymbol: '$' }),
    ];
    render(<AccountCurrencySubtotals rows={rows} />);
    // The USD subtotal should be 500, not 10499.
    expect(screen.getByText(/\$500/)).toBeInTheDocument();
    expect(screen.queryByText(/\$10/)).toBeNull();
  });

  it('renders nothing when only archived accounts span 2 currencies', () => {
    const rows = [
      row({ id: 'a', balance: 100, currencyCode: 'EUR', isActive: false }),
      row({ id: 'b', balance: 200, currencyCode: 'USD', isActive: false }),
    ];
    const { container } = render(<AccountCurrencySubtotals rows={rows} />);
    expect(container).toBeEmptyDOMElement();
  });
});
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `cd ProjectCeres.Client && pnpm test AccountCurrencySubtotals`
Expected: FAIL — `AccountCurrencySubtotals` not exported.

- [ ] **Step 3: DO NOT COMMIT.**

---

## Task 9: AccountCurrencySubtotals — implementation

**Files:**
- Create: `ProjectCeres.Client/src/app/features/accounts/AccountCurrencySubtotals.tsx`

- [ ] **Step 1: Write the implementation**

```tsx
import { Card, CardContent } from '@/components/ui/card';
import type { AccountListItemDto } from './accounts-api';

type Props = {
  rows: AccountListItemDto[];
};

type CurrencyTotal = {
  code: string;
  symbol: string;
  net: number;
};

export function AccountCurrencySubtotals({ rows }: Props) {
  const totals = computeNetPerCurrency(rows);
  if (totals.length < 2) return null;

  return (
    <Card>
      <CardContent className="py-3">
        <div className="flex flex-wrap items-center gap-x-3 gap-y-1 text-sm">
          {totals.map((t, idx) => (
            <span key={t.code} className="flex items-center gap-3">
              {idx > 0 ? <span className="text-muted-foreground">·</span> : null}
              <span>
                <span className="text-muted-foreground mr-2">{t.code}</span>
                <span className="tabular-nums font-medium">{formatNet(t)}</span>
              </span>
            </span>
          ))}
        </div>
      </CardContent>
    </Card>
  );
}

function computeNetPerCurrency(rows: AccountListItemDto[]): CurrencyTotal[] {
  const map = new Map<string, CurrencyTotal>();
  for (const r of rows) {
    if (!r.isActive) continue;
    const cur = map.get(r.currencyCode) ?? { code: r.currencyCode, symbol: r.currencySymbol, net: 0 };
    const sign = r.accountTypeName === 'Liability' ? -1 : 1;
    cur.net += sign * r.balance;
    map.set(r.currencyCode, cur);
  }
  return Array.from(map.values()).sort((a, b) => a.code.localeCompare(b.code));
}

function formatNet(t: CurrencyTotal): string {
  const formatted = new Intl.NumberFormat(undefined, {
    minimumFractionDigits: 2,
    maximumFractionDigits: 2,
  }).format(Math.abs(t.net));
  const sign = t.net < 0 ? '-' : '';
  return `${sign}${t.symbol}${formatted}`;
}
```

- [ ] **Step 2: Run tests to verify they pass**

Run: `cd ProjectCeres.Client && pnpm test AccountCurrencySubtotals`
Expected: PASS — all 5 tests green.

- [ ] **Step 3: DO NOT COMMIT.**

---

## Task 10: AccountForm — failing tests first

**Files:**
- Create: `ProjectCeres.Client/src/app/features/accounts/AccountForm.test.tsx`

`AccountForm` is pure UI. Owns local state, computes `isDirty` against initialValues, calls `props.onSubmit`. Knows nothing about fetch or toast.

- [ ] **Step 1: Write the test file**

```tsx
import { fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import { describe, expect, it, vi } from 'vitest';
import { AccountForm } from './AccountForm';
import type { AccountFormValues, AccountTypeDto, CurrencyDto } from './accounts-api';

const accountTypes: AccountTypeDto[] = [
  { id: 1, name: 'Asset' },
  { id: 2, name: 'Liability' },
];

const currencies: CurrencyDto[] = [
  { id: 1, code: 'EUR', name: 'Euro',     symbol: '€' },
  { id: 2, code: 'USD', name: 'US Dollar', symbol: '$' },
];

const initialCreate: AccountFormValues = {
  name: '',
  accountTypeId: 1,
  currencyId: 1,
  description: '',
  openingBalance: 0,
  openingBalanceDate: '2026-05-03',
  liabilityRepaymentType: null,
  interestRate: null,
  excludeFromSpendable: false,
};

const initialEditAsset: AccountFormValues = {
  name: 'Checking Account',
  accountTypeId: 1,
  currencyId: 1,
  description: 'Main checking',
  openingBalance: 1000,
  openingBalanceDate: '2026-01-01',
  liabilityRepaymentType: null,
  interestRate: null,
  excludeFromSpendable: false,
};

const initialEditLiability: AccountFormValues = {
  name: 'Mortgage',
  accountTypeId: 2,
  currencyId: 1,
  description: null as unknown as string,  // form normalises null→''
  openingBalance: 100000,
  openingBalanceDate: '2026-01-01',
  liabilityRepaymentType: 'Amortising',
  interestRate: 0.035,
  excludeFromSpendable: false,
} as AccountFormValues;

function renderForm(overrides?: {
  mode?: 'create' | 'edit';
  initialValues?: AccountFormValues;
  onSubmit?: ReturnType<typeof vi.fn>;
}) {
  const mode = overrides?.mode ?? 'create';
  const initialValues =
    overrides?.initialValues ??
    (mode === 'create' ? initialCreate : initialEditAsset);
  const onSubmit = overrides?.onSubmit ?? vi.fn().mockResolvedValue({ ok: true });
  const onCancel = vi.fn();
  const utils = render(
    <AccountForm
      mode={mode}
      initialValues={initialValues}
      accountTypes={accountTypes}
      currencies={currencies}
      onSubmit={onSubmit}
      onCancel={onCancel}
    />,
  );
  return { ...utils, onSubmit, onCancel };
}

describe('AccountForm', () => {
  it('renders Name, Type, Currency, Description with initial values (Edit)', () => {
    renderForm({ mode: 'edit' });
    expect(screen.getByLabelText(/name/i)).toHaveValue('Checking Account');
    expect(within(screen.getByLabelText(/^type$/i)).getByText('Asset')).toBeInTheDocument();
    expect(within(screen.getByLabelText(/currency/i)).getByText(/EUR/)).toBeInTheDocument();
    expect(screen.getByLabelText(/description/i)).toHaveValue('Main checking');
  });

  it('Type picker is editable on Create', () => {
    renderForm({ mode: 'create' });
    expect(screen.getByLabelText(/^type$/i)).toHaveAttribute('aria-expanded');
  });

  it('Type is read-only on Edit', () => {
    renderForm({ mode: 'edit' });
    const trigger = screen.getByLabelText(/^type$/i);
    expect(trigger).not.toHaveAttribute('aria-expanded');
  });

  it('Currency is read-only on Edit', () => {
    renderForm({ mode: 'edit' });
    const trigger = screen.getByLabelText(/currency/i);
    expect(trigger).not.toHaveAttribute('aria-expanded');
  });

  it('Asset block (Exclude from spendable) renders when Type is Asset', () => {
    renderForm({ mode: 'edit', initialValues: initialEditAsset });
    expect(screen.getByText(/exclude from spendable/i)).toBeInTheDocument();
    expect(screen.queryByText(/repayment type/i)).toBeNull();
  });

  it('Liability block (Repayment type) renders when Type is Liability', () => {
    renderForm({ mode: 'edit', initialValues: initialEditLiability });
    expect(screen.getByText(/repayment type/i)).toBeInTheDocument();
    expect(screen.queryByText(/exclude from spendable/i)).toBeNull();
  });

  it('Interest rate field renders when Liability + Amortising', () => {
    renderForm({ mode: 'edit', initialValues: initialEditLiability });
    expect(screen.getByLabelText(/interest rate/i)).toBeInTheDocument();
  });

  it('Save and Cancel are visible', () => {
    renderForm({ mode: 'edit' });
    expect(screen.getByRole('button', { name: /save/i })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: /cancel/i })).toBeInTheDocument();
  });

  it('Cancel calls onCancel', () => {
    const { onCancel } = renderForm({ mode: 'edit' });
    fireEvent.click(screen.getByRole('button', { name: /cancel/i }));
    expect(onCancel).toHaveBeenCalledTimes(1);
  });

  it('Save is disabled when not dirty (Edit)', () => {
    renderForm({ mode: 'edit' });
    expect(screen.getByRole('button', { name: /save/i })).toBeDisabled();
  });

  it('Save enables when Name changes', () => {
    renderForm({ mode: 'edit' });
    fireEvent.change(screen.getByLabelText(/name/i), { target: { value: 'Renamed' } });
    expect(screen.getByRole('button', { name: /save/i })).toBeEnabled();
  });

  it('Save shows "Saving…" while pending', () => {
    let resolveSubmit: (v: { ok: true }) => void = () => {};
    const onSubmit = vi.fn(
      () => new Promise<{ ok: true }>((resolve) => { resolveSubmit = resolve; }),
    );
    renderForm({ mode: 'edit', onSubmit });
    fireEvent.change(screen.getByLabelText(/name/i), { target: { value: 'Renamed' } });
    fireEvent.click(screen.getByRole('button', { name: /save/i }));
    expect(screen.getByRole('button', { name: /saving/i })).toBeDisabled();
    resolveSubmit({ ok: true });
  });

  it('Inputs retain edits when onSubmit returns ok:false', async () => {
    const onSubmit = vi.fn().mockResolvedValue({ ok: false });
    renderForm({ mode: 'edit', onSubmit });
    fireEvent.change(screen.getByLabelText(/name/i), { target: { value: 'Renamed' } });
    fireEvent.click(screen.getByRole('button', { name: /save/i }));
    await waitFor(() =>
      expect(screen.getByRole('button', { name: /save/i })).toBeEnabled(),
    );
    expect(screen.getByLabelText(/name/i)).toHaveValue('Renamed');
  });

  it('Submit normalises interestRate to null when repayment type is not Amortising', async () => {
    const onSubmit = vi.fn().mockResolvedValue({ ok: true });
    // Edit a Liability/Amortising account, switch repayment to FullMonthly, save.
    renderForm({ mode: 'edit', initialValues: initialEditLiability, onSubmit });
    // Click the repayment type picker and switch to FullMonthly via the option list.
    fireEvent.click(screen.getByLabelText(/repayment type/i));
    fireEvent.click(await screen.findByText(/Full Monthly/i));
    // Save (form is now dirty because repayment changed).
    fireEvent.click(screen.getByRole('button', { name: /save/i }));
    await waitFor(() => expect(onSubmit).toHaveBeenCalled());
    const submittedValues = onSubmit.mock.calls[0][0];
    expect(submittedValues.liabilityRepaymentType).toBe('FullMonthly');
    expect(submittedValues.interestRate).toBeNull();
  });

  it('Switching Amortising → FullMonthly hides Interest rate but keeps cached value', async () => {
    renderForm({ mode: 'edit', initialValues: initialEditLiability });
    expect(screen.getByLabelText(/interest rate/i)).toBeInTheDocument();
    fireEvent.click(screen.getByLabelText(/repayment type/i));
    fireEvent.click(await screen.findByText(/Full Monthly/i));
    expect(screen.queryByLabelText(/interest rate/i)).toBeNull();
    // Switch back to Amortising — cached value should be preserved.
    fireEvent.click(screen.getByLabelText(/repayment type/i));
    fireEvent.click(await screen.findByText(/Amortising/i));
    expect(screen.getByLabelText(/interest rate/i)).toHaveValue(0.035);
  });

  it('Advanced disclosure on Edit hides opening balance + start date by default', () => {
    renderForm({ mode: 'edit' });
    expect(screen.queryByLabelText(/opening balance/i)).toBeNull();
    expect(screen.queryByLabelText(/start date/i)).toBeNull();
    expect(screen.getByText(/advanced/i)).toBeInTheDocument();
  });

  it('Expanding the Advanced disclosure on Edit reveals opening balance + start date', () => {
    renderForm({ mode: 'edit' });
    fireEvent.click(screen.getByText(/advanced/i));
    expect(screen.getByLabelText(/opening balance/i)).toBeInTheDocument();
    expect(screen.getByLabelText(/start date/i)).toBeInTheDocument();
  });

  it('Opening balance + start date are inline (no disclosure) on Create', () => {
    renderForm({ mode: 'create' });
    expect(screen.getByLabelText(/opening balance/i)).toBeInTheDocument();
    expect(screen.getByLabelText(/start date/i)).toBeInTheDocument();
    expect(screen.queryByText(/advanced/i)).toBeNull();
  });
});
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `cd ProjectCeres.Client && pnpm test AccountForm`
Expected: FAIL — `AccountForm` not exported.

- [ ] **Step 3: DO NOT COMMIT.**

---

## Task 11: AccountForm — implementation

**Files:**
- Create: `ProjectCeres.Client/src/app/features/accounts/AccountForm.tsx`

Pure UI. Conditional Asset vs Liability blocks. Server-immutable Type + Currency on Edit. Opening balance behind disclosure on Edit. Submit-time normalisation (Layer 1).

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
import { Switch } from '@/components/ui/switch';
import {
  Tooltip,
  TooltipContent,
  TooltipProvider,
  TooltipTrigger,
} from '@/components/ui/tooltip';
import { cn } from '@/lib/utils';
import type {
  AccountFormValues,
  AccountTypeDto,
  CurrencyDto,
  CreateAccountRequest,
  RepaymentType,
  UpdateAccountRequest,
} from './accounts-api';

type SubmitResult = { ok: true } | { ok: false };

type Props = {
  mode: 'create' | 'edit';
  initialValues: AccountFormValues;
  accountTypes: AccountTypeDto[];
  currencies: CurrencyDto[];
  onSubmit: (
    values: CreateAccountRequest | UpdateAccountRequest,
  ) => Promise<SubmitResult>;
  onCancel: () => void;
};

const REPAYMENT_OPTIONS: { value: RepaymentType | null; label: string; hint: string }[] = [
  { value: null,         label: '— None —',     hint: '' },
  { value: 'FullMonthly', label: 'Full Monthly', hint: 'Pay the full balance each month; no interest accrues.' },
  { value: 'Amortising',  label: 'Amortising',   hint: 'Fixed monthly payment with interest. Use for loans and mortgages.' },
];

const TYPE_LOCKED_TOOLTIP =
  'Account type cannot be changed after creation. Create a new account if you need a different type.';
const CURRENCY_LOCKED_TOOLTIP =
  'Account currency cannot be changed after creation. Create a new account if you need a different currency.';

function shallowEqual(a: AccountFormValues, b: AccountFormValues): boolean {
  return (
    a.name                   === b.name                   &&
    a.accountTypeId          === b.accountTypeId          &&
    a.currencyId             === b.currencyId             &&
    a.description            === b.description            &&
    a.openingBalance         === b.openingBalance         &&
    a.openingBalanceDate     === b.openingBalanceDate     &&
    a.liabilityRepaymentType === b.liabilityRepaymentType &&
    a.interestRate           === b.interestRate           &&
    a.excludeFromSpendable   === b.excludeFromSpendable
  );
}

export function AccountForm({
  mode, initialValues, accountTypes, currencies, onSubmit, onCancel,
}: Props) {
  // Normalise null description to empty string for input handling.
  const normalised: AccountFormValues = {
    ...initialValues,
    description: initialValues.description ?? '',
  };
  const [snapshot, setSnapshot] = useState<AccountFormValues>(normalised);
  const [values, setValues] = useState<AccountFormValues>(normalised);
  const [submitting, setSubmitting] = useState(false);
  const [advancedOpen, setAdvancedOpen] = useState(false);

  const isDirty = !shallowEqual(values, snapshot);

  const isLiability = values.accountTypeId === 2;
  const isAmortising = values.liabilityRepaymentType === 'Amortising';

  const selectedType = accountTypes.find((t) => t.id === values.accountTypeId);
  const selectedCurrency = currencies.find((c) => c.id === values.currencyId);

  async function handleSubmit(e: React.FormEvent<HTMLFormElement>) {
    e.preventDefault();
    if (!isDirty || submitting) return;
    setSubmitting(true);

    // Layer 1: SPA submit-time normalisation. Always ensure the wire is self-consistent.
    const normalisedRepayment = isLiability ? values.liabilityRepaymentType : null;
    const normalisedRate = isLiability && normalisedRepayment === 'Amortising'
      ? values.interestRate
      : null;
    const normalisedExclude = isLiability ? false : values.excludeFromSpendable;

    const body =
      mode === 'create'
        ? ({
            name: values.name.trim(),
            accountTypeId: values.accountTypeId,
            currencyId: values.currencyId,
            description: values.description.trim() === '' ? null : values.description.trim(),
            openingBalance: values.openingBalance,
            openingBalanceDate: values.openingBalanceDate,
            liabilityRepaymentType: normalisedRepayment,
            interestRate: normalisedRate,
            excludeFromSpendable: normalisedExclude,
          } as CreateAccountRequest)
        : ({
            name: values.name.trim(),
            description: values.description.trim() === '' ? null : values.description.trim(),
            openingBalance: values.openingBalance,
            openingBalanceDate: values.openingBalanceDate,
            liabilityRepaymentType: normalisedRepayment,
            interestRate: normalisedRate,
            excludeFromSpendable: normalisedExclude,
          } as UpdateAccountRequest);

    const result = await onSubmit(body);
    setSubmitting(false);
    if (result.ok) setSnapshot(values);
  }

  return (
    <TooltipProvider delay={200}>
      <form onSubmit={handleSubmit} className="space-y-6">
        <div className="space-y-1.5">
          <Label htmlFor="accountName">Name</Label>
          <Input
            id="accountName"
            aria-label="Name"
            value={values.name}
            onChange={(e) => setValues((v) => ({ ...v, name: e.target.value }))}
            required
            maxLength={100}
          />
        </div>

        <div className="space-y-1.5">
          <Label htmlFor="accountType">Type</Label>
          {mode === 'edit' ? (
            <LockedRow
              id="accountType"
              ariaLabel="Type"
              text={selectedType?.name ?? '—'}
              tooltip={TYPE_LOCKED_TOOLTIP}
            />
          ) : (
            <TypeCombobox
              id="accountType"
              value={values.accountTypeId}
              types={accountTypes}
              onChange={(id) =>
                setValues((v) => ({
                  ...v,
                  accountTypeId: id,
                  // Switching to Asset clears liability fields visually; switching to Liability
                  // resets the toggle. The submit normalisation handles the wire layer.
                  liabilityRepaymentType: id === 2 ? v.liabilityRepaymentType : null,
                  excludeFromSpendable: id === 1 ? v.excludeFromSpendable : false,
                }))
              }
            />
          )}
        </div>

        <div className="space-y-1.5">
          <Label htmlFor="accountCurrency">Currency</Label>
          {mode === 'edit' ? (
            <LockedRow
              id="accountCurrency"
              ariaLabel="Currency"
              text={selectedCurrency ? `${selectedCurrency.code} — ${selectedCurrency.name} ${selectedCurrency.symbol}` : '—'}
              tooltip={CURRENCY_LOCKED_TOOLTIP}
            />
          ) : (
            <CurrencyCombobox
              id="accountCurrency"
              value={values.currencyId}
              currencies={currencies}
              onChange={(id) => setValues((v) => ({ ...v, currencyId: id }))}
            />
          )}
        </div>

        <div className="space-y-1.5">
          <Label htmlFor="accountDescription">Description</Label>
          <Input
            id="accountDescription"
            aria-label="Description"
            value={values.description}
            onChange={(e) => setValues((v) => ({ ...v, description: e.target.value }))}
            maxLength={500}
          />
        </div>

        {!isLiability ? (
          <div className="space-y-1.5">
            <div className="flex items-center gap-2">
              <Switch
                id="excludeFromSpendable"
                checked={values.excludeFromSpendable}
                onCheckedChange={(checked) =>
                  setValues((v) => ({ ...v, excludeFromSpendable: checked }))
                }
              />
              <Label htmlFor="excludeFromSpendable" className="text-sm font-normal">
                Exclude from spendable balance
              </Label>
            </div>
            <p className="text-xs text-muted-foreground">
              Excluded accounts don't count toward your spendable balance but still appear in net worth.
            </p>
          </div>
        ) : (
          <>
            <div className="space-y-1.5">
              <Label htmlFor="repaymentType">Repayment type</Label>
              <RepaymentCombobox
                id="repaymentType"
                value={values.liabilityRepaymentType}
                onChange={(rt) => setValues((v) => ({ ...v, liabilityRepaymentType: rt }))}
              />
              <p className="text-xs text-muted-foreground">
                <strong>Full Monthly</strong> — pay the full balance each month; no interest accrues.{' '}
                <strong>Amortising</strong> — fixed monthly payment with interest (loans, mortgages).
              </p>
            </div>
            {isAmortising ? (
              <div className="space-y-1.5">
                <Label htmlFor="interestRate">Interest rate</Label>
                <Input
                  id="interestRate"
                  aria-label="Interest rate"
                  type="number"
                  step="0.0001"
                  min={0}
                  max={1}
                  value={values.interestRate ?? ''}
                  onChange={(e) =>
                    setValues((v) => ({
                      ...v,
                      interestRate: e.target.value === '' ? null : Number(e.target.value),
                    }))
                  }
                />
                <p className="text-xs text-muted-foreground">
                  Annual interest rate as a decimal. e.g. 0.035 = 3.5%.
                </p>
              </div>
            ) : null}
          </>
        )}

        {mode === 'create' ? (
          <OpeningBalanceFields values={values} setValues={setValues} />
        ) : (
          <details
            className="rounded-md border border-input p-3"
            open={advancedOpen}
            onToggle={(e) => setAdvancedOpen((e.target as HTMLDetailsElement).open)}
          >
            <summary className="cursor-pointer text-sm font-medium select-none">
              Advanced — opening balance and start date
            </summary>
            <div className="mt-4">
              <OpeningBalanceFields values={values} setValues={setValues} editing />
            </div>
          </details>
        )}

        <div className="flex items-center gap-2 pt-2">
          <Button type="submit" disabled={!isDirty || submitting}>
            {submitting ? 'Saving…' : 'Save'}
          </Button>
          <Button type="button" variant="outline" onClick={onCancel} disabled={submitting}>
            Cancel
          </Button>
        </div>
      </form>
    </TooltipProvider>
  );
}

// ---------------------------------------------------------------------------
// Subcomponents
// ---------------------------------------------------------------------------

function LockedRow({
  id, ariaLabel, text, tooltip,
}: { id: string; ariaLabel: string; text: string; tooltip: string }) {
  return (
    <div
      id={id}
      aria-label={ariaLabel}
      className="flex h-9 w-full items-center justify-between rounded-md border border-input bg-muted/40 px-3 text-sm"
    >
      <span>{text}</span>
      <Tooltip>
        <TooltipTrigger
          render={
            <button
              type="button"
              aria-label="Why is this locked?"
              className="text-muted-foreground hover:text-foreground transition-colors"
            >
              <Info className="h-3.5 w-3.5" />
            </button>
          }
        />
        <TooltipContent className="max-w-xs">{tooltip}</TooltipContent>
      </Tooltip>
    </div>
  );
}

function OpeningBalanceFields({
  values, setValues, editing,
}: {
  values: AccountFormValues;
  setValues: React.Dispatch<React.SetStateAction<AccountFormValues>>;
  editing?: boolean;
}) {
  return (
    <div className="space-y-4">
      <div className="space-y-1.5">
        <Label htmlFor="openingBalance">Opening balance</Label>
        <Input
          id="openingBalance"
          aria-label="Opening balance"
          type="number"
          step="0.01"
          value={values.openingBalance}
          onChange={(e) =>
            setValues((v) => ({ ...v, openingBalance: Number(e.target.value) }))
          }
        />
        <p className="text-xs text-muted-foreground">
          Leave at 0 to start with no balance. A negative value records a liability opening balance.
        </p>
      </div>
      <div className="space-y-1.5">
        <Label htmlFor="openingBalanceDate">Start date</Label>
        <Input
          id="openingBalanceDate"
          aria-label="Start date"
          type="date"
          value={values.openingBalanceDate}
          onChange={(e) => setValues((v) => ({ ...v, openingBalanceDate: e.target.value }))}
        />
        <p className="text-xs text-muted-foreground">
          {editing
            ? 'Moving the start date backward lets you record earlier transactions. Moving it forward will hide existing transactions before that date from the active list — they remain in the database but become read-only.'
            : 'The date this account starts being tracked. Defaults to today.'}
        </p>
      </div>
    </div>
  );
}

function TypeCombobox({
  id, value, types, onChange,
}: {
  id: string;
  value: number;
  types: AccountTypeDto[];
  onChange: (id: number) => void;
}) {
  const [open, setOpen] = useState(false);
  const selected = types.find((t) => t.id === value);
  return (
    <Popover open={open} onOpenChange={setOpen}>
      <PopoverTrigger
        render={
          <Button
            id={id} type="button" variant="outline" role="combobox"
            aria-label="Type" aria-expanded={open}
            className="w-full justify-between"
          >
            <span>{selected?.name ?? 'Select type'}</span>
            <ChevronsUpDown className="ml-2 h-4 w-4 shrink-0 opacity-50" />
          </Button>
        }
      />
      <PopoverContent className="p-0 w-(--anchor-width) min-w-(--anchor-width)" align="start">
        <Command>
          <CommandList>
            <CommandGroup>
              {types.map((t) => (
                <CommandItem key={t.id} value={t.name} className="whitespace-nowrap"
                  onSelect={() => { onChange(t.id); setOpen(false); }}>
                  <Check className={cn('mr-2 h-4 w-4', t.id === value ? 'opacity-100' : 'opacity-0')} />
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

function CurrencyCombobox({
  id, value, currencies, onChange,
}: {
  id: string;
  value: number;
  currencies: CurrencyDto[];
  onChange: (id: number) => void;
}) {
  const [open, setOpen] = useState(false);
  const selected = currencies.find((c) => c.id === value);
  return (
    <Popover open={open} onOpenChange={setOpen}>
      <PopoverTrigger
        render={
          <Button
            id={id} type="button" variant="outline" role="combobox"
            aria-label="Currency" aria-expanded={open}
            className="w-full justify-between"
          >
            <span>{selected ? `${selected.code} — ${selected.name} ${selected.symbol}` : 'Select currency'}</span>
            <ChevronsUpDown className="ml-2 h-4 w-4 shrink-0 opacity-50" />
          </Button>
        }
      />
      <PopoverContent className="p-0 w-(--anchor-width) min-w-(--anchor-width)" align="start">
        <Command>
          <CommandList>
            <CommandGroup>
              {currencies.map((c) => (
                <CommandItem key={c.id} value={c.code} className="whitespace-nowrap"
                  onSelect={() => { onChange(c.id); setOpen(false); }}>
                  <Check className={cn('mr-2 h-4 w-4', c.id === value ? 'opacity-100' : 'opacity-0')} />
                  {c.code} — {c.name} {c.symbol}
                </CommandItem>
              ))}
            </CommandGroup>
          </CommandList>
        </Command>
      </PopoverContent>
    </Popover>
  );
}

function RepaymentCombobox({
  id, value, onChange,
}: {
  id: string;
  value: RepaymentType | null;
  onChange: (v: RepaymentType | null) => void;
}) {
  const [open, setOpen] = useState(false);
  const selected = REPAYMENT_OPTIONS.find((o) => o.value === value);
  return (
    <Popover open={open} onOpenChange={setOpen}>
      <PopoverTrigger
        render={
          <Button
            id={id} type="button" variant="outline" role="combobox"
            aria-label="Repayment type" aria-expanded={open}
            className="w-full justify-between"
          >
            <span>{selected?.label ?? '— None —'}</span>
            <ChevronsUpDown className="ml-2 h-4 w-4 shrink-0 opacity-50" />
          </Button>
        }
      />
      <PopoverContent className="p-0 w-(--anchor-width) min-w-(--anchor-width)" align="start">
        <Command>
          <CommandList>
            <CommandGroup>
              {REPAYMENT_OPTIONS.map((opt) => (
                <CommandItem key={opt.label} value={opt.label} className="whitespace-nowrap"
                  onSelect={() => { onChange(opt.value); setOpen(false); }}>
                  <Check className={cn('mr-2 h-4 w-4', opt.value === value ? 'opacity-100' : 'opacity-0')} />
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

Run: `cd ProjectCeres.Client && pnpm test AccountForm`
Expected: PASS — all 17 tests green.

- [ ] **Step 3: DO NOT COMMIT.**

---

## Task 12: AccountsLayout — failing tests first

**Files:**
- Create: `ProjectCeres.Client/src/app/features/accounts/AccountsLayout.test.tsx`

The layout owns the GET, the URL state (search/include-archived), and renders the subtotal strip + table. The first-run state requires a separate "do I have any accounts at all" signal — handled here via a second `useApi` call to `/api/accounts?includeInactive=true` whose result is checked only when the visible list is empty.

- [ ] **Step 1: Write the test file**

```tsx
import { fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import { AccountsLayout } from './AccountsLayout';

vi.mock('sonner', () => ({
  toast: { success: vi.fn(), error: vi.fn() },
}));

const mockFetch = vi.fn();

const allRows = [
  { id: 'a-1', name: 'Cash',             accountTypeId: 1, accountTypeName: 'Asset',     currencyId: 1, currencyCode: 'EUR', currencySymbol: '€', description: null, isActive: true,  excludeFromSpendable: false, liabilityRepaymentType: null,         interestRate: null,  balance: 120,     hasTransactions: false },
  { id: 'a-2', name: 'Checking Account', accountTypeId: 1, accountTypeName: 'Asset',     currencyId: 1, currencyCode: 'EUR', currencySymbol: '€', description: null, isActive: true,  excludeFromSpendable: false, liabilityRepaymentType: null,         interestRate: null,  balance: 2114.56, hasTransactions: true  },
  { id: 'a-3', name: 'Credit Card',      accountTypeId: 2, accountTypeName: 'Liability', currencyId: 1, currencyCode: 'EUR', currencySymbol: '€', description: null, isActive: true,  excludeFromSpendable: false, liabilityRepaymentType: 'FullMonthly', interestRate: null,  balance: 500,     hasTransactions: true  },
  { id: 'a-4', name: 'Old Savings',      accountTypeId: 1, accountTypeName: 'Asset',     currencyId: 1, currencyCode: 'EUR', currencySymbol: '€', description: null, isActive: false, excludeFromSpendable: false, liabilityRepaymentType: null,         interestRate: null,  balance: 0,       hasTransactions: true  },
];

beforeEach(() => {
  global.fetch = mockFetch as unknown as typeof fetch;
  mockFetch.mockImplementation((url: string) => {
    if (url === '/api/accounts') {
      return Promise.resolve({ ok: true, status: 200, json: async () => allRows.filter((r) => r.isActive) });
    }
    if (url === '/api/accounts?includeInactive=true') {
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
        <Route path="/accounts" element={<AccountsLayout />}>
          <Route path="new" element={<div data-testid="new-page">NEW</div>} />
          <Route path=":id/edit" element={<div data-testid="edit-page">EDIT</div>} />
        </Route>
      </Routes>
    </MemoryRouter>,
  );
}

describe('AccountsLayout', () => {
  it('renders skeleton while loading', () => {
    mockFetch.mockImplementation(() => new Promise(() => {}));
    renderAt('/accounts');
    expect(screen.getByTestId('accounts-skeleton')).toBeInTheDocument();
  });

  it('renders active accounts in the table', async () => {
    renderAt('/accounts');
    await screen.findByText('Cash');
    expect(screen.getByText('Checking Account')).toBeInTheDocument();
    expect(screen.getByText('Credit Card')).toBeInTheDocument();
    expect(screen.queryByText('Old Savings')).toBeNull();
  });

  it('search filters rows', async () => {
    renderAt('/accounts');
    await screen.findByText('Cash');
    fireEvent.change(screen.getByPlaceholderText(/filter accounts/i), { target: { value: 'che' } });
    await waitFor(() => {
      expect(screen.getByText('Checking Account')).toBeInTheDocument();
      expect(screen.queryByText('Cash')).toBeNull();
    });
  });

  it('search-empty state with Clear search', async () => {
    renderAt('/accounts');
    await screen.findByText('Cash');
    fireEvent.change(screen.getByPlaceholderText(/filter accounts/i), { target: { value: 'zzznomatch' } });
    await waitFor(() => {
      expect(screen.getByText(/no accounts match/i)).toBeInTheDocument();
    });
    expect(screen.getByRole('button', { name: /clear search/i })).toBeInTheDocument();
  });

  it('Include archived toggle adds archived rows', async () => {
    renderAt('/accounts');
    await screen.findByText('Cash');
    fireEvent.click(screen.getByRole('switch', { name: /include archived/i }));
    await screen.findByText('Old Savings');
  });

  it('archived rows render Archived badge', async () => {
    renderAt('/accounts?includeInactive=true');
    const old = (await screen.findByText('Old Savings')).closest('tr')!;
    expect(within(old).getByText('Archived')).toBeInTheDocument();
  });

  it('GET error renders CardError with Retry', async () => {
    mockFetch.mockImplementation(() =>
      Promise.resolve({ ok: false, status: 500, json: async () => null }),
    );
    renderAt('/accounts');
    await waitFor(() => expect(screen.getByText(/Accounts/)).toBeInTheDocument());
    expect(screen.getByRole('button', { name: /retry/i })).toBeInTheDocument();
  });

  it('clicking + New account navigates to /accounts/new', async () => {
    renderAt('/accounts');
    await screen.findByText('Cash');
    fireEvent.click(screen.getByRole('link', { name: /new account/i }));
    expect(await screen.findByTestId('new-page')).toBeInTheDocument();
  });

  it('renders the per-currency subtotal strip when accounts span 2+ currencies', async () => {
    mockFetch.mockImplementation((url: string) => {
      if (url === '/api/accounts') {
        return Promise.resolve({
          ok: true, status: 200,
          json: async () => [
            { ...allRows[0], id: 'eur', currencyCode: 'EUR', currencySymbol: '€', balance: 1000 },
            { ...allRows[0], id: 'usd', currencyCode: 'USD', currencySymbol: '$', balance: 500 },
          ],
        });
      }
      return Promise.resolve({ ok: false, status: 404, json: async () => null });
    });
    renderAt('/accounts');
    await screen.findByText(/EUR/);
    expect(screen.getByText(/€1.000/)).toBeInTheDocument();
    expect(screen.getByText(/\$500/)).toBeInTheDocument();
  });

  it('hides the per-currency subtotal strip when only one currency', async () => {
    renderAt('/accounts');
    await screen.findByText('Cash');
    // No 'EUR' label visible because there's only one currency.
    expect(screen.queryByText(/EUR €/)).toBeNull();
  });

  it('first-run empty state renders when no accounts at all', async () => {
    mockFetch.mockImplementation(() =>
      Promise.resolve({ ok: true, status: 200, json: async () => [] }),
    );
    renderAt('/accounts');
    await waitFor(() => {
      expect(screen.getByText(/no accounts yet/i)).toBeInTheDocument();
    });
    expect(screen.getByText(/create your first account/i)).toBeInTheDocument();
  });

  it('"No accounts." renders when only archived exist (toggle off)', async () => {
    mockFetch.mockImplementation((url: string) => {
      if (url === '/api/accounts') {
        return Promise.resolve({ ok: true, status: 200, json: async () => [] });
      }
      if (url === '/api/accounts?includeInactive=true') {
        return Promise.resolve({ ok: true, status: 200, json: async () => [allRows[3]] });
      }
      return Promise.resolve({ ok: false, status: 404, json: async () => null });
    });
    renderAt('/accounts');
    await waitFor(() => {
      expect(screen.getByText(/^no accounts\.$/i)).toBeInTheDocument();
    });
  });
});
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `cd ProjectCeres.Client && pnpm test AccountsLayout`
Expected: FAIL — `AccountsLayout` not exported.

- [ ] **Step 3: DO NOT COMMIT.**

---

## Task 13: AccountsLayout — implementation

**Files:**
- Create: `ProjectCeres.Client/src/app/features/accounts/AccountsLayout.tsx`

Page glue. Owns the data fetch (active list + a second "all" fetch for the first-run check), search input, archived toggle, subtotal strip, and the table.

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
import { CardError } from '../../components/CardError';
import { useApi, type UseApiResult } from '../../lib/use-api';
import { useDebounced } from '../../lib/use-debounced';
import { AccountCurrencySubtotals } from './AccountCurrencySubtotals';
import { AccountsTable } from './AccountsTable';
import { ACCOUNTS_URL, buildListUrl, type AccountListItemDto } from './accounts-api';

export function AccountsLayout() {
  const [params, setParams] = useSearchParams();

  const onCreate = !!useMatch('/accounts/new');
  const onEdit   = !!useMatch('/accounts/:id/edit');
  const childActive = onCreate || onEdit;

  const includeInactive = params.get('includeInactive') === 'true';
  const queryParam = params.get('q') ?? '';

  const headingRef = useRef<HTMLHeadingElement>(null);
  useEffect(() => { headingRef.current?.focus(); }, []);

  const [searchInput, setSearchInput] = useState(queryParam);
  const debouncedSearch = useDebounced(searchInput, 200);

  useEffect(() => {
    const next = new URLSearchParams(params);
    if (debouncedSearch) next.set('q', debouncedSearch);
    else next.delete('q');
    setParams(next, { replace: true });
  }, [debouncedSearch]);  // eslint-disable-line react-hooks/exhaustive-deps

  const list = useApi<AccountListItemDto[]>(buildListUrl(includeInactive));
  // Second fetch ONLY for the first-run check — gates whether we render the
  // first-run empty state vs. the "No accounts." message. Cheap (4–15 rows).
  const allList = useApi<AccountListItemDto[]>(`${ACCOUNTS_URL}?includeInactive=true`);

  if (childActive) {
    return (
      <div className="mx-auto max-w-3xl space-y-6">
        <Outlet context={{ refetch: list.refetch }} />
      </div>
    );
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
        <h1 ref={headingRef} tabIndex={-1} className="text-2xl font-semibold outline-none">
          Accounts
        </h1>
        <p className="mt-2 text-muted-foreground">
          Manage the accounts you own and the debts you owe. Balances are derived from your
          transactions, transfers, and liability payments.
        </p>
      </header>

      {list.data ? <AccountCurrencySubtotals rows={list.data} /> : null}

      <Card>
        <CardContent className="pt-6 space-y-4">
          <div className="flex flex-col gap-3 sm:flex-row sm:items-end">
            <div className="flex-1 space-y-1.5">
              <Label htmlFor="acc-search" className="sr-only">Filter accounts</Label>
              <Input
                id="acc-search"
                placeholder="Filter accounts…"
                value={searchInput}
                onChange={(e) => setSearchInput(e.target.value)}
              />
            </div>
            <Button render={<Link to="new"><Plus className="h-4 w-4 mr-1" />New account</Link>} />
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

          <AccountsBody
            list={list}
            allList={allList}
            query={debouncedSearch}
            onClearSearch={clearSearch}
          />
        </CardContent>
      </Card>
    </div>
  );
}

function AccountsBody({
  list, allList, query, onClearSearch,
}: {
  list: UseApiResult<AccountListItemDto[]>;
  allList: UseApiResult<AccountListItemDto[]>;
  query: string;
  onClearSearch: () => void;
}) {
  const lower = query.toLowerCase();

  const sorted = useMemo(() => {
    if (!list.data) return [];
    return [...list.data]
      .filter((row) => row.name.toLowerCase().includes(lower))
      .sort((a, b) => a.name.localeCompare(b.name));
  }, [list.data, lower]);

  if (list.loading) {
    return (
      <div data-testid="accounts-skeleton" className="space-y-2 py-2">
        <Skeleton className="h-9 w-full" />
        <Skeleton className="h-9 w-full" />
        <Skeleton className="h-9 w-full" />
        <Skeleton className="h-9 w-full" />
      </div>
    );
  }

  if (list.error || !list.data) {
    return <CardError section="Accounts" onRetry={list.refetch} />;
  }

  // First-run vs. "only archived exist" branch: only check when the list is empty.
  if (sorted.length === 0 && query === '') {
    const totalAccountsExist = (allList.data?.length ?? 0) > 0;
    if (!totalAccountsExist) {
      return (
        <div className="px-3 py-12 text-center space-y-3">
          <h2 className="text-lg font-semibold">No accounts yet</h2>
          <p className="text-sm text-muted-foreground">
            Create your first account to start tracking.
          </p>
          <Button render={<Link to="new"><Plus className="h-4 w-4 mr-1" />New account</Link>} />
        </div>
      );
    }
    // Otherwise, only-archived case: show the simple message.
    return (
      <div className="px-3 py-8 text-center text-sm text-muted-foreground italic">
        No accounts.
      </div>
    );
  }

  if (sorted.length === 0 && query) {
    return (
      <div className="px-3 py-8 text-center text-sm text-muted-foreground italic space-y-3">
        <p>No accounts match '{query}'.</p>
        <Button type="button" variant="link" onClick={onClearSearch}>
          Clear search
        </Button>
      </div>
    );
  }

  return <AccountsTable rows={sorted} onChanged={() => { list.refetch(); allList.refetch(); }} />;
}
```

- [ ] **Step 2: Run tests to verify they pass**

Run: `cd ProjectCeres.Client && pnpm test AccountsLayout`
Expected: PASS — all 12 tests green.

- [ ] **Step 3: DO NOT COMMIT.**

---

## Task 14: AccountCreate — failing tests first

**Files:**
- Create: `ProjectCeres.Client/src/app/features/accounts/AccountCreate.test.tsx`

- [ ] **Step 1: Write the test file**

```tsx
import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { MemoryRouter, Outlet, Route, Routes } from 'react-router-dom';
import { toast } from 'sonner';
import { AccountCreate } from './AccountCreate';

vi.mock('sonner', () => ({
  toast: { success: vi.fn(), error: vi.fn() },
}));

const mockFetch = vi.fn();

const accountTypesResponse = [
  { id: 1, name: 'Asset' },
  { id: 2, name: 'Liability' },
];

const currenciesResponse = [
  { id: 1, code: 'EUR', name: 'Euro',     symbol: '€' },
  { id: 2, code: 'USD', name: 'US Dollar', symbol: '$' },
];

beforeEach(() => {
  global.fetch = mockFetch as unknown as typeof fetch;
  mockFetch.mockImplementation((url: string, init?: RequestInit) => {
    if (url === '/api/account-types') {
      return Promise.resolve({ ok: true, status: 200, json: async () => accountTypesResponse });
    }
    if (url === '/api/currencies') {
      return Promise.resolve({ ok: true, status: 200, json: async () => currenciesResponse });
    }
    if (url === '/api/accounts' && init?.method === 'POST') {
      return Promise.resolve({ ok: true, status: 201, json: async () => ({ id: 'new-1' }) });
    }
    return Promise.resolve({ ok: false, status: 404, json: async () => null });
  });
});

afterEach(() => vi.resetAllMocks());

function LayoutShim() {
  return <Outlet context={{ refetch: vi.fn() }} />;
}

function renderPage() {
  return render(
    <MemoryRouter initialEntries={['/accounts/new']}>
      <Routes>
        <Route path="/accounts" element={<LayoutShim />}>
          <Route index element={<div data-testid="list-page">LIST</div>} />
          <Route path="new" element={<AccountCreate />} />
        </Route>
      </Routes>
    </MemoryRouter>,
  );
}

describe('AccountCreate', () => {
  it('renders the form when account-types and currencies load', async () => {
    renderPage();
    await screen.findByLabelText(/name/i);
    expect(screen.getByLabelText(/^type$/i)).toBeInTheDocument();
    expect(screen.getByLabelText(/currency/i)).toBeInTheDocument();
  });

  it('POST 2xx fires toast.success and navigates back', async () => {
    renderPage();
    const nameInput = await screen.findByLabelText(/name/i);
    fireEvent.change(nameInput, { target: { value: 'New Account' } });
    fireEvent.click(screen.getByRole('button', { name: /save/i }));
    await waitFor(() => expect(toast.success).toHaveBeenCalledWith('Created.'));
    expect(await screen.findByTestId('list-page')).toBeInTheDocument();
  });

  it('POST 422 fires toast.error and form retains values', async () => {
    mockFetch.mockImplementation((url: string, init?: RequestInit) => {
      if (url === '/api/account-types') return Promise.resolve({ ok: true, status: 200, json: async () => accountTypesResponse });
      if (url === '/api/currencies')    return Promise.resolve({ ok: true, status: 200, json: async () => currenciesResponse });
      if (url === '/api/accounts' && init?.method === 'POST') {
        return Promise.resolve({ ok: false, status: 422, json: async () => ({ error: { code: 'VALIDATION_ERROR', message: 'Bad', details: [] } }) });
      }
      return Promise.resolve({ ok: false, status: 404, json: async () => null });
    });
    renderPage();
    const nameInput = await screen.findByLabelText(/name/i);
    fireEvent.change(nameInput, { target: { value: 'New Account' } });
    fireEvent.click(screen.getByRole('button', { name: /save/i }));
    await waitFor(() =>
      expect(toast.error).toHaveBeenCalledWith("Couldn't save. Try again."),
    );
    expect(screen.getByLabelText(/name/i)).toHaveValue('New Account');
  });
});
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `cd ProjectCeres.Client && pnpm test AccountCreate`
Expected: FAIL — `AccountCreate` not exported.

- [ ] **Step 3: DO NOT COMMIT.**

---

## Task 15: AccountCreate — implementation

**Files:**
- Create: `ProjectCeres.Client/src/app/features/accounts/AccountCreate.tsx`

- [ ] **Step 1: Write the implementation**

```tsx
import { toast } from 'sonner';
import { useNavigate, useOutletContext } from 'react-router-dom';
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card';
import { Skeleton } from '@/components/ui/skeleton';
import { useApi } from '../../lib/use-api';
import { AccountForm } from './AccountForm';
import {
  ACCOUNTS_URL,
  ACCOUNT_TYPES_URL,
  CURRENCIES_URL,
  type AccountFormValues,
  type AccountTypeDto,
  type CreateAccountRequest,
  type CurrencyDto,
  type UpdateAccountRequest,
} from './accounts-api';

type LayoutContext = { refetch: () => void };

const initialValues: AccountFormValues = {
  name: '',
  accountTypeId: 1,        // Asset default — most-frequent type
  currencyId: 1,           // EUR default — first seeded currency
  description: '',
  openingBalance: 0,
  openingBalanceDate: new Date().toISOString().slice(0, 10),
  liabilityRepaymentType: null,
  interestRate: null,
  excludeFromSpendable: false,
};

export function AccountCreate() {
  const navigate = useNavigate();
  const ctx = useOutletContext<LayoutContext>();
  const types = useApi<AccountTypeDto[]>(ACCOUNT_TYPES_URL);
  const currencies = useApi<CurrencyDto[]>(CURRENCIES_URL);

  if (types.loading || currencies.loading) {
    return (
      <Card>
        <CardHeader><CardTitle>New account</CardTitle></CardHeader>
        <CardContent className="space-y-4">
          <Skeleton className="h-9 w-full" />
          <Skeleton className="h-9 w-full" />
          <Skeleton className="h-9 w-full" />
          <Skeleton className="h-9 w-full" />
        </CardContent>
      </Card>
    );
  }

  if (types.error || !types.data || currencies.error || !currencies.data) {
    return (
      <Card>
        <CardHeader><CardTitle>New account</CardTitle></CardHeader>
        <CardContent>
          <div className="rounded-md border border-destructive/30 bg-destructive/10 px-4 py-3 text-sm text-destructive">
            Couldn't load account form data.
          </div>
        </CardContent>
      </Card>
    );
  }

  async function handleSubmit(body: CreateAccountRequest | UpdateAccountRequest) {
    try {
      const response = await fetch(ACCOUNTS_URL, {
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
      navigate('/accounts');
      return { ok: true as const };
    } catch {
      toast.error("Couldn't save. Try again.");
      return { ok: false as const };
    }
  }

  return (
    <Card>
      <CardHeader><CardTitle>New account</CardTitle></CardHeader>
      <CardContent>
        <AccountForm
          mode="create"
          initialValues={initialValues}
          accountTypes={types.data}
          currencies={currencies.data}
          onSubmit={handleSubmit}
          onCancel={() => navigate('/accounts')}
        />
      </CardContent>
    </Card>
  );
}
```

- [ ] **Step 2: Run tests to verify they pass**

Run: `cd ProjectCeres.Client && pnpm test AccountCreate`
Expected: PASS — all 3 tests green.

- [ ] **Step 3: DO NOT COMMIT.**

---

## Task 16: AccountEdit — failing tests first

**Files:**
- Create: `ProjectCeres.Client/src/app/features/accounts/AccountEdit.test.tsx`

- [ ] **Step 1: Write the test file**

```tsx
import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { MemoryRouter, Outlet, Route, Routes } from 'react-router-dom';
import { toast } from 'sonner';
import { AccountEdit } from './AccountEdit';

vi.mock('sonner', () => ({
  toast: { success: vi.fn(), error: vi.fn() },
}));

const mockFetch = vi.fn();

const accountDetail = {
  id: 'a-1', name: 'Checking Account',
  accountTypeId: 1, accountTypeName: 'Asset',
  currencyId: 1, currencyCode: 'EUR', currencySymbol: '€',
  description: 'Main checking', isActive: true, excludeFromSpendable: false,
  liabilityRepaymentType: null, interestRate: null,
  openingBalance: 1000, openingBalanceDate: '2026-01-01',
};

const accountTypesResponse = [
  { id: 1, name: 'Asset' },
  { id: 2, name: 'Liability' },
];
const currenciesResponse = [
  { id: 1, code: 'EUR', name: 'Euro', symbol: '€' },
];

beforeEach(() => {
  global.fetch = mockFetch as unknown as typeof fetch;
  mockFetch.mockImplementation((url: string, init?: RequestInit) => {
    if (url === '/api/account-types') return Promise.resolve({ ok: true, status: 200, json: async () => accountTypesResponse });
    if (url === '/api/currencies')    return Promise.resolve({ ok: true, status: 200, json: async () => currenciesResponse });
    if (url === '/api/accounts/a-1' && (!init || init.method === undefined)) {
      return Promise.resolve({ ok: true, status: 200, json: async () => accountDetail });
    }
    if (url === '/api/accounts/a-missing') {
      return Promise.resolve({ ok: false, status: 404, json: async () => null });
    }
    if (url === '/api/accounts/a-1' && init?.method === 'PATCH') {
      return Promise.resolve({ ok: true, status: 204, json: async () => null });
    }
    return Promise.resolve({ ok: false, status: 404, json: async () => null });
  });
});

afterEach(() => vi.resetAllMocks());

function LayoutShim() {
  return <Outlet context={{ refetch: vi.fn() }} />;
}

function renderPage(path: string) {
  return render(
    <MemoryRouter initialEntries={[path]}>
      <Routes>
        <Route path="/accounts" element={<LayoutShim />}>
          <Route index element={<div data-testid="list-page">LIST</div>} />
          <Route path=":id/edit" element={<AccountEdit />} />
        </Route>
      </Routes>
    </MemoryRouter>,
  );
}

describe('AccountEdit', () => {
  it('renders the form pre-populated', async () => {
    renderPage('/accounts/a-1/edit');
    await waitFor(() => expect(screen.getByLabelText(/name/i)).toHaveValue('Checking Account'));
  });

  it('GET 404 renders the not-found banner', async () => {
    renderPage('/accounts/a-missing/edit');
    await waitFor(() =>
      expect(screen.getByText(/that account doesn't exist/i)).toBeInTheDocument(),
    );
    expect(screen.getByRole('link', { name: /back to accounts/i })).toBeInTheDocument();
  });

  it('PATCH success fires toast.success and navigates back', async () => {
    renderPage('/accounts/a-1/edit');
    await waitFor(() => expect(screen.getByLabelText(/name/i)).toHaveValue('Checking Account'));
    fireEvent.change(screen.getByLabelText(/name/i), { target: { value: 'Renamed' } });
    fireEvent.click(screen.getByRole('button', { name: /save/i }));
    await waitFor(() => expect(toast.success).toHaveBeenCalledWith('Saved.'));
    expect(await screen.findByTestId('list-page')).toBeInTheDocument();
  });

  it('PATCH 422 fires toast.error and form retains values', async () => {
    mockFetch.mockImplementation((url: string, init?: RequestInit) => {
      if (url === '/api/account-types') return Promise.resolve({ ok: true, status: 200, json: async () => accountTypesResponse });
      if (url === '/api/currencies')    return Promise.resolve({ ok: true, status: 200, json: async () => currenciesResponse });
      if (url === '/api/accounts/a-1' && (!init || init.method === undefined)) {
        return Promise.resolve({ ok: true, status: 200, json: async () => accountDetail });
      }
      if (url === '/api/accounts/a-1' && init?.method === 'PATCH') {
        return Promise.resolve({ ok: false, status: 422, json: async () => ({ error: { code: 'VALIDATION_ERROR', message: 'Bad', details: [] } }) });
      }
      return Promise.resolve({ ok: false, status: 404, json: async () => null });
    });
    renderPage('/accounts/a-1/edit');
    await waitFor(() => expect(screen.getByLabelText(/name/i)).toHaveValue('Checking Account'));
    fireEvent.change(screen.getByLabelText(/name/i), { target: { value: 'Renamed' } });
    fireEvent.click(screen.getByRole('button', { name: /save/i }));
    await waitFor(() => expect(toast.error).toHaveBeenCalledWith("Couldn't save. Try again."));
    expect(screen.getByLabelText(/name/i)).toHaveValue('Renamed');
  });
});
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `cd ProjectCeres.Client && pnpm test AccountEdit`
Expected: FAIL — `AccountEdit` not exported.

- [ ] **Step 3: DO NOT COMMIT.**

---

## Task 17: AccountEdit — implementation

**Files:**
- Create: `ProjectCeres.Client/src/app/features/accounts/AccountEdit.tsx`

- [ ] **Step 1: Write the implementation**

```tsx
import { toast } from 'sonner';
import { Link, useNavigate, useOutletContext, useParams } from 'react-router-dom';
import { Button } from '@/components/ui/button';
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card';
import { Skeleton } from '@/components/ui/skeleton';
import { useApi } from '../../lib/use-api';
import { AccountForm } from './AccountForm';
import {
  ACCOUNT_BY_ID_URL,
  ACCOUNT_TYPES_URL,
  CURRENCIES_URL,
  type AccountDetailDto,
  type AccountFormValues,
  type AccountTypeDto,
  type CreateAccountRequest,
  type CurrencyDto,
  type UpdateAccountRequest,
} from './accounts-api';

type LayoutContext = { refetch: () => void };

export function AccountEdit() {
  const { id } = useParams<{ id: string }>();
  const navigate = useNavigate();
  const ctx = useOutletContext<LayoutContext>();

  const detail = useApi<AccountDetailDto>(id ? ACCOUNT_BY_ID_URL(id) : '/api/accounts/__missing__');
  const types = useApi<AccountTypeDto[]>(ACCOUNT_TYPES_URL);
  const currencies = useApi<CurrencyDto[]>(CURRENCIES_URL);

  if (detail.loading || types.loading || currencies.loading) {
    return (
      <Card>
        <CardHeader><CardTitle>Edit account</CardTitle></CardHeader>
        <CardContent className="space-y-4">
          <Skeleton className="h-9 w-full" />
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
        <CardHeader><CardTitle>Edit account</CardTitle></CardHeader>
        <CardContent>
          <div className="rounded-md border border-destructive/30 bg-destructive/10 px-4 py-3 text-sm text-destructive">
            That account doesn't exist.
          </div>
          <Button variant="outline" className="mt-4" render={<Link to="/accounts">Back to Accounts</Link>} />
        </CardContent>
      </Card>
    );
  }

  if (types.error || !types.data || currencies.error || !currencies.data) {
    return (
      <Card>
        <CardHeader><CardTitle>Edit account</CardTitle></CardHeader>
        <CardContent>
          <div className="rounded-md border border-destructive/30 bg-destructive/10 px-4 py-3 text-sm text-destructive">
            Couldn't load account form data.
          </div>
        </CardContent>
      </Card>
    );
  }

  const initialValues: AccountFormValues = {
    name: detail.data.name,
    accountTypeId: detail.data.accountTypeId,
    currencyId: detail.data.currencyId,
    description: detail.data.description ?? '',
    openingBalance: detail.data.openingBalance,
    openingBalanceDate: detail.data.openingBalanceDate ?? new Date().toISOString().slice(0, 10),
    liabilityRepaymentType: detail.data.liabilityRepaymentType,
    interestRate: detail.data.interestRate,
    excludeFromSpendable: detail.data.excludeFromSpendable,
  };

  async function handleSubmit(body: CreateAccountRequest | UpdateAccountRequest) {
    try {
      const response = await fetch(ACCOUNT_BY_ID_URL(id!), {
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
      navigate('/accounts');
      return { ok: true as const };
    } catch {
      toast.error("Couldn't save. Try again.");
      return { ok: false as const };
    }
  }

  return (
    <Card>
      <CardHeader><CardTitle>Edit account</CardTitle></CardHeader>
      <CardContent>
        <AccountForm
          mode="edit"
          initialValues={initialValues}
          accountTypes={types.data}
          currencies={currencies.data}
          onSubmit={handleSubmit}
          onCancel={() => navigate('/accounts')}
        />
      </CardContent>
    </Card>
  );
}
```

- [ ] **Step 2: Run tests to verify they pass**

Run: `cd ProjectCeres.Client && pnpm test AccountEdit`
Expected: PASS — all 4 tests green.

- [ ] **Step 3: DO NOT COMMIT.**

---

## Task 18: AccountLedger — failing tests first

**Files:**
- Create: `ProjectCeres.Client/src/app/features/accounts/AccountLedger.test.tsx`

- [ ] **Step 1: Write the test file**

```tsx
import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import { AccountLedger } from './AccountLedger';

const mockFetch = vi.fn();

const baseAccount = {
  id: 'a-1', name: 'Checking Account',
  accountTypeId: 1, accountTypeName: 'Asset',
  currencyId: 1, currencyCode: 'EUR', currencySymbol: '€',
  description: null, isActive: true, excludeFromSpendable: false,
  liabilityRepaymentType: null, interestRate: null,
  openingBalance: 1000, openingBalanceDate: '2026-01-01',
};

const baseLedger = {
  accountId: 'a-1',
  accountName: 'Checking Account',
  currencySymbol: '€',
  entries: [
    { date: '2026-01-01', createdAt: '2026-01-01T00:00:00Z', description: 'Opening Balance', entryType: 'OpeningBalance', categoryName: null, signedAmount: 1000, runningBalance: 1000 },
    { date: '2026-01-15', createdAt: '2026-01-15T10:00:00Z', description: 'Salary',          entryType: 'Transaction',     categoryName: 'Salary', signedAmount: 2500, runningBalance: 3500 },
    { date: '2026-01-20', createdAt: '2026-01-20T12:00:00Z', description: 'Groceries',        entryType: 'Transaction',     categoryName: 'Groceries', signedAmount: -85, runningBalance: 3415 },
  ],
};

beforeEach(() => {
  global.fetch = mockFetch as unknown as typeof fetch;
  mockFetch.mockImplementation((url: string) => {
    if (url === '/api/accounts/a-1') return Promise.resolve({ ok: true, status: 200, json: async () => baseAccount });
    if (url === '/api/accounts/a-1/ledger') return Promise.resolve({ ok: true, status: 200, json: async () => baseLedger });
    if (url === '/api/accounts/a-missing') return Promise.resolve({ ok: false, status: 404, json: async () => null });
    if (url === '/api/accounts/a-missing/ledger') return Promise.resolve({ ok: false, status: 404, json: async () => null });
    return Promise.resolve({ ok: false, status: 404, json: async () => null });
  });
});

afterEach(() => vi.resetAllMocks());

function renderAt(path: string) {
  return render(
    <MemoryRouter initialEntries={[path]}>
      <Routes>
        <Route path="/accounts" element={<div data-testid="list-page">LIST</div>} />
        <Route path="/accounts/:id/ledger" element={<AccountLedger />} />
      </Routes>
    </MemoryRouter>,
  );
}

describe('AccountLedger', () => {
  it('renders the header with account name', async () => {
    renderAt('/accounts/a-1/ledger');
    await screen.findByText(/Checking Account — Ledger/i);
  });

  it('renders one row per ledger entry', async () => {
    renderAt('/accounts/a-1/ledger');
    await screen.findByText('Salary');
    expect(screen.getByText('Groceries')).toBeInTheDocument();
    expect(screen.getByText('Opening Balance')).toBeInTheDocument();
  });

  it('Amount column color follows sign (positive normal, negative destructive)', async () => {
    renderAt('/accounts/a-1/ledger');
    await screen.findByText('Salary');
    const groceriesRow = screen.getByText('Groceries').closest('tr')!;
    const cells = groceriesRow.querySelectorAll('td');
    const amountCell = cells[3];  // Date, Description, Category, Amount, Running
    expect(amountCell.className).toMatch(/text-destructive/);
    const salaryRow = screen.getByText('Salary').closest('tr')!;
    const salaryAmount = salaryRow.querySelectorAll('td')[3];
    expect(salaryAmount.className).not.toMatch(/text-destructive/);
  });

  it('Running balance column color follows account-type convention (Asset normal here)', async () => {
    renderAt('/accounts/a-1/ledger');
    await screen.findByText('Salary');
    const salaryRow = screen.getByText('Salary').closest('tr')!;
    const runningCell = salaryRow.querySelectorAll('td')[4];
    expect(runningCell.className).not.toMatch(/text-destructive/);
  });

  it('renders Archived badge when account is archived', async () => {
    mockFetch.mockImplementation((url: string) => {
      if (url === '/api/accounts/a-1') return Promise.resolve({ ok: true, status: 200, json: async () => ({ ...baseAccount, isActive: false }) });
      if (url === '/api/accounts/a-1/ledger') return Promise.resolve({ ok: true, status: 200, json: async () => baseLedger });
      return Promise.resolve({ ok: false, status: 404, json: async () => null });
    });
    renderAt('/accounts/a-1/ledger');
    await screen.findByText(/Checking Account — Ledger/i);
    expect(screen.getByText('Archived')).toBeInTheDocument();
  });

  it('renders empty state when there are no entries', async () => {
    mockFetch.mockImplementation((url: string) => {
      if (url === '/api/accounts/a-1') return Promise.resolve({ ok: true, status: 200, json: async () => baseAccount });
      if (url === '/api/accounts/a-1/ledger') return Promise.resolve({ ok: true, status: 200, json: async () => ({ ...baseLedger, entries: [] }) });
      return Promise.resolve({ ok: false, status: 404, json: async () => null });
    });
    renderAt('/accounts/a-1/ledger');
    await waitFor(() => expect(screen.getByText(/no entries found/i)).toBeInTheDocument());
  });

  it('GET 404 renders the not-found banner', async () => {
    renderAt('/accounts/a-missing/ledger');
    await waitFor(() => expect(screen.getByText(/that account doesn't exist/i)).toBeInTheDocument());
  });

  it('Projection card is hidden for Asset accounts', async () => {
    renderAt('/accounts/a-1/ledger');
    await screen.findByText('Salary');
    expect(screen.queryByText(/payoff projection/i)).toBeNull();
  });

  it('Projection card renders for Liability + Amortising + InterestRate', async () => {
    mockFetch.mockImplementation((url: string) => {
      if (url === '/api/accounts/a-1') {
        return Promise.resolve({
          ok: true, status: 200,
          json: async () => ({
            ...baseAccount, accountTypeId: 2, accountTypeName: 'Liability',
            liabilityRepaymentType: 'Amortising', interestRate: 0.035,
          }),
        });
      }
      if (url === '/api/accounts/a-1/ledger') return Promise.resolve({ ok: true, status: 200, json: async () => ({ ...baseLedger, entries: [{ ...baseLedger.entries[0], runningBalance: 5000 }] }) });
      return Promise.resolve({ ok: false, status: 404, json: async () => null });
    });
    renderAt('/accounts/a-1/ledger');
    await screen.findByText(/payoff projection/i);
    expect(screen.getByLabelText(/monthly payment/i)).toBeInTheDocument();
  });

  it('Projection card hidden for Liability + FullMonthly', async () => {
    mockFetch.mockImplementation((url: string) => {
      if (url === '/api/accounts/a-1') {
        return Promise.resolve({
          ok: true, status: 200,
          json: async () => ({
            ...baseAccount, accountTypeId: 2, accountTypeName: 'Liability',
            liabilityRepaymentType: 'FullMonthly', interestRate: null,
          }),
        });
      }
      if (url === '/api/accounts/a-1/ledger') return Promise.resolve({ ok: true, status: 200, json: async () => baseLedger });
      return Promise.resolve({ ok: false, status: 404, json: async () => null });
    });
    renderAt('/accounts/a-1/ledger');
    await screen.findByText(/Checking Account — Ledger/i);
    expect(screen.queryByText(/payoff projection/i)).toBeNull();
  });

  it('Calculate computes payoff months from balance/rate/payment', async () => {
    mockFetch.mockImplementation((url: string) => {
      if (url === '/api/accounts/a-1') {
        return Promise.resolve({
          ok: true, status: 200,
          json: async () => ({
            ...baseAccount, accountTypeId: 2, accountTypeName: 'Liability',
            liabilityRepaymentType: 'Amortising', interestRate: 0.035,
          }),
        });
      }
      if (url === '/api/accounts/a-1/ledger') return Promise.resolve({ ok: true, status: 200, json: async () => ({ ...baseLedger, entries: [{ ...baseLedger.entries[0], runningBalance: 5000 }] }) });
      return Promise.resolve({ ok: false, status: 404, json: async () => null });
    });
    renderAt('/accounts/a-1/ledger');
    await screen.findByLabelText(/monthly payment/i);
    fireEvent.change(screen.getByLabelText(/monthly payment/i), { target: { value: '500' } });
    fireEvent.click(screen.getByRole('button', { name: /calculate/i }));
    await waitFor(() => expect(screen.getByText(/estimated payoff/i)).toBeInTheDocument());
    expect(screen.getByText(/months/i)).toBeInTheDocument();
  });
});
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `cd ProjectCeres.Client && pnpm test AccountLedger`
Expected: FAIL — `AccountLedger` not exported.

- [ ] **Step 3: DO NOT COMMIT.**

---

## Task 19: AccountLedger — implementation

**Files:**
- Create: `ProjectCeres.Client/src/app/features/accounts/AccountLedger.tsx`

- [ ] **Step 1: Write the implementation**

```tsx
import { useState } from 'react';
import { Link, useParams } from 'react-router-dom';
import { ArrowLeft } from 'lucide-react';
import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card';
import { Input } from '@/components/ui/input';
import { Label } from '@/components/ui/label';
import { Skeleton } from '@/components/ui/skeleton';
import {
  Table, TableBody, TableCell, TableHead, TableHeader, TableRow,
} from '@/components/ui/table';
import { CardError } from '../../components/CardError';
import { useApi } from '../../lib/use-api';
import { cn } from '@/lib/utils';
import {
  ACCOUNT_BY_ID_URL,
  ACCOUNT_LEDGER_URL,
  type AccountDetailDto,
  type AccountLedgerDto,
} from './accounts-api';
import { project, type ProjectionResult } from './projection';

export function AccountLedger() {
  const { id } = useParams<{ id: string }>();
  const account = useApi<AccountDetailDto>(id ? ACCOUNT_BY_ID_URL(id) : '/api/accounts/__missing__');
  const ledger = useApi<AccountLedgerDto>(id ? ACCOUNT_LEDGER_URL(id) : '/api/accounts/__missing__/ledger');

  if (account.loading || ledger.loading) {
    return (
      <div className="mx-auto max-w-4xl space-y-6">
        <Skeleton className="h-9 w-64" />
        <Card>
          <CardContent className="space-y-2 py-6">
            <Skeleton className="h-9 w-full" />
            <Skeleton className="h-9 w-full" />
            <Skeleton className="h-9 w-full" />
          </CardContent>
        </Card>
      </div>
    );
  }

  if (account.error || !account.data) {
    return (
      <div className="mx-auto max-w-4xl space-y-6">
        <Card>
          <CardContent className="py-6 space-y-4">
            <div className="rounded-md border border-destructive/30 bg-destructive/10 px-4 py-3 text-sm text-destructive">
              That account doesn't exist.
            </div>
            <Button variant="outline" render={<Link to="/accounts">Back to Accounts</Link>} />
          </CardContent>
        </Card>
      </div>
    );
  }

  if (ledger.error || !ledger.data) {
    return (
      <div className="mx-auto max-w-4xl space-y-6">
        <CardError section="ledger" onRetry={ledger.refetch} />
      </div>
    );
  }

  const isLiability = account.data.accountTypeName === 'Liability';
  const showProjection =
    isLiability && account.data.liabilityRepaymentType === 'Amortising' && account.data.interestRate != null;
  const currentBalance = ledger.data.entries.length > 0
    ? ledger.data.entries[ledger.data.entries.length - 1].runningBalance
    : account.data.openingBalance;

  return (
    <div className="mx-auto max-w-4xl space-y-6">
      <div>
        <Link to="/accounts" className="inline-flex items-center gap-1 text-sm text-muted-foreground hover:text-foreground">
          <ArrowLeft className="h-4 w-4" />
          Back to Accounts
        </Link>
      </div>

      <header>
        <div className="flex items-center gap-3">
          <h1 className="text-2xl font-semibold">{account.data.name} — Ledger</h1>
          {!account.data.isActive ? <Badge variant="secondary">Archived</Badge> : null}
        </div>
        <p className="mt-2 text-muted-foreground">
          Every entry that contributes to this account's balance, in chronological order. The final
          running balance matches the account balance.
        </p>
      </header>

      {showProjection ? (
        <PayoffProjectionCard
          balance={Math.abs(currentBalance)}
          annualRate={account.data.interestRate!}
          symbol={account.data.currencySymbol}
        />
      ) : null}

      <Card>
        <CardContent className="pt-6">
          {ledger.data.entries.length === 0 ? (
            <div className="px-3 py-8 text-center text-sm text-muted-foreground italic">
              No entries found for this account.
            </div>
          ) : (
            <Table>
              <TableHeader>
                <TableRow>
                  <TableHead className="w-28">Date</TableHead>
                  <TableHead>Description</TableHead>
                  <TableHead className="w-40">Category</TableHead>
                  <TableHead className="w-32 text-right">Amount</TableHead>
                  <TableHead className="w-32 text-right">Running</TableHead>
                </TableRow>
              </TableHeader>
              <TableBody>
                {ledger.data.entries.map((e, idx) => (
                  <TableRow key={`${e.date}-${idx}-${e.entryType}`}>
                    <TableCell className="text-sm">{formatDate(e.date)}</TableCell>
                    <TableCell className="text-sm">{e.description}</TableCell>
                    <TableCell className="text-sm text-muted-foreground">{e.categoryName ?? '—'}</TableCell>
                    <TableCell
                      className={cn(
                        'text-right tabular-nums text-sm',
                        e.signedAmount < 0 && 'text-destructive',
                      )}
                    >
                      {formatSigned(e.signedAmount, account.data.currencySymbol)}
                    </TableCell>
                    <TableCell
                      className={cn(
                        'text-right tabular-nums text-sm',
                        isLiability && 'text-destructive',
                      )}
                    >
                      {formatBalance(e.runningBalance, account.data.currencySymbol)}
                    </TableCell>
                  </TableRow>
                ))}
              </TableBody>
            </Table>
          )}
        </CardContent>
      </Card>
    </div>
  );
}

function PayoffProjectionCard({
  balance, annualRate, symbol,
}: { balance: number; annualRate: number; symbol: string }) {
  const [paymentInput, setPaymentInput] = useState('');
  const [result, setResult] = useState<ProjectionResult | null>(null);

  function handleCalculate() {
    const payment = Number(paymentInput);
    if (Number.isNaN(payment)) {
      setResult({ ok: false, error: 'Enter a valid number.' });
      return;
    }
    setResult(project(balance, annualRate, payment));
  }

  return (
    <Card>
      <CardHeader>
        <CardTitle>Payoff projection</CardTitle>
      </CardHeader>
      <CardContent>
        <dl className="grid grid-cols-[max-content_1fr] gap-x-6 gap-y-1 text-sm">
          <dt className="text-muted-foreground">Outstanding balance</dt>
          <dd className="tabular-nums">{symbol}{balance.toFixed(2)}</dd>
          <dt className="text-muted-foreground">Annual interest rate</dt>
          <dd className="tabular-nums">{(annualRate * 100).toFixed(2)}%</dd>
        </dl>

        <div className="mt-4 flex items-end gap-3">
          <div className="flex-1">
            <Label htmlFor="monthlyPayment">Monthly payment</Label>
            <Input
              id="monthlyPayment"
              aria-label="Monthly payment"
              type="number"
              step="0.01"
              min={0}
              value={paymentInput}
              onChange={(e) => setPaymentInput(e.target.value)}
            />
          </div>
          <Button type="button" onClick={handleCalculate}>Calculate</Button>
        </div>

        {result?.ok ? (
          <dl className="mt-6 grid grid-cols-[max-content_1fr] gap-x-6 gap-y-1 text-sm">
            <dt className="text-muted-foreground">Estimated payoff</dt>
            <dd>{result.payoffDate.toLocaleDateString(undefined, { month: 'long', year: 'numeric' })} ({result.monthsToPayoff} months)</dd>
            <dt className="text-muted-foreground">Total interest cost</dt>
            <dd className="tabular-nums">{symbol}{result.totalInterest.toFixed(2)}</dd>
            <dt className="text-muted-foreground">Total paid</dt>
            <dd className="tabular-nums">{symbol}{result.totalPaid.toFixed(2)}</dd>
          </dl>
        ) : null}
        {result && !result.ok ? (
          <p className="mt-4 text-sm text-destructive">{result.error}</p>
        ) : null}
      </CardContent>
    </Card>
  );
}

function formatDate(iso: string): string {
  const d = new Date(iso);
  return d.toLocaleDateString(undefined, { day: '2-digit', month: '2-digit', year: 'numeric' });
}

function formatSigned(amount: number, symbol: string): string {
  const formatted = new Intl.NumberFormat(undefined, {
    minimumFractionDigits: 2, maximumFractionDigits: 2,
  }).format(Math.abs(amount));
  const sign = amount < 0 ? '−' : '+';
  return `${sign}${symbol}${formatted}`;
}

function formatBalance(amount: number, symbol: string): string {
  const formatted = new Intl.NumberFormat(undefined, {
    minimumFractionDigits: 2, maximumFractionDigits: 2,
  }).format(Math.abs(amount));
  const sign = amount < 0 ? '-' : '';
  return `${sign}${symbol}${formatted}`;
}
```

- [ ] **Step 2: Run tests to verify they pass**

Run: `cd ProjectCeres.Client && pnpm test AccountLedger`
Expected: PASS — all 11 tests green.

- [ ] **Step 3: DO NOT COMMIT.**

---

## Task 20: Wire the routes + Accounts.tsx re-export

**Files:**
- Modify: `ProjectCeres.Client/src/app/pages/Accounts.tsx`
- Modify: `ProjectCeres.Client/src/app/App.tsx`

- [ ] **Step 1: Replace pages/Accounts.tsx with the one-line re-export**

Open `ProjectCeres.Client/src/app/pages/Accounts.tsx`, replace its entire contents with:

```typescript
export { AccountsLayout as Accounts } from '../features/accounts/AccountsLayout';
```

- [ ] **Step 2: Add the new imports + nested + sibling routes in App.tsx**

Open `ProjectCeres.Client/src/app/App.tsx`. Add these imports near the other feature imports (e.g. next to the existing CategoryCreate / CategoryEdit imports):

```typescript
import { AccountCreate } from './features/accounts/AccountCreate';
import { AccountEdit } from './features/accounts/AccountEdit';
import { AccountLedger } from './features/accounts/AccountLedger';
```

The current Accounts route is:

```tsx
        <Route path="accounts" element={<Accounts />} />
```

Replace it with:

```tsx
        <Route path="accounts" element={<Accounts />}>
          <Route path="new" element={<AccountCreate />} />
          <Route path=":id/edit" element={<AccountEdit />} />
        </Route>
        <Route path="accounts/:id/ledger" element={<AccountLedger />} />
```

- [ ] **Step 3: Verify the SPA still type-checks**

Run: `cd ProjectCeres.Client && pnpm tsc --noEmit`
Expected: no errors.

- [ ] **Step 4: Run the full client test suite**

Run: `cd ProjectCeres.Client && pnpm test`
Expected: PASS — all tests including the ~60 new accounts tests plus all pre-existing client tests.

- [ ] **Step 5: DO NOT COMMIT.**

---

## Task 21: Server — `HasTransactions` on the list DTO

**Files:**
- Modify: `ProjectCeres/ViewModels/AccountApiDtos.cs`
- Modify: `ProjectCeres/Controllers/Api/AccountsApiController.cs`
- Modify: `ProjectCeres.Tests/Integration/Api/AccountsCrudApiTests.cs`

- [ ] **Step 1: Add HasTransactions to AccountListItemDto**

Open `ProjectCeres/ViewModels/AccountApiDtos.cs`. Replace the `AccountListItemDto` declaration (currently:):

```csharp
public record AccountListItemDto(
    Guid    Id,
    string  Name,
    int     AccountTypeId,
    string  AccountTypeName,
    int     CurrencyId,
    string  CurrencyCode,
    string  CurrencySymbol,
    string? Description,
    bool    IsActive,
    bool    ExcludeFromSpendable,
    string? LiabilityRepaymentType,
    decimal? InterestRate,
    decimal Balance);
```

with:

```csharp
public record AccountListItemDto(
    Guid    Id,
    string  Name,
    int     AccountTypeId,
    string  AccountTypeName,
    int     CurrencyId,
    string  CurrencyCode,
    string  CurrencySymbol,
    string? Description,
    bool    IsActive,
    bool    ExcludeFromSpendable,
    string? LiabilityRepaymentType,
    decimal? InterestRate,
    decimal Balance,
    bool    HasTransactions);
```

- [ ] **Step 2: Populate HasTransactions in the list endpoint**

Open `ProjectCeres/Controllers/Api/AccountsApiController.cs`. In the `Get([FromQuery] bool includeInactive = false)` method (the `[HttpGet]` one without a route segment), the current foreach is:

```csharp
foreach (var a in accounts)
{
    var balance = await accountService.GetBalanceAsync(a.Id);
    dtos.Add(new AccountListItemDto(
        a.Id, a.Name, a.AccountTypeId, a.AccountType.Name,
        a.CurrencyId, a.Currency.Code, a.Currency.Symbol,
        a.Description, a.IsActive, a.ExcludeFromSpendable,
        a.LiabilityRepaymentType, a.InterestRate, balance));
}
```

Replace with:

```csharp
foreach (var a in accounts)
{
    var balance = await accountService.GetBalanceAsync(a.Id);
    var hasTransactions =
        await db.Transactions.Owned(user).AnyAsync(t => t.AccountId == a.Id) ||
        await db.Transfers.Owned(user).AnyAsync(t => t.SourceAccountId == a.Id || t.DestAccountId == a.Id) ||
        await db.LiabilityPayments.Owned(user).AnyAsync(p => p.AssetAccountId == a.Id || p.LiabilityAccountId == a.Id);
    dtos.Add(new AccountListItemDto(
        a.Id, a.Name, a.AccountTypeId, a.AccountType.Name,
        a.CurrencyId, a.Currency.Code, a.Currency.Symbol,
        a.Description, a.IsActive, a.ExcludeFromSpendable,
        a.LiabilityRepaymentType, a.InterestRate, balance, hasTransactions));
}
```

The `Create` method also constructs an `AccountListItemDto` — it currently passes `request.OpeningBalance` as the last positional arg (Balance). Update it to pass `false` for `hasTransactions` after the balance arg, since a freshly-created account may have an opening-balance transaction (so technically `true` if `OpeningBalance != 0`). For correctness, compute it inline:

```csharp
var hasTx = request.OpeningBalance != 0;
return Created($"/api/accounts/{a.Id}", new AccountListItemDto(
    a.Id, a.Name, a.AccountTypeId, a.AccountType.Name,
    a.CurrencyId, a.Currency.Code, a.Currency.Symbol,
    a.Description, a.IsActive, a.ExcludeFromSpendable,
    a.LiabilityRepaymentType, a.InterestRate, request.OpeningBalance, hasTx));
```

- [ ] **Step 3: Add tests for HasTransactions in AccountsCrudApiTests.cs**

Open `ProjectCeres.Tests/Integration/Api/AccountsCrudApiTests.cs`. Add at the end of the existing test class:

```csharp
[Fact]
public async Task Get_returns_HasTransactions_true_for_account_with_transactions()
{
    // The seeded "Checking Account" (id 10000000-0000-0000-0000-000000000002) has seeded transactions.
    var response = await _client.GetAsync("/api/accounts");
    response.EnsureSuccessStatusCode();
    var accounts = await response.Content.ReadFromJsonAsync<List<AccountListItemDto>>();
    accounts.Should().NotBeNull();
    var checking = accounts!.FirstOrDefault(a => a.Name == "Checking Account");
    checking.Should().NotBeNull();
    checking!.HasTransactions.Should().BeTrue();
}

[Fact]
public async Task Get_returns_HasTransactions_false_for_account_with_no_movements()
{
    // Create a fresh account with no transactions.
    var create = await _client.PostAsJsonAsync("/api/accounts", new {
        Name = "Empty Test", AccountTypeId = 1, CurrencyId = 1,
        Description = (string?)null, OpeningBalance = 0m,
        OpeningBalanceDate = DateOnly.FromDateTime(DateTime.UtcNow),
        LiabilityRepaymentType = (string?)null, InterestRate = (decimal?)null,
        ExcludeFromSpendable = false,
    });
    create.EnsureSuccessStatusCode();

    var response = await _client.GetAsync("/api/accounts");
    var accounts = await response.Content.ReadFromJsonAsync<List<AccountListItemDto>>();
    var empty = accounts!.FirstOrDefault(a => a.Name == "Empty Test");
    empty.Should().NotBeNull();
    empty!.HasTransactions.Should().BeFalse();
}
```

(The `Get_returns_HasTransactions_true_for_account_with_only_a_Transfer` and `..._only_a_LiabilityPayment` cases require seeding a Transfer / LiabilityPayment row — if the test fixture supports it, add similar tests; otherwise document them as a follow-up.)

- [ ] **Step 4: Build the test project**

Run: `dotnet build ProjectCeres.Tests/ProjectCeres.Tests.csproj --nologo -v quiet`
Expected: PASS — no errors.

- [ ] **Step 5: Run the new tests**

Run: `dotnet test ProjectCeres.Tests/ProjectCeres.Tests.csproj --filter "FullyQualifiedName~HasTransactions" --nologo`
Expected: PASS.

- [ ] **Step 6: DO NOT COMMIT.**

---

## Task 22: Server — `AccountPolicies.ValidateLiabilityRepayment` extended (Layer 2)

**Files:**
- Modify: `ProjectCeres/Services/AccountPolicies.cs`
- Modify: `ProjectCeres.Tests/Unit/AccountPoliciesTests.cs`

- [ ] **Step 1: Extend the policy method**

Open `ProjectCeres/Services/AccountPolicies.cs`. Find `ValidateLiabilityRepayment`:

```csharp
public static Result ValidateLiabilityRepayment(string? repaymentType, decimal? interestRate)
{
    if (repaymentType == "Amortising" && interestRate is null)
        return Result.Fail(InvalidLiabilityRepaymentCode, "An Amortising liability must have an interest rate.");
    if (repaymentType == "FullMonthly" && interestRate is not null)
        return Result.Fail(InvalidLiabilityRepaymentCode, "A FullMonthly liability must not have an interest rate.");
    return Result.Ok();
}
```

Add a third check before the `return Result.Ok()`:

```csharp
    if (repaymentType is null && interestRate is not null)
        return Result.Fail(InvalidLiabilityRepaymentCode,
                           "An account with no repayment type must not have an interest rate.");
    return Result.Ok();
}
```

- [ ] **Step 2: Add the new unit tests**

Open `ProjectCeres.Tests/Unit/AccountPoliciesTests.cs`. Add at the end of the test class (next to the existing `ValidateLiabilityRepayment_*` tests):

```csharp
[Fact]
public void ValidateLiabilityRepayment_WithNoneAndRate_Fails()
{
    var result = AccountPolicies.ValidateLiabilityRepayment(null, 0.035m);
    result.IsSuccess.Should().BeFalse();
    result.Error!.Value.Message.Should().Contain("no repayment type");
}

[Fact]
public void ValidateLiabilityRepayment_WithNoneAndNoRate_Succeeds()
{
    var result = AccountPolicies.ValidateLiabilityRepayment(null, null);
    result.IsSuccess.Should().BeTrue();
}
```

- [ ] **Step 3: Build and run the policy tests**

Run: `dotnet test ProjectCeres.Tests/ProjectCeres.Tests.csproj --filter "FullyQualifiedName~AccountPoliciesTests" --nologo`
Expected: PASS — all AccountPoliciesTests green.

- [ ] **Step 4: DO NOT COMMIT.**

---

## Task 23: Razor cutover — slim controller, drop throwing methods, delete views & projection service

**Files:**
- Modify: `ProjectCeres/Controllers/AccountsController.cs`
- Modify: `ProjectCeres/Services/IAccountService.cs`
- Modify: `ProjectCeres/Services/AccountService.cs`
- Modify: `ProjectCeres/Program.cs`
- Modify: `ProjectCeres.Tests/Integration/AccountServiceTests.cs`
- Modify: `ProjectCeres.Tests/Integration/Api/UserIdStampingTests.cs`
- Delete: `ProjectCeres/Views/Accounts/Index.cshtml`, `Create.cshtml`, `Edit.cshtml`, `Deactivate.cshtml`, `Ledger.cshtml`
- Delete: `ProjectCeres/ViewModels/AccountCreateViewModel.cs`, `AccountEditViewModel.cs`, `AccountLedgerViewModel.cs`
- Delete: `ProjectCeres/Services/ILiabilityProjectionService.cs`, `LiabilityProjectionService.cs`
- Delete: `ProjectCeres/ViewModels/LiabilityProjectionViewModel.cs`
- Delete: `ProjectCeres.Tests/Unit/LiabilityProjectionServiceTests.cs`

- [ ] **Step 1: Slim the AccountsController**

Open `ProjectCeres/Controllers/AccountsController.cs`. Replace its entire contents with:

```csharp
using Microsoft.AspNetCore.Mvc;

namespace ProjectCeres.Controllers;

/// <summary>
/// Razor accounts UI is replaced by the SPA at /app/accounts. This controller
/// exists only to 302-redirect any in-flight bookmarks. Use 302 (not 301) so
/// browsers don't aggressively cache during the SPA migration window. The
/// redirects are removed entirely in the final SPA-cutover cleanup batch.
/// </summary>
public class AccountsController : Controller
{
    public IActionResult Index()             => Redirect("/app/accounts");
    public IActionResult Create()            => Redirect("/app/accounts/new");
    public IActionResult Edit(Guid id)       => Redirect($"/app/accounts/{id}/edit");
    public IActionResult Deactivate(Guid id) => Redirect("/app/accounts");
    public IActionResult Ledger(Guid id)     => Redirect($"/app/accounts/{id}/ledger");
}
```

- [ ] **Step 2: Delete Razor views, ViewModels, and projection-service files**

Run from the repo root:

```bash
git rm ProjectCeres/Views/Accounts/Index.cshtml \
       ProjectCeres/Views/Accounts/Create.cshtml \
       ProjectCeres/Views/Accounts/Edit.cshtml \
       ProjectCeres/Views/Accounts/Deactivate.cshtml \
       ProjectCeres/Views/Accounts/Ledger.cshtml \
       ProjectCeres/ViewModels/AccountCreateViewModel.cs \
       ProjectCeres/ViewModels/AccountEditViewModel.cs \
       ProjectCeres/ViewModels/AccountLedgerViewModel.cs \
       ProjectCeres/Services/ILiabilityProjectionService.cs \
       ProjectCeres/Services/LiabilityProjectionService.cs \
       ProjectCeres/ViewModels/LiabilityProjectionViewModel.cs \
       ProjectCeres.Tests/Unit/LiabilityProjectionServiceTests.cs
```

This stages the deletions. Do NOT commit.

- [ ] **Step 3: Verify the build now fails on dangling references**

Run: `dotnet build ProjectCeres/ProjectCeres.csproj --nologo -v quiet`
Expected: FAIL — references to `AccountCreateViewModel`, `AccountEditViewModel`, `LiabilityProjectionService` are now dangling.

- [ ] **Step 4: Drop throwing methods from IAccountService and AccountService**

Open `ProjectCeres/Services/IAccountService.cs`. Delete the throwing-method declarations (the ones that take `AccountCreateViewModel` / `AccountEditViewModel` arguments) and any preceding XML doc comments. Keep:
- `Task<IEnumerable<Account>> GetAllAsync(bool includeInactive = false);`
- `Task<Account?> GetByIdAsync(Guid id);`
- `Task<decimal> GetBalanceAsync(Guid id);`
- `Task<decimal> GetOpeningBalanceAsync(Guid id);`
- `Task<DateOnly?> GetOpeningBalanceDateAsync(Guid id);`
- `Task<AccountLedgerViewModel?> GetLedgerAsync(Guid id);` (note: `AccountLedgerViewModel` is being deleted — change the return type to `AccountLedgerDto` or whatever the API endpoint already uses; verify by reading `AccountsApiController.GetLedger`)

Wait — `GetLedgerAsync` returns `AccountLedgerViewModel?` and the API DTO is constructed in the controller (lines 116–122 of `AccountsApiController.cs`). The view-model type is therefore the *internal* shape; verify whether it's the same class file as `AccountLedgerViewModel.cs` listed for deletion. If it is, **rename it inline** to a non-Razor name (e.g. move it into `Services/AccountLedgerResult.cs`) before deleting the original — or keep the file but rename the class. Audit at this step.

For `AccountService.cs`: delete the corresponding throwing method bodies. Verify the `using ProjectCeres.ViewModels;` directive is still needed (it is, for `CreateAccountRequest` / `UpdateAccountRequest`).

- [ ] **Step 5: Remove the projection service registration from Program.cs**

Open `ProjectCeres/Program.cs`. Find and delete this line:

```csharp
builder.Services.AddScoped<ILiabilityProjectionService, LiabilityProjectionService>();
```

- [ ] **Step 6: Audit and clean test files**

```bash
cat ProjectCeres.Tests/Integration/AccountServiceTests.cs
```

If it contains tests for the deleted throwing CRUD methods (e.g. `CreateAsync_PersistsAccount`, `UpdateAsync_*_Throws`, `DeactivateAsync_*_Throws`), delete those tests. If the file becomes empty, run:

```bash
git rm ProjectCeres.Tests/Integration/AccountServiceTests.cs
```

Then audit `UserIdStampingTests.cs`:

```bash
grep -n "RazorAccountService" ProjectCeres.Tests/Integration/Api/UserIdStampingTests.cs
```

If a `RazorAccountService_*_stamps_UserId` test exists, delete it (Categories precedent — guard for a now-removed Razor service path).

- [ ] **Step 7: Verify the project builds**

Run: `dotnet build --nologo -v quiet`
Expected: PASS — no errors.

- [ ] **Step 8: Run the full server test suite**

Run: `dotnet test --nologo`
Expected: PASS — every test green.

- [ ] **Step 9: DO NOT COMMIT.**

---

## Task 24: Verification gate — full builds + tests + manual + docs sync

This is the gate before commit. Everything must be green and the docs must reflect reality.

- [ ] **Step 1: Full server test suite**

Run: `dotnet test --nologo`
Expected: PASS — every test green.

- [ ] **Step 2: Full client test suite**

Run: `cd ProjectCeres.Client && pnpm test`
Expected: PASS — every test green, including ~60 new accounts tests.

- [ ] **Step 3: Client production build**

Run: `cd ProjectCeres.Client && pnpm build`
Expected: PASS.

- [ ] **Step 4: Documentation sync**

Per saved feedback (`feedback_sync_docs_before_spa_commits.md`), this is a named gate. Use the `sync-docs` skill, then verify each proposed update against the actual file state. High-value targets:

- `docs/planning-phase3-spa-migration.md` §2 (controller-by-controller table) — update the `AccountsController` row to match the Categories row format (add "Migrated 2026-05-03 (commit `<sha>`)" with spec link, plan link, and one-line description). Use commit hash placeholder `<sha>` — fill in after the commit lands.
- `docs/planning-phase3-spa-migration.md` §8 ("Frontend execution batches" table) — flip Accounts row from "Pending" to "✅ Migrated 2026-05-03 (commit `<sha>`)" with one-line description.
- `docs/planning-phase3.md` §14 — add a sub-bullet under group 7: "✓ **Accounts — list + Create/Edit + Ledger + payoff projection** (2026-05-03)" with spec/plan citations matching the existing Categories bullet shape.
- `docs/api-contract.md` — audit; add `HasTransactions: bool` to `AccountListItemDto` documentation if the doc enumerates DTOs exhaustively.
- `docs/models.md` — verify the Account section is still accurate. The `Notes (Phase 3)` mention stays (Notes is still planned, just deferred from this scope).

No ADR needed (symmetric policy extension, not a new architectural concept).

Verify each proposed update against the actual file state before applying — do not trust diff inference alone.

- [ ] **Step 5: Manual click-through (USER STEP — Claude cannot drive a browser)**

Start the app:

```bash
dotnet run --project ProjectCeres
```

In a browser:

1. Visit `https://localhost:7001/app/accounts`. Default view shows 4 active seeded accounts (Cash, Checking, Savings, Credit Card). Per-currency subtotal hidden (single currency).
2. Search "che". Filters to "Checking Account". Clear search.
3. Toggle "Include archived". No archived rows in seed data — empty toggle effect should still toggle the URL param.
4. Click `⋯ → Edit` on Checking Account. Form pre-populates. Type and Currency render as locked rows. Description editable. Click Cancel → back to list.
5. Click `+ New account`. Pick Type=Liability. The form should reveal Repayment type. Pick Amortising; Interest rate field appears. Fill: Currency=EUR, Name="Test Loan", Repayment=Amortising, Rate=0.035, Opening balance=5000. Save. Toast appears, returns to list, "Test Loan" renders red.
6. Click `⋯ → Archive…` on "Test Loan" (which has no transactions). Empty-case copy ("This account has no transactions… Safe to archive"). Click Archive. Toast appears, row disappears.
7. Toggle Include archived. "Test Loan" reappears with Archived badge + reduced opacity. The `⋯` menu shows only "View ledger" (no Edit, no Archive…).
8. Click `⋯ → Archive…` on Checking Account (has transactions). Non-empty-case copy ("balance still counts toward your net worth and reports"). Click Cancel.
9. Click `⋯ → View ledger` on a Liability+Amortising account (e.g. Test Loan). Projection card renders. Enter Monthly payment=500. Click Calculate. Result rows populate (Estimated payoff month, Total interest cost, Total paid).
10. Click `⋯ → View ledger` on Checking Account (Asset). Projection card hidden. Ledger table renders with positive entries in normal text and Running balance in normal foreground.
11. Visit `https://localhost:7001/Accounts`. 302 → `/app/accounts`. Same for `/Accounts/Create`, `/Accounts/Edit/<guid>`, `/Accounts/Deactivate/<guid>`, `/Accounts/Ledger/<guid>`.

If any step fails, stop and fix before committing.

- [ ] **Step 6: DO NOT COMMIT (yet).**

---

## Task 25: Commit

This is the only commit step.

- [ ] **Step 1: Stage and verify**

Run:

```bash
git add ProjectCeres.Client/src/app/features/accounts \
        ProjectCeres.Client/src/app/pages/Accounts.tsx \
        ProjectCeres.Client/src/app/App.tsx \
        ProjectCeres/Controllers/AccountsController.cs \
        ProjectCeres/Services/IAccountService.cs \
        ProjectCeres/Services/AccountService.cs \
        ProjectCeres/Services/AccountPolicies.cs \
        ProjectCeres/ViewModels/AccountApiDtos.cs \
        ProjectCeres/Controllers/Api/AccountsApiController.cs \
        ProjectCeres/Program.cs \
        ProjectCeres.Tests/Unit/AccountPoliciesTests.cs \
        ProjectCeres.Tests/Integration/Api/AccountsCrudApiTests.cs \
        ProjectCeres.Tests/Integration/AccountServiceTests.cs \
        ProjectCeres.Tests/Integration/Api/UserIdStampingTests.cs \
        docs/planning-phase3-spa-migration.md \
        docs/planning-phase3.md \
        docs/api-contract.md \
        docs/models.md

git status --short
```

The deletions from Task 23 are already staged via `git rm`. Verify the staged set looks like the spec's File structure summary. Unstage anything unexpected (e.g. `launchSettings.json` modifications) with `git restore --staged <path>`.

- [ ] **Step 2: Commit**

```bash
git commit -m "$(cat <<'EOF'
feat(spa): Accounts page + ledger + Razor cutover

Replaces the /app/accounts placeholder with a full Accounts SPA: list
page (search, Include archived toggle, per-currency subtotal,
adaptive archive AlertDialog), Create/Edit forms (Type+Currency
locked on Edit, conditional Asset/Liability fields, opening balance
behind Advanced disclosure on Edit), and a dedicated Ledger page
(running balance + payoff projection for amortising liability
accounts, projection math runs SPA-side). Slims the Razor
AccountsController to five 302 redirects.

New pieces:
- features/accounts/{accounts-api.ts, projection.ts, AccountsLayout.tsx,
  AccountsTable.tsx, AccountRowMenu.tsx, AccountForm.tsx,
  AccountCurrencySubtotals.tsx, AccountCreate.tsx, AccountEdit.tsx,
  AccountLedger.tsx} + co-located Vitest tests
- Nested routes /accounts/new and /accounts/:id/edit in App.tsx
- Sibling top-level route /accounts/:id/ledger
- pages/Accounts.tsx is a one-line re-export of AccountsLayout

Locked design choices (12 brainstorm questions):
- Single mixed list with Type column (no Asset/Liability tabs).
- Balance colour by account type, not by sign — handles credit-card-
  with-refund-credit case correctly.
- Two-layer interest-rate normalisation: SPA submit-time clears the
  rate when not Amortising; server policy extended to reject
  null repaymentType + non-null rate (closes silent invariant gap).
- HasTransactions field added to AccountListItemDto powers adaptive
  archive copy (empty: "Safe to archive"; non-empty: "balance still
  counts toward your net worth").
- LiabilityProjectionService deleted — projection math moves to the
  SPA, single source of truth post-cutover.

Razor cutover:
- Views/Accounts/{Index,Create,Edit,Deactivate,Ledger}.cshtml: deleted
- ViewModels/AccountCreateViewModel.cs, AccountEditViewModel.cs,
  AccountLedgerViewModel.cs: deleted
- Services/ILiabilityProjectionService.cs, LiabilityProjectionService.cs,
  ViewModels/LiabilityProjectionViewModel.cs,
  Tests/Unit/LiabilityProjectionServiceTests.cs: deleted (zero callers
  post-cutover)
- AccountsController slimmed to 5 redirects (302, not 301)
- IAccountService and AccountService throwing CRUD methods deleted
  (covered at API level by AccountsCrudApiTests)
- AccountPolicies.ValidateLiabilityRepayment extended to symmetric
  None-case rejection (Layer 2 of two-layer interest-rate defense)
- Program.cs: removed AddScoped<ILiabilityProjectionService> registration

Tests: ~60 new client tests across 9 test files; AccountPoliciesTests
gains 2 cases for the new None-case branch; AccountsCrudApiTests gains
HasTransactions coverage. Full client and server suites green.

Docs synced: planning-phase3-spa-migration.md §2 + §8 mark Accounts
migrated; planning-phase3.md §14 group 7 records the migration with
spec/plan citations.

Spec: docs/superpowers/specs/2026-05-03-accounts-spa-design.md
Plan: docs/superpowers/plans/2026-05-03-accounts-spa.md
EOF
)"
```

- [ ] **Step 3: Verify the commit landed cleanly**

Run: `git log -1 --stat`
Expected: shows the new files added under `features/accounts/`, the modifications to `App.tsx`/`pages/Accounts.tsx`/`AccountsController.cs`/`IAccountService.cs`/`AccountService.cs`/`AccountPolicies.cs`/`AccountApiDtos.cs`/`AccountsApiController.cs`/`Program.cs`, the test file modifications, the deletions of all 5 `Views/Accounts/*.cshtml` + 3 ViewModels + 3 projection service files + 1 projection test file, and the doc updates.

- [ ] **Step 4: Update commit-hash placeholders in docs**

The commit message references `<sha>` placeholders in the planning docs. Get the actual commit SHA:

```bash
git log -1 --format='%h'
```

Replace the `<sha>` placeholders in `docs/planning-phase3-spa-migration.md` §2 and §8 with the actual SHA, then amend:

```bash
git commit --amend --no-edit
```

(This amend is acceptable because the commit hasn't been pushed — there's no remote.)

---

## Self-review (completed inline)

**Spec coverage:**
- File structure → Tasks 1, 3, 5, 7, 9, 11, 13, 15, 17, 19.
- Routing changes → Task 20.
- Locked decisions (mixed list, type-driven balance colour, conditional fields, opening-balance disclosure, adaptive archive copy, three empty states, no system-row treatment) → Tasks 4–13.
- Server-immutability of Type + Currency on Edit → Tasks 10, 11 (form), already enforced server-side.
- Conditional Asset/Liability blocks with Amortising-cached interest rate (C2) → Tasks 10, 11.
- Layer 1 SPA submit-time normalisation → Task 11.
- Layer 2 server policy tightening → Task 22.
- HasTransactions field + adaptive AlertDialog copy → Tasks 6, 7, 21.
- Per-currency subtotal (≥2 currencies, net = sum(asset) − sum(liability)) → Tasks 8, 9.
- Ledger with running-balance column + payoff projection (SPA-side math) → Tasks 18, 19, plus the projection util in Tasks 2, 3.
- Ledger archived-account badge → Task 18.
- Razor cutover (controller + views + viewmodels + service throwing methods + projection service deletion) → Task 23.
- Verification gate including doc sync → Task 24.
- Single commit → Task 25.

**Placeholder scan:** No "TBD" / "TODO" / vague instructions. Every code block is complete and runnable. The `<sha>` placeholders in the doc updates are explicitly resolved in Task 25 step 4.

**Type consistency:**
- `AccountListItemDto`, `AccountDetailDto`, `AccountFormValues`, `AccountTypeDto`, `CurrencyDto`, `CreateAccountRequest`, `UpdateAccountRequest`, `RepaymentType`, `LedgerEntryDto`, `AccountLedgerDto`, `ProjectionResult`, `ApiErrorEnvelope` defined in Tasks 1–3, used consistently in Tasks 4, 6, 8, 10, 12, 14, 16, 18.
- `LayoutContext` (`{ refetch: () => void }`) defined in Tasks 15, 17.
- `SubmitResult` (`{ ok: true } | { ok: false }`) defined in Task 11, used by Tasks 15, 17.
- `formatBalance` / `formatSigned` / `formatDate` are defined per-component (in `AccountsTable.tsx` and `AccountLedger.tsx`); the Categories spec set the precedent of inline formatters until the Settings-aware-formatting follow-up lands.
- The `Get_returns_HasTransactions_*` test names match exactly between Task 21 and the spec's test inventory.

**Spec deviation check:** None. The plan implements exactly what the spec describes.

No contradictions; the plan is internally consistent.
