# SPA Page Pattern (Settings) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the placeholder `/app/settings` route with a real Settings page, add a public `refetchSettings()` to the existing settings cache, and slim the Razor `SettingsController` to a 302 redirect — all in one commit.

**Architecture:** A new `features/settings/` folder with a Page (data + plumbing) and a Form (pure UI), following the Budgets/Movements split. The SPA Page calls `useApi` for GET, hand-rolled `fetch` for PATCH, and notifies the existing `lib/use-settings.ts` cache via a new `refetchSettings()` export so other components pick up format/currency changes without a page reload.

**Tech Stack:** React 19 + TypeScript + Vite, shadcn/ui, Vitest + React Testing Library, ASP.NET Core (Razor controller cutover).

**Spec:** `docs/superpowers/specs/2026-05-02-spa-page-pattern-and-settings-design.md`

---

## File Structure (locked from spec)

**Create:**
- `ProjectCeres.Client/src/app/features/settings/settings-api.ts` — URL constants + DTO types
- `ProjectCeres.Client/src/app/features/settings/SettingsForm.tsx` — pure UI (form state, dirty detection, four fields)
- `ProjectCeres.Client/src/app/features/settings/SettingsForm.test.tsx`
- `ProjectCeres.Client/src/app/features/settings/SettingsPage.tsx` — data + plumbing
- `ProjectCeres.Client/src/app/features/settings/SettingsPage.test.tsx`

**Modify:**
- `ProjectCeres.Client/src/app/lib/use-settings.ts` — add public `refetchSettings()` export
- `ProjectCeres.Client/src/app/lib/use-settings.test.ts` — add tests for `refetchSettings`
- `ProjectCeres.Client/src/app/pages/Settings.tsx` — replace placeholder with one-line re-export
- `ProjectCeres/Controllers/SettingsController.cs` — slim to 302 redirect-only
- `ProjectCeres/Services/ISettingsService.cs` — remove `UpdateAsync(SettingsEditViewModel)` method declaration
- `ProjectCeres/Services/SettingsService.cs` — remove `UpdateAsync(SettingsEditViewModel)` impl
- `ProjectCeres.Tests/Integration/SettingsServiceTests.cs` — remove `UpdateAsync_*` test methods

**Delete:**
- `ProjectCeres/Views/Settings/Edit.cshtml`
- `ProjectCeres/ViewModels/SettingsEditViewModel.cs`

**Note for the next six pages:** The `features/<area>/` + `<Area>Page.tsx` + `<Area>Form.tsx` (or list/dialog component) split is the template. Replicate exactly. The `refetchSettings()` cache invalidation is **Settings-specific** — Categories/Accounts/etc. don't have a corresponding singleton cache and won't need that step.

---

## Task 1: settings-api.ts (URL builders + DTOs)

**Files:**
- Create: `ProjectCeres.Client/src/app/features/settings/settings-api.ts`

This file is logic-free. It mirrors the existing `budgets-api.ts` shape so the next six features have a clear template to copy.

- [ ] **Step 1: Write the file**

```typescript
// ---------- URL builders ----------

export const SETTINGS_URL = '/api/settings';
export const CURRENCIES_URL = '/api/currencies';

// ---------- DTOs ----------

export type NumberFormat = 'comma_decimal' | 'period_decimal';
export type DateFormat = 'DD/MM/YYYY' | 'MM/DD/YYYY' | 'YYYY-MM-DD';

export type SettingsDto = {
  numberFormat: NumberFormat;
  dateFormat: DateFormat;
  defaultCurrencyCode: string;
  defaultCurrencySymbol: string;
  periodStartDay: number;
};

/**
 * The currencies endpoint serves only id/code/symbol — there is no `name`
 * field on the wire (verified against `CurrenciesApiController` on
 * 2026-05-02). Don't be tempted to add one without server-side support.
 */
export type CurrencyOptionDto = {
  id: number;
  code: string;
  symbol: string;
};

export type UpdateSettingsRequest = {
  numberFormat: NumberFormat;
  dateFormat: DateFormat;
  defaultCurrencyId: number;
  periodStartDay: number;
};

// ---------- Form values (UI layer) ----------

/**
 * The form holds the values the user is editing. It tracks
 * `defaultCurrencyId` directly because the dropdown's value is the id;
 * the server's GET response uses `defaultCurrencyCode` for display.
 */
export type SettingsFormValues = {
  numberFormat: NumberFormat;
  dateFormat: DateFormat;
  defaultCurrencyId: number;
  periodStartDay: number;
};
```

- [ ] **Step 2: Verify typescript compiles**

Run: `cd ProjectCeres.Client && pnpm tsc --noEmit`
Expected: no errors.

---

## Task 2: SettingsForm — failing tests first

**Files:**
- Create: `ProjectCeres.Client/src/app/features/settings/SettingsForm.test.tsx`

The form is pure UI. `onSubmit` is mocked with `vi.fn()`. No fetch mocking. Seven tests.

- [ ] **Step 1: Write the test file**

```typescript
import { fireEvent, render, screen } from '@testing-library/react';
import { describe, expect, it, vi } from 'vitest';
import { SettingsForm } from './SettingsForm';
import type { CurrencyOptionDto, SettingsFormValues } from './settings-api';

const initialValues: SettingsFormValues = {
  numberFormat: 'comma_decimal',
  dateFormat: 'DD/MM/YYYY',
  defaultCurrencyId: 1,
  periodStartDay: 1,
};

const currencies: CurrencyOptionDto[] = [
  { id: 1, code: 'EUR', symbol: '€' },
  { id: 2, code: 'USD', symbol: '$' },
];

function renderForm(overrides?: { onSubmit?: ReturnType<typeof vi.fn> }) {
  const onSubmit = overrides?.onSubmit ?? vi.fn().mockResolvedValue({ ok: true });
  const onCancel = vi.fn();
  const utils = render(
    <SettingsForm
      initialValues={initialValues}
      currencies={currencies}
      onSubmit={onSubmit}
      onCancel={onCancel}
    />,
  );
  return { ...utils, onSubmit, onCancel };
}

describe('SettingsForm', () => {
  it('renders all four fields with initial values', () => {
    renderForm();
    expect(screen.getByLabelText(/number format/i)).toBeInTheDocument();
    expect(screen.getByLabelText(/date format/i)).toBeInTheDocument();
    expect(screen.getByLabelText(/default currency/i)).toBeInTheDocument();
    expect(screen.getByLabelText(/period start day/i)).toHaveValue(1);
  });

  it('Save button is disabled when nothing has changed', () => {
    renderForm();
    expect(screen.getByRole('button', { name: /save/i })).toBeDisabled();
  });

  it('Save button enables when a field changes', () => {
    renderForm();
    fireEvent.change(screen.getByLabelText(/period start day/i), {
      target: { value: '15' },
    });
    expect(screen.getByRole('button', { name: /save/i })).toBeEnabled();
  });

  it('Save button shows "Saving…" while onSubmit is pending', async () => {
    let resolveSubmit: (v: { ok: true }) => void = () => {};
    const onSubmit = vi.fn(
      () => new Promise<{ ok: true }>((resolve) => { resolveSubmit = resolve; }),
    );
    renderForm({ onSubmit });
    fireEvent.change(screen.getByLabelText(/period start day/i), {
      target: { value: '15' },
    });
    fireEvent.click(screen.getByRole('button', { name: /save/i }));
    expect(screen.getByRole('button', { name: /saving/i })).toBeDisabled();
    resolveSubmit({ ok: true });
  });

  it('Save button greys back out after onSubmit returns ok:true', async () => {
    const onSubmit = vi.fn().mockResolvedValue({ ok: true });
    renderForm({ onSubmit });
    fireEvent.change(screen.getByLabelText(/period start day/i), {
      target: { value: '15' },
    });
    fireEvent.click(screen.getByRole('button', { name: /save/i }));
    // Wait for the submit promise to resolve.
    await screen.findByRole('button', { name: /save/i });
    expect(screen.getByRole('button', { name: /save/i })).toBeDisabled();
  });

  it('inputs retain user edits when onSubmit returns ok:false', async () => {
    const onSubmit = vi.fn().mockResolvedValue({ ok: false });
    renderForm({ onSubmit });
    fireEvent.change(screen.getByLabelText(/period start day/i), {
      target: { value: '20' },
    });
    fireEvent.click(screen.getByRole('button', { name: /save/i }));
    await screen.findByRole('button', { name: /save/i });
    expect(screen.getByLabelText(/period start day/i)).toHaveValue(20);
    expect(screen.getByRole('button', { name: /save/i })).toBeEnabled();
  });

  it('Period-start-day clamps values outside 1-31 to the nearest valid value', () => {
    renderForm();
    const input = screen.getByLabelText(/period start day/i);
    fireEvent.change(input, { target: { value: '0' } });
    expect(input).toHaveValue(1);
    fireEvent.change(input, { target: { value: '32' } });
    expect(input).toHaveValue(31);
  });
});
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `cd ProjectCeres.Client && pnpm test SettingsForm`
Expected: FAIL — `SettingsForm` not exported / module not found.

---

## Task 3: SettingsForm — implementation

**Files:**
- Create: `ProjectCeres.Client/src/app/features/settings/SettingsForm.tsx`

Pure UI. Owns local form state, computes `isDirty` by shallow comparison, renders four fields. Returns a `Promise<{ok:true}|{ok:false}>` from `onSubmit`. Knows nothing about fetch or toast.

- [ ] **Step 1: Write the implementation**

```tsx
import { useState } from 'react';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/input';
import { Label } from '@/components/ui/label';
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from '@/components/ui/select';
import type {
  CurrencyOptionDto,
  DateFormat,
  NumberFormat,
  SettingsFormValues,
} from './settings-api';

type SubmitResult = { ok: true } | { ok: false };

type Props = {
  initialValues: SettingsFormValues;
  currencies: CurrencyOptionDto[];
  onSubmit: (values: SettingsFormValues) => Promise<SubmitResult>;
  onCancel: () => void;
};

const NUMBER_FORMATS: { value: NumberFormat; label: string }[] = [
  { value: 'comma_decimal',  label: '1.234,56  (comma decimal — EU)' },
  { value: 'period_decimal', label: '1,234.56  (period decimal — US)' },
];

const DATE_FORMATS: { value: DateFormat; label: string }[] = [
  { value: 'DD/MM/YYYY', label: 'DD/MM/YYYY' },
  { value: 'MM/DD/YYYY', label: 'MM/DD/YYYY' },
  { value: 'YYYY-MM-DD', label: 'YYYY-MM-DD' },
];

function shallowEqual(a: SettingsFormValues, b: SettingsFormValues): boolean {
  return (
    a.numberFormat       === b.numberFormat       &&
    a.dateFormat         === b.dateFormat         &&
    a.defaultCurrencyId  === b.defaultCurrencyId  &&
    a.periodStartDay     === b.periodStartDay
  );
}

function clampStartDay(raw: string): number {
  const n = Number.parseInt(raw, 10);
  if (Number.isNaN(n)) return 1;
  if (n < 1) return 1;
  if (n > 31) return 31;
  return n;
}

export function SettingsForm({ initialValues, currencies, onSubmit, onCancel }: Props) {
  const [snapshot, setSnapshot] = useState<SettingsFormValues>(initialValues);
  const [values, setValues] = useState<SettingsFormValues>(initialValues);
  const [submitting, setSubmitting] = useState(false);

  const isDirty = !shallowEqual(values, snapshot);

  async function handleSubmit(e: React.FormEvent<HTMLFormElement>) {
    e.preventDefault();
    if (!isDirty || submitting) return;
    setSubmitting(true);
    const result = await onSubmit(values);
    setSubmitting(false);
    if (result.ok) {
      // Re-baseline so isDirty becomes false and Save greys back out.
      setSnapshot(values);
    }
    // On ok:false the snapshot stays old, isDirty stays true, user can retry.
  }

  return (
    <form onSubmit={handleSubmit} className="space-y-6 max-w-xl">
      <div className="space-y-2">
        <Label htmlFor="numberFormat">Number format</Label>
        <Select
          value={values.numberFormat}
          onValueChange={(v) =>
            setValues((cur) => ({ ...cur, numberFormat: v as NumberFormat }))
          }
        >
          <SelectTrigger id="numberFormat" aria-label="Number format">
            <SelectValue />
          </SelectTrigger>
          <SelectContent>
            {NUMBER_FORMATS.map((f) => (
              <SelectItem key={f.value} value={f.value}>{f.label}</SelectItem>
            ))}
          </SelectContent>
        </Select>
      </div>

      <div className="space-y-2">
        <Label htmlFor="dateFormat">Date format</Label>
        <Select
          value={values.dateFormat}
          onValueChange={(v) =>
            setValues((cur) => ({ ...cur, dateFormat: v as DateFormat }))
          }
        >
          <SelectTrigger id="dateFormat" aria-label="Date format">
            <SelectValue />
          </SelectTrigger>
          <SelectContent>
            {DATE_FORMATS.map((f) => (
              <SelectItem key={f.value} value={f.value}>{f.label}</SelectItem>
            ))}
          </SelectContent>
        </Select>
      </div>

      <div className="space-y-2">
        <Label htmlFor="defaultCurrencyId">Default currency</Label>
        <Select
          value={String(values.defaultCurrencyId)}
          onValueChange={(v) =>
            setValues((cur) => ({ ...cur, defaultCurrencyId: Number(v) }))
          }
        >
          <SelectTrigger id="defaultCurrencyId" aria-label="Default currency">
            <SelectValue />
          </SelectTrigger>
          <SelectContent>
            {currencies.map((c) => (
              <SelectItem key={c.id} value={String(c.id)}>
                {c.code} ({c.symbol})
              </SelectItem>
            ))}
          </SelectContent>
        </Select>
      </div>

      <div className="space-y-2">
        <Label htmlFor="periodStartDay">Period start day</Label>
        <Input
          id="periodStartDay"
          aria-label="Period start day"
          type="number"
          min={1}
          max={31}
          value={values.periodStartDay}
          onChange={(e) =>
            setValues((cur) => ({ ...cur, periodStartDay: clampStartDay(e.target.value) }))
          }
        />
        <p className="text-sm text-muted-foreground">
          Day of month (1–31) when monthly cycles start.
        </p>
      </div>

      <div className="flex gap-2">
        <Button type="submit" disabled={!isDirty || submitting}>
          {submitting ? 'Saving…' : 'Save'}
        </Button>
        <Button type="button" variant="ghost" onClick={onCancel}>
          Cancel
        </Button>
      </div>
    </form>
  );
}
```

- [ ] **Step 2: Run tests to verify they pass**

Run: `cd ProjectCeres.Client && pnpm test SettingsForm`
Expected: PASS — all 7 tests green.

---

## Task 4: refetchSettings() — failing tests first

**Files:**
- Modify: `ProjectCeres.Client/src/app/lib/use-settings.test.ts` — append two test cases at the end of the existing `describe('useSettings')` block (or in a new `describe('refetchSettings')` block — see code below).

- [ ] **Step 1: Append the new tests**

Open `ProjectCeres.Client/src/app/lib/use-settings.test.ts` and add this `describe` block after the existing `describe('useSettings', ...)` block:

```typescript
import { __resetSettingsForTests, refetchSettings, useSettings } from './use-settings';

// ... existing useSettings tests stay unchanged ...

describe('refetchSettings', () => {
  it('triggers a new fetch and notifies subscribers', async () => {
    mockFetch.mockResolvedValueOnce({
      ok: true,
      json: async () => ({
        numberFormat: 'comma_decimal',
        dateFormat: 'DD/MM/YYYY',
        defaultCurrencyCode: 'EUR',
        defaultCurrencySymbol: '€',
        periodStartDay: 1,
      }),
    });

    const { result } = renderHook(() => useSettings());
    await waitFor(() => expect(result.current.loading).toBe(false));
    expect(mockFetch).toHaveBeenCalledTimes(1);

    mockFetch.mockResolvedValueOnce({
      ok: true,
      json: async () => ({
        numberFormat: 'period_decimal',
        dateFormat: 'YYYY-MM-DD',
        defaultCurrencyCode: 'USD',
        defaultCurrencySymbol: '$',
        periodStartDay: 15,
      }),
    });

    await act(async () => {
      await refetchSettings();
    });

    expect(mockFetch).toHaveBeenCalledTimes(2);
  });

  it('updates cache.data so existing subscribers see the new response', async () => {
    mockFetch.mockResolvedValueOnce({
      ok: true,
      json: async () => ({
        numberFormat: 'comma_decimal',
        dateFormat: 'DD/MM/YYYY',
        defaultCurrencyCode: 'EUR',
        defaultCurrencySymbol: '€',
        periodStartDay: 1,
      }),
    });

    const { result } = renderHook(() => useSettings());
    await waitFor(() => expect(result.current.data?.defaultCurrencyCode).toBe('EUR'));

    mockFetch.mockResolvedValueOnce({
      ok: true,
      json: async () => ({
        numberFormat: 'period_decimal',
        dateFormat: 'YYYY-MM-DD',
        defaultCurrencyCode: 'USD',
        defaultCurrencySymbol: '$',
        periodStartDay: 15,
      }),
    });

    await act(async () => {
      await refetchSettings();
    });

    await waitFor(() =>
      expect(result.current.data?.defaultCurrencyCode).toBe('USD'),
    );
    expect(result.current.data?.numberFormat).toBe('period_decimal');
    expect(result.current.data?.periodStartDay).toBe(15);
  });
});
```

If the existing top-of-file imports already contain `useSettings` and `__resetSettingsForTests` from `./use-settings`, change that import line to also include `refetchSettings`:

```typescript
import { __resetSettingsForTests, refetchSettings, useSettings } from './use-settings';
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `cd ProjectCeres.Client && pnpm test use-settings`
Expected: FAIL — `refetchSettings` not exported from `./use-settings`.

---

## Task 5: refetchSettings() — implementation

**Files:**
- Modify: `ProjectCeres.Client/src/app/lib/use-settings.ts` — add the new export at the bottom, just before `__resetSettingsForTests`.

- [ ] **Step 1: Append the new export**

Open `ProjectCeres.Client/src/app/lib/use-settings.ts` and add this function above the existing `__resetSettingsForTests` declaration:

```typescript
/**
 * Force a refresh of the cached settings. Notifies every useSettings()
 * subscriber when the new data arrives. Call this after PATCH /api/settings
 * succeeds so the rest of the app picks up format/currency changes
 * without a page reload.
 *
 * Does NOT clear cache.data first — old data stays visible for the ~50–100 ms
 * the GET takes, avoiding a flash of empty state in every other component.
 */
export function refetchSettings(): Promise<void> {
  cache.promise = null;     // clear the dedup so startFetch actually runs
  cache.loading = false;
  return startFetch();
}
```

- [ ] **Step 2: Run tests to verify they pass**

Run: `cd ProjectCeres.Client && pnpm test use-settings`
Expected: PASS — including the original `useSettings` tests AND the two new `refetchSettings` tests.

---

## Task 6: SettingsPage — failing tests first

**Files:**
- Create: `ProjectCeres.Client/src/app/features/settings/SettingsPage.test.tsx`

This is where `global.fetch` is mocked. Six tests covering loading / data / error / retry / PATCH success / PATCH failure.

- [ ] **Step 1: Write the test file**

```typescript
import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { toast } from 'sonner';
import { SettingsPage } from './SettingsPage';
import { refetchSettings } from '../../lib/use-settings';

vi.mock('sonner', () => ({
  toast: { success: vi.fn(), error: vi.fn() },
}));

vi.mock('../../lib/use-settings', () => ({
  refetchSettings: vi.fn().mockResolvedValue(undefined),
}));

const mockFetch = vi.fn();

const settingsResponse = {
  numberFormat: 'comma_decimal',
  dateFormat: 'DD/MM/YYYY',
  defaultCurrencyCode: 'EUR',
  defaultCurrencySymbol: '€',
  periodStartDay: 1,
};

const currenciesResponse = [
  { id: 1, code: 'EUR', symbol: '€' },
  { id: 2, code: 'USD', symbol: '$' },
];

beforeEach(() => {
  global.fetch = mockFetch as unknown as typeof fetch;
  mockFetch.mockImplementation((url: string) => {
    if (url === '/api/settings') {
      return Promise.resolve({ ok: true, status: 200, json: async () => settingsResponse });
    }
    if (url === '/api/currencies') {
      return Promise.resolve({ ok: true, status: 200, json: async () => currenciesResponse });
    }
    return Promise.resolve({ ok: false, status: 404, json: async () => null });
  });
});

afterEach(() => vi.resetAllMocks());

describe('SettingsPage', () => {
  it('renders skeleton while loading', () => {
    // Make both fetches hang forever for this test.
    mockFetch.mockImplementation(() => new Promise(() => {}));
    render(<SettingsPage />);
    expect(screen.getByTestId('settings-skeleton')).toBeInTheDocument();
  });

  it('renders the form when data arrives', async () => {
    render(<SettingsPage />);
    await waitFor(() =>
      expect(screen.getByLabelText(/period start day/i)).toHaveValue(1),
    );
  });

  it('renders error block + Retry when GET fails', async () => {
    mockFetch.mockImplementation((url: string) => {
      if (url === '/api/settings') {
        return Promise.resolve({ ok: false, status: 500, json: async () => null });
      }
      return Promise.resolve({ ok: true, status: 200, json: async () => currenciesResponse });
    });
    render(<SettingsPage />);
    await waitFor(() =>
      expect(screen.getByText(/couldn't load settings/i)).toBeInTheDocument(),
    );
    expect(screen.getByRole('button', { name: /retry/i })).toBeInTheDocument();
  });

  it('Retry re-fetches after error and renders the form', async () => {
    let attempt = 0;
    mockFetch.mockImplementation((url: string) => {
      if (url === '/api/settings') {
        attempt++;
        if (attempt === 1) {
          return Promise.resolve({ ok: false, status: 500, json: async () => null });
        }
        return Promise.resolve({ ok: true, status: 200, json: async () => settingsResponse });
      }
      return Promise.resolve({ ok: true, status: 200, json: async () => currenciesResponse });
    });
    render(<SettingsPage />);
    await waitFor(() =>
      expect(screen.getByText(/couldn't load settings/i)).toBeInTheDocument(),
    );
    fireEvent.click(screen.getByRole('button', { name: /retry/i }));
    await waitFor(() =>
      expect(screen.getByLabelText(/period start day/i)).toBeInTheDocument(),
    );
  });

  it('PATCH success shows toast.success and calls refetchSettings', async () => {
    mockFetch.mockImplementation((url: string, init?: RequestInit) => {
      if (url === '/api/settings' && init?.method === 'PATCH') {
        return Promise.resolve({ ok: true, status: 204, json: async () => null });
      }
      if (url === '/api/settings') {
        return Promise.resolve({ ok: true, status: 200, json: async () => settingsResponse });
      }
      if (url === '/api/currencies') {
        return Promise.resolve({ ok: true, status: 200, json: async () => currenciesResponse });
      }
      return Promise.resolve({ ok: false, status: 404, json: async () => null });
    });
    render(<SettingsPage />);
    await screen.findByLabelText(/period start day/i);

    fireEvent.change(screen.getByLabelText(/period start day/i), {
      target: { value: '15' },
    });
    fireEvent.click(screen.getByRole('button', { name: /save/i }));

    await waitFor(() => expect(toast.success).toHaveBeenCalledWith('Saved.'));
    expect(refetchSettings).toHaveBeenCalledTimes(1);
  });

  it('PATCH failure shows toast.error and Save stays enabled', async () => {
    mockFetch.mockImplementation((url: string, init?: RequestInit) => {
      if (url === '/api/settings' && init?.method === 'PATCH') {
        return Promise.resolve({ ok: false, status: 422, json: async () => null });
      }
      if (url === '/api/settings') {
        return Promise.resolve({ ok: true, status: 200, json: async () => settingsResponse });
      }
      if (url === '/api/currencies') {
        return Promise.resolve({ ok: true, status: 200, json: async () => currenciesResponse });
      }
      return Promise.resolve({ ok: false, status: 404, json: async () => null });
    });
    render(<SettingsPage />);
    await screen.findByLabelText(/period start day/i);

    fireEvent.change(screen.getByLabelText(/period start day/i), {
      target: { value: '15' },
    });
    fireEvent.click(screen.getByRole('button', { name: /save/i }));

    await waitFor(() =>
      expect(toast.error).toHaveBeenCalledWith("Couldn't save. Try again."),
    );
    expect(refetchSettings).not.toHaveBeenCalled();
    expect(screen.getByRole('button', { name: /save/i })).toBeEnabled();
  });
});
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `cd ProjectCeres.Client && pnpm test SettingsPage`
Expected: FAIL — `SettingsPage` not exported / module not found.

---

## Task 7: SettingsPage — implementation

**Files:**
- Create: `ProjectCeres.Client/src/app/features/settings/SettingsPage.tsx`

Page glue: data fetching, PATCH plumbing, toast, cache refetch.

- [ ] **Step 1: Write the implementation**

```tsx
import { toast } from 'sonner';
import { Button } from '@/components/ui/button';
import { Skeleton } from '@/components/ui/skeleton';
import { useApi } from '../../lib/use-api';
import { refetchSettings } from '../../lib/use-settings';
import { SettingsForm } from './SettingsForm';
import {
  CURRENCIES_URL,
  SETTINGS_URL,
  type CurrencyOptionDto,
  type SettingsDto,
  type SettingsFormValues,
  type UpdateSettingsRequest,
} from './settings-api';

export function SettingsPage() {
  const settings = useApi<SettingsDto>(SETTINGS_URL);
  const currencies = useApi<CurrencyOptionDto[]>(CURRENCIES_URL);

  if (settings.loading || currencies.loading) {
    return (
      <div className="p-6">
        <h1 className="text-2xl font-semibold mb-6">Settings</h1>
        <div data-testid="settings-skeleton" className="space-y-4 max-w-xl">
          <Skeleton className="h-10" />
          <Skeleton className="h-10" />
          <Skeleton className="h-10" />
          <Skeleton className="h-10" />
        </div>
      </div>
    );
  }

  if (settings.error || currencies.error || !settings.data || !currencies.data) {
    return (
      <div className="p-6">
        <h1 className="text-2xl font-semibold mb-6">Settings</h1>
        <div className="rounded border border-destructive/40 bg-destructive/10 p-4 max-w-xl">
          <p className="text-sm">Couldn't load settings.</p>
          <Button
            type="button"
            variant="outline"
            className="mt-2"
            onClick={() => {
              settings.refetch();
              currencies.refetch();
            }}
          >
            Retry
          </Button>
        </div>
      </div>
    );
  }

  // Convert the SettingsDto (which carries the currency code/symbol for
  // display) into form values (which carry the currency *id* for editing).
  const initialValues: SettingsFormValues = {
    numberFormat:      settings.data.numberFormat,
    dateFormat:        settings.data.dateFormat,
    defaultCurrencyId: currencies.data.find(
                         (c) => c.code === settings.data!.defaultCurrencyCode,
                       )?.id ?? currencies.data[0].id,
    periodStartDay:    settings.data.periodStartDay,
  };

  async function handleSubmit(values: SettingsFormValues) {
    const body: UpdateSettingsRequest = {
      numberFormat:      values.numberFormat,
      dateFormat:        values.dateFormat,
      defaultCurrencyId: values.defaultCurrencyId,
      periodStartDay:    values.periodStartDay,
    };

    try {
      const response = await fetch(SETTINGS_URL, {
        method: 'PATCH',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify(body),
      });

      if (!response.ok) {
        toast.error("Couldn't save. Try again.");
        return { ok: false as const };
      }

      // Notify every other component using useSettings(). Fire-and-forget;
      // we don't block the success toast on the cache settling.
      void refetchSettings();
      toast.success('Saved.');
      return { ok: true as const };
    } catch {
      toast.error("Couldn't save. Try again.");
      return { ok: false as const };
    }
  }

  return (
    <div className="p-6">
      <h1 className="text-2xl font-semibold mb-6">Settings</h1>
      <SettingsForm
        initialValues={initialValues}
        currencies={currencies.data}
        onSubmit={handleSubmit}
        onCancel={() => {
          // No external page to navigate to — this is the only Settings
          // surface. Cancel is a no-op for now; the form's snapshot reset
          // pattern means there's nothing to "discard" beyond what the
          // user has typed since last save.
        }}
      />
    </div>
  );
}
```

- [ ] **Step 2: Run tests to verify they pass**

Run: `cd ProjectCeres.Client && pnpm test SettingsPage`
Expected: PASS — all 6 tests green.

---

## Task 8: Wire SettingsPage to the route

**Files:**
- Modify: `ProjectCeres.Client/src/app/pages/Settings.tsx`

Replace the placeholder with a one-line re-export.

- [ ] **Step 1: Replace the file contents**

Open `ProjectCeres.Client/src/app/pages/Settings.tsx` and replace its entire contents with:

```typescript
export { SettingsPage as Settings } from '../features/settings/SettingsPage';
```

- [ ] **Step 2: Verify the SPA still type-checks**

Run: `cd ProjectCeres.Client && pnpm tsc --noEmit`
Expected: no errors.

- [ ] **Step 3: Run the full client test suite**

Run: `cd ProjectCeres.Client && pnpm test`
Expected: all tests PASS, including SettingsForm (7), SettingsPage (6), use-settings (existing 3 + 2 new = 5).

---

## Task 9: Razor cutover — slim SettingsController to a redirect

**Files:**
- Modify: `ProjectCeres/Controllers/SettingsController.cs`

The current controller has GET `Edit()` (loads settings, renders view), POST `Edit(SettingsEditViewModel vm)` (saves settings), and a `PopulateViewBagAsync` helper. Replace with a single redirect-only `Edit()`.

- [ ] **Step 1: Replace the file contents**

Open `ProjectCeres/Controllers/SettingsController.cs` and replace its entire contents with:

```csharp
using Microsoft.AspNetCore.Mvc;

namespace ProjectCeres.Controllers;

/// <summary>
/// Razor settings UI is replaced by the SPA at /app/settings. This controller
/// exists only to 302-redirect any in-flight bookmarks. Use 302 (not 301) so
/// browsers don't aggressively cache during the SPA migration window.
/// The redirect is removed entirely in the final SPA-cutover cleanup batch.
/// </summary>
public class SettingsController : Controller
{
    public IActionResult Edit() => Redirect("/app/settings");
}
```

- [ ] **Step 2: Verify the project still builds**

Run: `dotnet build ProjectCeres/ProjectCeres.csproj --nologo -v quiet`
Expected: build FAILS — `SettingsService.UpdateAsync` and `SettingsEditViewModel` are still referenced from the (now-deleted) POST action's call sites elsewhere. **Wait** — actually the controller was the only caller. We'll see in the next task.

(If the build *succeeds*, that's also acceptable; the dangling references are removed in Tasks 10–11.)

---

## Task 10: Razor cutover — delete the view + ViewModel

**Files:**
- Delete: `ProjectCeres/Views/Settings/Edit.cshtml`
- Delete: `ProjectCeres/ViewModels/SettingsEditViewModel.cs`

- [ ] **Step 1: Delete both files**

Run:
```bash
git rm ProjectCeres/Views/Settings/Edit.cshtml
git rm ProjectCeres/ViewModels/SettingsEditViewModel.cs
```

- [ ] **Step 2: Verify the build now fails on dangling service references**

Run: `dotnet build ProjectCeres/ProjectCeres.csproj --nologo -v quiet`
Expected: FAIL — `SettingsService.UpdateAsync(SettingsEditViewModel)` references the deleted type, and `ISettingsService.UpdateAsync` does too. Build errors in `SettingsService.cs` and `ISettingsService.cs`.

This is the expected handoff to Task 11.

---

## Task 11: Razor cutover — remove UpdateAsync from service

**Files:**
- Modify: `ProjectCeres/Services/ISettingsService.cs` — remove `UpdateAsync(SettingsEditViewModel vm)` declaration
- Modify: `ProjectCeres/Services/SettingsService.cs` — remove `UpdateAsync(SettingsEditViewModel vm)` implementation. Keep `GetAsync`, `EnsureExistsAsync`, and `TryUpdateAsync(UpdateSettingsRequest request)`.

- [ ] **Step 1: Remove the interface method**

Open `ProjectCeres/Services/ISettingsService.cs` and delete the line:

```csharp
Task UpdateAsync(SettingsEditViewModel vm);
```

…along with any leading XML doc comment for it.

- [ ] **Step 2: Remove the implementation**

Open `ProjectCeres/Services/SettingsService.cs` and delete the entire `UpdateAsync(SettingsEditViewModel vm)` method body.

- [ ] **Step 3: Verify the project builds**

Run: `dotnet build ProjectCeres/ProjectCeres.csproj --nologo -v quiet`
Expected: PASS — no errors.

---

## Task 12: Delete the Razor-only test methods

**Files:**
- Modify: `ProjectCeres.Tests/Integration/SettingsServiceTests.cs` — remove the two `UpdateAsync_*` test methods. Keep all `GetAsync_*` and `EnsureExistsAsync_*` test methods.

- [ ] **Step 1: Delete the two test methods**

Open `ProjectCeres.Tests/Integration/SettingsServiceTests.cs` and delete:

- The entire `[Fact] public async Task UpdateAsync_ChangesAllFields()` method.
- The entire `[Fact] public async Task UpdateAsync_CreatesRow_WhenNoneExist()` method.
- The `// UpdateAsync` section comment that introduces them, if present.

If the test file imports `SettingsEditViewModel`, remove that `using` line too:

```csharp
using ProjectCeres.ViewModels;   // <-- delete IF the only thing it imported was SettingsEditViewModel
```

(Verify by skimming: if `SettingsEditViewModel` is the only `ViewModels` type referenced in the file, the `using` is now dead.)

- [ ] **Step 2: Build the test project**

Run: `dotnet build ProjectCeres.Tests/ProjectCeres.Tests.csproj --nologo -v quiet`
Expected: PASS — no errors.

- [ ] **Step 3: Run the full server test suite**

Run: `dotnet test --nologo`
Expected: PASS — every test in the suite passes. The `SettingsApiTests.Patch_*` tests cover the same behaviour the deleted `UpdateAsync_*` tests used to cover.

---

## Task 13: Verification gate — full build + test

This is the gate before commit. Both stacks must be green and a manual click-through must work.

- [ ] **Step 1: Full server test suite**

Run: `dotnet test --nologo`
Expected: PASS — every test green.

- [ ] **Step 2: Full client test suite**

Run: `cd ProjectCeres.Client && pnpm test`
Expected: PASS — every test green, including the 13 new tests (7 SettingsForm + 6 SettingsPage) plus the 2 new use-settings tests.

- [ ] **Step 3: Client production build**

Run: `cd ProjectCeres.Client && pnpm build`
Expected: PASS — typecheck + Vite build complete with no errors.

- [ ] **Step 4: Manual click-through**

Start the app:
```bash
dotnet run --project ProjectCeres
```

In a browser, in this order:

1. Visit `https://localhost:7001/app/settings` — confirm the SPA page renders the form populated with current settings.
2. Change a field (e.g. flip Number Format from comma to period decimal). Click Save. Confirm the toast says "Saved." and the Save button greys out.
3. Reload the page. Confirm the new value is persisted.
4. Visit `https://localhost:7001/Settings/Edit` — confirm a 302 redirect lands you at `/app/settings`.
5. Change another setting that affects the dashboard (e.g. Default Currency). Save. Navigate to the dashboard (`/app/`) and confirm the currency code/symbol updated immediately on cards that read `useSettings()` (e.g. MTD card). The numbers may be stale until a separate refresh — that is expected.

If any step fails, stop and fix before committing.

---

## Task 14: Commit

- [ ] **Step 1: Stage and commit**

Run:
```bash
git add ProjectCeres.Client/src/app/features/settings \
        ProjectCeres.Client/src/app/lib/use-settings.ts \
        ProjectCeres.Client/src/app/lib/use-settings.test.ts \
        ProjectCeres.Client/src/app/pages/Settings.tsx \
        ProjectCeres/Controllers/SettingsController.cs \
        ProjectCeres/Services/ISettingsService.cs \
        ProjectCeres/Services/SettingsService.cs \
        ProjectCeres.Tests/Integration/SettingsServiceTests.cs

# git rm already staged the deleted files in Task 10; verify:
git status --short

git commit -m "$(cat <<'EOF'
feat(spa): Settings page + Razor cutover

Replaces the /app/settings placeholder with a real form and slims the
Razor SettingsController to a 302 redirect. Same commit deletes the
Razor view + edit ViewModel + UpdateAsync(SettingsEditViewModel) on
the service.

New pieces:
- features/settings/{settings-api.ts, SettingsForm.tsx, SettingsPage.tsx}
  + co-located Vitest tests
- lib/use-settings.ts gains a public refetchSettings() so PATCH success
  invalidates the cache without a page reload; every useSettings()
  consumer re-renders with new currency code/symbol immediately.

Razor cutover:
- Views/Settings/Edit.cshtml: deleted
- ViewModels/SettingsEditViewModel.cs: deleted
- SettingsController slimmed to: Edit() => Redirect("/app/settings")
  (302, not 301 — per migration doc §8)
- ISettingsService.UpdateAsync(SettingsEditViewModel) and impl: deleted
- SettingsServiceTests.UpdateAsync_*: deleted (covered at API level by
  SettingsApiTests.Patch_*)

Tests: 7 SettingsForm + 6 SettingsPage + 2 new use-settings = 15 new
client tests. Full client and server suites green.

This is the first SPA page in Phase 3 Batch 2 and locks the template
for the next six pages: features/<area>/ + Page+Form split, useApi for
GET, fetch for mutations, sonner for toasts. The refetchSettings()
cache invalidation is Settings-specific — Categories/Accounts/etc.
have no equivalent singleton cache.

Spec: docs/superpowers/specs/2026-05-02-spa-page-pattern-and-settings-design.md
Plan: docs/superpowers/plans/2026-05-02-spa-page-pattern-and-settings.md
EOF
)"
```

- [ ] **Step 2: Verify the commit landed cleanly**

Run: `git log -1 --stat`
Expected: shows the new files added under `features/settings/`, the modifications to `use-settings.ts`/`pages/Settings.tsx`/`SettingsController.cs`/`ISettingsService.cs`/`SettingsService.cs`/`SettingsServiceTests.cs`, and the deletions of `Edit.cshtml` + `SettingsEditViewModel.cs`.

---

## Self-review (already completed inline)

**Spec coverage:** Every section of the spec maps to a task in this plan:

- File structure → Tasks 1, 3, 7, 8.
- Data flow (mount, input, submit) → Tasks 3, 7.
- Public `refetchSettings()` → Tasks 4, 5.
- Loading / error / success states → Tasks 3, 7 (rendering); Tasks 2, 6 (test coverage).
- Testing strategy → Tasks 2, 4, 6 (writing); Task 13 step 2 (running).
- Razor cutover → Tasks 9, 10, 11, 12.
- Verification gate → Task 13.
- Single commit → Task 14.

**Placeholder scan:** No "TBD", "TODO", or vague guidance. Every code block is complete and runnable. Every command has its expected output.

**Type consistency:**
- `SettingsFormValues` defined in Task 1 used in Tasks 2, 3, 7. Field names (`numberFormat`, `dateFormat`, `defaultCurrencyId`, `periodStartDay`) are identical across all uses.
- `CurrencyOptionDto` shape (`{ id, code, symbol }`, **no `name`**) verified against the `CurrenciesApiController` source on 2026-05-02 and used consistently in Tasks 2, 6, 7.
- `SubmitResult` (`{ ok: true } | { ok: false }`) defined in Task 3, returned by Task 7's `handleSubmit`, asserted in Task 2's tests.
- `refetchSettings()` signature (`(): Promise<void>`) defined in Task 5, called in Task 7, mocked/asserted in Tasks 4, 6.

No contradictions; the plan is internally consistent.
