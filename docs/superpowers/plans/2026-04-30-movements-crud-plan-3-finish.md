# Movements CRUD — Plan 3: Attachments, bulk, export, Razor demolition

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Close out the Movements CRUD slice. Wire the attachment client-side: a drop-zone component, the Create→Edit pending-file hand-off, and per-attachment delete. Add the bulk-cleared and CSV export buttons to the Movements page header. Demolish the Razor `TransactionsController` and `TransfersController` page actions: each Index/Create/Edit/Delete returns a 302 to the SPA equivalent; the Razor view files are deleted. Sync the affected docs.

**Architecture:** The attachment surface is a **two-phase, save-first** flow as locked in the spec brainstorm. On Create, the dropzone is disabled until first save (with a tooltip explaining why); on save, the page redirects to Edit and any pending file the user picked is handed off via `useLocation().state` and uploaded immediately on Edit's first render. On Edit (cold load or post-redirect), the dropzone is active — drop a file → POST → optimistic add to the list with progress → confirm/rollback. Existing attachments list with an `×` delete button, confirmed via `AlertDialog`. Bulk-cleared and Export are simple `<Button>` controls in the page header that call the existing API endpoints with the active filter set, scoped from `useSearchParams()`. Razor demolition is the simplest part: per-action `Redirect("/app/...")` (302) + delete the corresponding `.cshtml` files in one commit.

**Tech Stack:** React 19, React Router v7, Vitest + React Testing Library on the client; ASP.NET Core MVC + Razor on the server (just to demolish).

**Spec:** `docs/superpowers/specs/2026-04-30-movements-crud.md`. Read §6.1 (page header bulk + export), §6.2 (attachments dropzone disabled on Create), §6.3 (attachments active on Edit), §7.1 (happy path attachment hand-off), §8 (Razor demolition map), §10 (docs to sync), §12 (the hand-off fragility note).

**Scope boundary:**
- **In scope:** `AttachmentDropzone` client component + the Create→Edit pending-file hand-off via `location.state`, `+ Mark visible cleared` and `Export CSV` buttons in the Movements page header (with `AlertDialog` confirmation on bulk), Razor 302 redirects + Razor view deletions for `Transactions` and `Transfers` controllers, deletion of the now-orphaned `Views/Shared/_AttachmentWidget.cshtml`, doc sync per spec §10.
- **Out of scope:** SPA-side attachment download (the file-serving still goes through the legacy `AttachmentsController` for now — its retirement is in the final SPA cleanup plan, not here). `[Obsolete]` removal on `BulkMarkCleared` / `ToggleCleared` Razor POST actions (also final cleanup). Liability Payment attachments (no entity exists; spec non-goal). Saved searches (separate brainstorm).

After Plan 3 ships, the SPA at `/app/movements*` is the canonical surface for movements: full CRUD, attachments, bulk-cleared, CSV export. Razor `/Transactions` and `/Transfers` URLs all 302-redirect to the SPA. Plans 1+2+3 together complete spec sequencing step 6.

---

## Prerequisites

Plans 1 and 2 must be merged before Plan 3 starts. Specifically the following must exist on `main`:

- All 14 server endpoints from Plan 1 (typed CRUD + attachments + bulk-cleared + export).
- `MovementsLayout`, `MovementForm`, `MovementCreate`, `MovementEdit`, `MovementRowMenu` from Plan 2.
- `parseValidationErrors` helper, the typed DTOs in `movements-api.ts`, and the `+ New` button on the page header.
- The SPA can fully Create / Edit / Delete movements end-to-end (without attachments — that's this plan).

---

## File Structure

**Created (React client):**
- `ProjectCeres.Client/src/app/features/movements/AttachmentDropzone.tsx` — drag-and-drop + file-picker fallback, per-file progress, existing-attachment list with delete buttons.
- `ProjectCeres.Client/src/app/features/movements/AttachmentDropzone.test.tsx`
- `ProjectCeres.Client/src/app/features/movements/movement-attachments.ts` — small helpers: `uploadAttachment(parentId, parentType, file, onProgress)`, `deleteAttachment(parentType, attachmentId)`. Centralizes the multipart POST and DELETE calls.
- `ProjectCeres.Client/src/app/features/movements/movement-attachments.test.ts`
- `ProjectCeres.Client/src/app/features/movements/MovementsBulkActions.tsx` — the page-header button group with `Mark visible cleared` and `Export CSV`. Reads filters from `useSearchParams`. Bulk fires an `AlertDialog` confirmation including the visible-row count.
- `ProjectCeres.Client/src/app/features/movements/MovementsBulkActions.test.tsx`

**Modified (React client):**
- `ProjectCeres.Client/src/app/features/movements/movements-api.ts` — add URL builders for the attachment endpoints (`POST /api/transactions/:id/attachments`, the transfer equivalent, the two DELETE-by-attachment-id variants), bulk-cleared, and export. Add request/response DTOs as needed.
- `ProjectCeres.Client/src/app/features/movements/MovementsLayout.tsx` — render `<MovementsBulkActions totalCount={data.totalCount} />` in the page header next to `+ New`. Pass the URL search params and the visible-row count down so the buttons can render the active filter snapshot.
- `ProjectCeres.Client/src/app/features/movements/MovementCreate.tsx` — capture a pending file from the user (file input on the form, disabled with explanatory tooltip until save) and pass it via `navigate(..., { state: { pendingAttachment: File } })` on the post-create redirect.
- `ProjectCeres.Client/src/app/features/movements/MovementCreate.test.tsx` — add the pending-file hand-off test.
- `ProjectCeres.Client/src/app/features/movements/MovementEdit.tsx` — render `<AttachmentDropzone parentType={resolvedType} parentId={id} initialAttachments={dto.attachments} />` (only on Edit, only when `resolvedType !== 'LiabilityPayment'`). Auto-upload any `location.state.pendingAttachment` on mount, then clear that piece of state via `navigate(location.pathname, { replace: true, state: {} })`.
- `ProjectCeres.Client/src/app/features/movements/MovementEdit.test.tsx` — add the pending-file auto-upload test, the manual upload test, the delete-attachment test.

**Modified (server, Razor demolition):**
- `ProjectCeres/Controllers/TransactionsController.cs` — every page-rendering action becomes a `Redirect("/app/...")` (302). The non-page-rendering actions (`ToggleCleared`, `BulkMarkCleared`) stay marked `[Obsolete]` per Checkpoint A — leave them; they'll be removed in a final cleanup plan. The Razor `Index` filter parameters (`accountId`, `from`, `to`, `page`) are dropped from the redirect — the SPA reconstructs URL state on its own.
- `ProjectCeres/Controllers/TransfersController.cs` — same treatment.
- `ProjectCeres/Views/Transactions/Create.cshtml` — DELETE.
- `ProjectCeres/Views/Transactions/Edit.cshtml` — DELETE.
- `ProjectCeres/Views/Transactions/Delete.cshtml` — DELETE.
- `ProjectCeres/Views/Transactions/Index.cshtml` — DELETE.
- `ProjectCeres/Views/Transfers/Create.cshtml` — DELETE.
- `ProjectCeres/Views/Transfers/Edit.cshtml` — DELETE.
- `ProjectCeres/Views/Transfers/Delete.cshtml` — DELETE.
- `ProjectCeres/Views/Transfers/Index.cshtml` — DELETE.
- `ProjectCeres/Views/Shared/_AttachmentWidget.cshtml` — DELETE (only consumed by the four doomed views; orphaned after demolition).

**Inbound Razor links** that point to `Transactions/Index` from other Razor pages — leave them. The 302 redirect is the contract; they'll seamlessly land on `/app/movements`. Identified call sites for the record (no action needed):
- `Views/TransferReview/Index.cshtml:14`
- `Views/ReconciliationReview/Index.cshtml:14`
- `Views/Import/Summary.cshtml:69`
- `Views/Import/Summary.cshtml:125`

**Modified (docs):**
- `docs/planning-phase3.md` — under §14 step 6, mark "Full Transactions/Transfers/LiabilityPayments CRUD" as ✓ migrated, with date and pointers to the spec + the three plans.
- `docs/planning-phase3-spa-migration.md` — update §2 controller table (Transactions row, Transfers row → both marked Migrated). Update §5 React Router route map (drop standalone `/app/transactions*` and `/app/transfers*` routes if still listed, add `/app/movements/new` and `/app/movements/:id/edit`).
- `docs/api-contract.md` — extend the endpoint reference with the typed CRUD endpoints, the discriminator, `bulk-cleared`, and `export.csv`. (Quick scan first: some of these may already be partially documented from earlier work.)
- `docs/CHANGELOG.md` — entry under `[Unreleased]` summarizing the Movements CRUD slice.

---

## Conventions used throughout this plan

- **Tests:** Vitest + React Testing Library on the client. xUnit + FluentAssertions on the server, but the server demolition is a thin shim (302 redirects) so the existing `MovementsApiTests` suite covers the actual behavior — no new server tests in this plan.
- **Each task ends with one commit.** Don't amend.
- **Imports:** match the existing client style (alphabetized within groups, `@/` aliases for shadcn primitives, relative paths for sibling modules).
- **Toasts:** `sonner` via `import { toast } from 'sonner'` — already wired.

---

## Task 1: Extend `movements-api.ts` with attachment + bulk + export URL builders and DTOs

**Files:**
- Modify: `ProjectCeres.Client/src/app/features/movements/movements-api.ts`

- [ ] **Step 1: Append**

```ts
// ---------- Attachment endpoints (Plan 1 server work) ----------

export const TRANSACTION_ATTACHMENTS_URL = (transactionId: string) =>
  `/api/transactions/${transactionId}/attachments`;
export const TRANSFER_ATTACHMENTS_URL = (transferId: string) =>
  `/api/transfers/${transferId}/attachments`;

export const TRANSACTION_ATTACHMENT_BY_ID_URL = (attachmentId: string) =>
  `/api/transactions/attachments/${attachmentId}`;
export const TRANSFER_ATTACHMENT_BY_ID_URL = (attachmentId: string) =>
  `/api/transfers/attachments/${attachmentId}`;

// ---------- Bulk-cleared and export ----------

export const MOVEMENTS_BULK_CLEARED_URL = '/api/movements/bulk-cleared';
export const MOVEMENTS_EXPORT_CSV_URL   = (search: string) =>
  search ? `/api/movements/export.csv?${search}` : '/api/movements/export.csv';

export type BulkClearedRequest = {
  from: string;            // "yyyy-MM-dd"
  to: string;              // "yyyy-MM-dd"
  accountId?: string | null;
  type?: 'transaction' | 'transfer' | 'liabilitypayment' | null;
};

export type BulkClearedResponse = { cleared: number };
```

- [ ] **Step 2: Build**

```bash
pnpm --dir ProjectCeres.Client build
```

Expected: PASS.

- [ ] **Step 3: Commit**

```bash
git add ProjectCeres.Client/src/app/features/movements/movements-api.ts
git commit -m "feat(spa): add attachment + bulk-cleared + export URL builders"
```

---

## Task 2: Implement `movement-attachments.ts` helper module

**Files:**
- Create: `ProjectCeres.Client/src/app/features/movements/movement-attachments.ts`
- Create: `ProjectCeres.Client/src/app/features/movements/movement-attachments.test.ts`

Two pure helpers:

- `uploadAttachment(parentType: 'Transaction' | 'Transfer', parentId: string, file: File): Promise<AttachmentDto>` — POSTs `multipart/form-data` to the right endpoint. Resolves with the saved `AttachmentDto` on 201; rejects with an `Error` whose `message` carries the 422 envelope's `error.message` (or a generic message for non-422 failures).
- `deleteAttachment(parentType: 'Transaction' | 'Transfer', attachmentId: string): Promise<void>` — DELETE; resolves on 204; rejects on 404.

We're NOT exposing per-file upload progress in this iteration — `fetch` doesn't expose upload progress without `XMLHttpRequest` plumbing, and the spec calls for the progress indicator but doesn't require true progress %. The dropzone (Task 3) will show a "Uploading…" spinner per file based on a pending-set Set in component state. Real progress percentages can be a follow-up if needed.

- [ ] **Step 1: Write failing tests**

```ts
import { describe, expect, it, vi, beforeEach, afterEach } from 'vitest';
import { uploadAttachment, deleteAttachment } from './movement-attachments';

const mockFetch = vi.fn();
beforeEach(() => { global.fetch = mockFetch as unknown as typeof fetch; });
afterEach(() => { vi.resetAllMocks(); });

describe('uploadAttachment', () => {
  it('POSTs multipart/form-data to the transaction endpoint', async () => {
    mockFetch.mockResolvedValueOnce({
      ok: true,
      status: 201,
      json: async () => ({
        id: 'a1', fileName: 'r.png', sizeBytes: 100,
        contentType: 'image/png', uploadedAt: '2026-04-30T10:00:00Z',
      }),
    });
    const file = new File([new Uint8Array([0x89, 0x50])], 'r.png', { type: 'image/png' });

    const result = await uploadAttachment('Transaction', 'tx-id', file);

    expect(mockFetch).toHaveBeenCalledWith(
      '/api/transactions/tx-id/attachments',
      expect.objectContaining({ method: 'POST', body: expect.any(FormData) }),
    );
    expect(result.fileName).toBe('r.png');
  });

  it('POSTs to the transfer endpoint when parentType is Transfer', async () => {
    mockFetch.mockResolvedValueOnce({
      ok: true, status: 201,
      json: async () => ({ id: 'a2', fileName: 'r.png', sizeBytes: 100, contentType: 'image/png', uploadedAt: '2026-04-30T10:00:00Z' }),
    });
    const file = new File([new Uint8Array([0x89])], 'r.png', { type: 'image/png' });
    await uploadAttachment('Transfer', 'tr-id', file);
    expect(mockFetch).toHaveBeenCalledWith('/api/transfers/tr-id/attachments', expect.anything());
  });

  it('rejects with the error.message on 422', async () => {
    mockFetch.mockResolvedValueOnce({
      ok: false, status: 422,
      json: async () => ({
        error: { code: 'VALIDATION_ERROR', message: 'File type not allowed.', details: [] },
      }),
    });
    const file = new File([new Uint8Array([0x00])], 'r.exe', { type: 'application/octet-stream' });
    await expect(uploadAttachment('Transaction', 'tx-id', file))
      .rejects.toThrow('File type not allowed.');
  });

  it('rejects with a generic message on other failures', async () => {
    mockFetch.mockResolvedValueOnce({ ok: false, status: 500, json: async () => ({}) });
    const file = new File([new Uint8Array([0x00])], 'r.png', { type: 'image/png' });
    await expect(uploadAttachment('Transaction', 'tx-id', file))
      .rejects.toThrow(/upload/i);
  });
});

describe('deleteAttachment', () => {
  it('DELETEs the transaction attachment endpoint', async () => {
    mockFetch.mockResolvedValueOnce({ ok: true, status: 204 });
    await deleteAttachment('Transaction', 'att-1');
    expect(mockFetch).toHaveBeenCalledWith('/api/transactions/attachments/att-1', expect.objectContaining({ method: 'DELETE' }));
  });

  it('DELETEs the transfer attachment endpoint', async () => {
    mockFetch.mockResolvedValueOnce({ ok: true, status: 204 });
    await deleteAttachment('Transfer', 'att-2');
    expect(mockFetch).toHaveBeenCalledWith('/api/transfers/attachments/att-2', expect.objectContaining({ method: 'DELETE' }));
  });

  it('rejects on non-204', async () => {
    mockFetch.mockResolvedValueOnce({ ok: false, status: 404 });
    await expect(deleteAttachment('Transaction', 'gone')).rejects.toThrow(/delete/i);
  });
});
```

- [ ] **Step 2: Run — expect FAIL**

```bash
pnpm --dir ProjectCeres.Client test movement-attachments
```

- [ ] **Step 3: Implement**

```ts
import {
  type AttachmentDto,
  TRANSACTION_ATTACHMENTS_URL,
  TRANSACTION_ATTACHMENT_BY_ID_URL,
  TRANSFER_ATTACHMENTS_URL,
  TRANSFER_ATTACHMENT_BY_ID_URL,
} from './movements-api';

export type AttachmentParentType = 'Transaction' | 'Transfer';

export async function uploadAttachment(
  parentType: AttachmentParentType,
  parentId: string,
  file: File,
): Promise<AttachmentDto> {
  const url = parentType === 'Transaction'
    ? TRANSACTION_ATTACHMENTS_URL(parentId)
    : TRANSFER_ATTACHMENTS_URL(parentId);

  const form = new FormData();
  form.append('file', file);

  const response = await fetch(url, { method: 'POST', body: form });

  if (response.status === 201) {
    return (await response.json()) as AttachmentDto;
  }

  if (response.status === 422) {
    const body = await response.json().catch(() => null);
    const message = body?.error?.message ?? 'Could not upload the file.';
    throw new Error(message);
  }

  throw new Error("Couldn't upload the file. Try again.");
}

export async function deleteAttachment(
  parentType: AttachmentParentType,
  attachmentId: string,
): Promise<void> {
  const url = parentType === 'Transaction'
    ? TRANSACTION_ATTACHMENT_BY_ID_URL(attachmentId)
    : TRANSFER_ATTACHMENT_BY_ID_URL(attachmentId);

  const response = await fetch(url, { method: 'DELETE' });

  if (response.status === 204) return;
  throw new Error("Couldn't delete the attachment. Try again.");
}
```

- [ ] **Step 4: Run — expect PASS**

- [ ] **Step 5: Commit**

```bash
git add ProjectCeres.Client/src/app/features/movements/movement-attachments.ts \
        ProjectCeres.Client/src/app/features/movements/movement-attachments.test.ts
git commit -m "feat(spa): movement-attachments helper (upload + delete)"
```

---

## Task 3: Build the `AttachmentDropzone` component

**Files:**
- Create: `ProjectCeres.Client/src/app/features/movements/AttachmentDropzone.tsx`
- Create: `ProjectCeres.Client/src/app/features/movements/AttachmentDropzone.test.tsx`

**Props contract:**

```ts
type AttachmentDropzoneProps = {
  parentType: 'Transaction' | 'Transfer';
  parentId: string;
  initialAttachments: AttachmentDto[];
  /** Optional file to auto-upload on mount (the Create→Edit hand-off). */
  pendingFile?: File | null;
  /** Called whenever the attachment list changes (added/removed). */
  onChange?: (attachments: AttachmentDto[]) => void;
};
```

**Behavior:**

- Internal state: `attachments: AttachmentDto[]` (initialized from `initialAttachments`), `uploading: Set<string>` (filenames currently in-flight).
- A drop region (drag-and-drop) and a file-input fallback button.
- On file drop / pick: optimistically push a placeholder (e.g., `{ id: temp-uuid, fileName: file.name, ... }`) and add the placeholder id to the `uploading` set. Call `uploadAttachment`. On success, replace the placeholder with the real `AttachmentDto`. On failure, remove the placeholder, show a `toast.error` with the error message.
- On mount, if `pendingFile` is present, kick off a single auto-upload immediately. Show a `toast.success` on success, `toast.error` on failure.
- Existing-attachment list: each item shows file name, size (formatted: `KB` / `MB`), upload date (formatted), and a `×` icon button. Clicking `×` opens an `AlertDialog` ("Delete this attachment?"). Confirm → call `deleteAttachment`, remove from local list, call `onChange`. Cancel → close dialog.
- If `parentType === 'LiabilityPayment'` is somehow passed (it's typed out), throw at runtime — defensive.

**Tests:**

1. Renders `initialAttachments` items with file name, size, and delete button.
2. Drag-and-drop a file → `uploadAttachment` called → after resolution, the new item appears in the list. `onChange` called with the updated list.
3. Click the file-picker fallback → choose a file → same as #2.
4. Upload failure → `toast.error` with the error message; placeholder removed; existing items intact.
5. Click `×` → confirm → `deleteAttachment` called → item removed.
6. Click `×` → cancel → no API call, item still present.
7. `pendingFile` prop on mount → `uploadAttachment` called immediately; toast shown.
8. Multiple uploads in flight → all show "Uploading…" UI simultaneously.

Implementation notes:

- For drag-and-drop, use the standard `onDragOver`/`onDrop` handlers on a wrapper div with visual feedback (border becomes primary color while dragging). Don't pull in a library — the standard browser API is enough.
- For the file picker fallback, use a hidden `<input type="file" multiple />` triggered by a `<Button>`.
- Show a banner above the dropzone if any file is in flight: "Uploading 2 files…". Cleared when the set is empty.

This component is the longest in Plan 3 (~250 lines). Lean on existing shadcn primitives (`Card`, `Button`, `AlertDialog` from Plan 2, `Skeleton` if you want for loading state).

- [ ] **Step 1: Write the 8 failing tests**
- [ ] **Step 2: Run — expect 8 FAILs**
- [ ] **Step 3: Implement**
- [ ] **Step 4: Run — expect 8 PASS**
- [ ] **Step 5: Build whole project**

```bash
pnpm --dir ProjectCeres.Client build
```

- [ ] **Step 6: Commit**

```bash
git add ProjectCeres.Client/src/app/features/movements/AttachmentDropzone.tsx \
        ProjectCeres.Client/src/app/features/movements/AttachmentDropzone.test.tsx
git commit -m "feat(spa): AttachmentDropzone component (upload + delete + auto-upload pending)"
```

---

## Task 4: Wire `AttachmentDropzone` into `MovementEdit`

**Files:**
- Modify: `ProjectCeres.Client/src/app/features/movements/MovementEdit.tsx`
- Modify: `ProjectCeres.Client/src/app/features/movements/MovementEdit.test.tsx`

`MovementEdit` from Plan 2 loads the typed entity and renders `<MovementForm>`. Now also render `<AttachmentDropzone>` below the form (or wherever fits the page layout best — judgment call) when `resolvedType !== 'LiabilityPayment'`.

The dropzone reads `pendingFile` from `useLocation().state.pendingAttachment`. After mount, **clear** that piece of state via `navigate(location.pathname + location.search, { replace: true, state: {} })` so a hard refresh of the same URL doesn't try to re-upload a file that no longer exists in browser memory. Spec §12 explicitly notes this is fragile-by-design and acceptable for the beta.

**Tests:**

- The dropzone is NOT rendered when the loaded entity is a Liability Payment.
- The dropzone IS rendered when the entity is Transaction.
- The dropzone IS rendered when the entity is Transfer.
- The `pendingAttachment` from `location.state` is passed to the dropzone as `pendingFile`.
- After mount, the location state is cleared (verify by reading `useLocation()` in a test spy).
- The Edit page's `initialAttachments` is sourced from the loaded DTO.

- [ ] **Step 1: Update tests** with the four assertions above.
- [ ] **Step 2: Run — expect FAILs**
- [ ] **Step 3: Implement**
- [ ] **Step 4: Run — expect PASS**
- [ ] **Step 5: Commit**

```bash
git add ProjectCeres.Client/src/app/features/movements/MovementEdit.tsx \
        ProjectCeres.Client/src/app/features/movements/MovementEdit.test.tsx
git commit -m "feat(spa): wire AttachmentDropzone into MovementEdit"
```

---

## Task 5: Capture pending file in `MovementCreate` and hand off via `location.state`

**Files:**
- Modify: `ProjectCeres.Client/src/app/features/movements/MovementCreate.tsx`
- Modify: `ProjectCeres.Client/src/app/features/movements/MovementCreate.test.tsx`

Per spec §6.2, the dropzone is disabled on Create. The simplest UX: a secondary **disabled** file input (or `<Button disabled>`) labeled "Attach receipt", with a tooltip / helper text saying "Save first, then drop receipts". The selected file is held in component state but doesn't upload.

When the user picks a file, store it in component state. On successful save:

```tsx
navigate(`/movements/${newId}/edit?created=1`, {
  replace: true,
  state: { pendingAttachment: pickedFile },
});
```

If no file was picked, the `state.pendingAttachment` is just `undefined` — Edit will render the dropzone in normal mode.

**Tests:**

1. The "Attach receipt" trigger is disabled before save (or shows an explanatory message). Clicking does not upload anything.
2. Picking a file stores it in state (verify by submitting and asserting the navigate call carries `pendingAttachment`).
3. On successful save with a picked file, `navigate` is called with `state.pendingAttachment` matching the picked `File`.
4. On successful save with no file picked, `navigate` is called with `state` either absent or with `pendingAttachment` undefined.

- [ ] **Step 1: Update tests**
- [ ] **Step 2: Run — expect FAILs**
- [ ] **Step 3: Implement**
- [ ] **Step 4: Run — expect PASS**
- [ ] **Step 5: Build full client**

```bash
pnpm --dir ProjectCeres.Client build
```

- [ ] **Step 6: Commit**

```bash
git add ProjectCeres.Client/src/app/features/movements/MovementCreate.tsx \
        ProjectCeres.Client/src/app/features/movements/MovementCreate.test.tsx
git commit -m "feat(spa): MovementCreate captures pending attachment, hands off to Edit via location.state"
```

---

## Task 6: Build `MovementsBulkActions` (page-header buttons)

**Files:**
- Create: `ProjectCeres.Client/src/app/features/movements/MovementsBulkActions.tsx`
- Create: `ProjectCeres.Client/src/app/features/movements/MovementsBulkActions.test.tsx`

Two buttons, side by side:

1. **`Mark visible cleared`** — opens an `AlertDialog` titled "Mark X movements as cleared?" where X is the totalCount the parent passes in. Description: "Movements matching the active filter will be marked cleared. This cannot be undone in bulk." Confirm → POST to `MOVEMENTS_BULK_CLEARED_URL` with `{ from, to, accountId, type }` from the URL params. On 200 → toast.success(`${count} marked cleared.`) and call `onAfterBulk()` (passed from parent — in Task 7 it'll be wired to `refetch`). On non-200 → toast.error.

2. **`Export CSV`** — clicking calls `window.location.href = MOVEMENTS_EXPORT_CSV_URL(searchParams.toString())`. The browser handles the download via the response `Content-Disposition: attachment` header. No spinner needed — the browser shows its own download progress.

The bulk button should be **disabled** when no `from` AND no `to` are set (bulk on the entire database is a footgun). Show a tooltip explaining: "Set a date range first to enable bulk actions". Export has no such constraint — exporting an unfiltered set is a valid operation.

**Props:**

```ts
type MovementsBulkActionsProps = {
  totalCount: number;
  onAfterBulk: () => void;
};
```

The component reads `useSearchParams()` directly for the filter values.

**Tests:**

1. Renders both buttons.
2. `Mark visible cleared` is disabled when no `from` and no `to` in URL params.
3. `Mark visible cleared` is enabled when at least one date is set; clicking opens the AlertDialog with the correct count in the title.
4. Confirming the bulk dialog POSTs to `/api/movements/bulk-cleared` with the right body, including `type` if present in URL params.
5. `Export CSV` click sets `window.location.href` to the export URL with the current search.

- [ ] **Step 1-5:** Standard test → fail → impl → pass → commit cycle.

```bash
git add ProjectCeres.Client/src/app/features/movements/MovementsBulkActions.tsx \
        ProjectCeres.Client/src/app/features/movements/MovementsBulkActions.test.tsx
git commit -m "feat(spa): MovementsBulkActions (Mark visible cleared + Export CSV)"
```

---

## Task 7: Wire `MovementsBulkActions` into `MovementsLayout`

**Files:**
- Modify: `ProjectCeres.Client/src/app/features/movements/MovementsLayout.tsx`
- Modify: `ProjectCeres.Client/src/app/features/movements/MovementsLayout.test.tsx`

In the page header, place `<MovementsBulkActions totalCount={data?.totalCount ?? 0} onAfterBulk={refetch} />` next to the existing `+ New` button. Layout: probably a flex row with the two action groups separated. Example:

```tsx
<div className="flex items-center justify-between">
  <h1 ref={headingRef} tabIndex={-1} className="text-3xl font-semibold outline-none">
    Movements
  </h1>
  <div className="flex items-center gap-2">
    <MovementsBulkActions
      totalCount={data?.totalCount ?? 0}
      onAfterBulk={refetch}
    />
    <Button asChild>
      <Link to="new">
        <Plus className="h-4 w-4" /> New
      </Link>
    </Button>
  </div>
</div>
```

When `data` is undefined (loading state), pass `totalCount={0}` — the buttons will render disabled or show "0 movements" in the bulk dialog title; that's fine because the user shouldn't bulk-clear during a loading flicker anyway.

**Tests:**

- The bulk actions render in the page header.
- The total count flows from the API response.
- `onAfterBulk` is wired to `refetch`.

- [ ] **Step 1: Update tests**
- [ ] **Step 2: Run — expect FAILs**
- [ ] **Step 3: Implement**
- [ ] **Step 4: Run — expect PASS**
- [ ] **Step 5: Commit**

```bash
git add ProjectCeres.Client/src/app/features/movements/MovementsLayout.tsx \
        ProjectCeres.Client/src/app/features/movements/MovementsLayout.test.tsx
git commit -m "feat(spa): wire MovementsBulkActions into Movements page header"
```

---

## Task 8: Razor demolition — `TransactionsController` and `TransfersController` actions become 302 redirects

**Files:**
- Modify: `ProjectCeres/Controllers/TransactionsController.cs`
- Modify: `ProjectCeres/Controllers/TransfersController.cs`

Per spec §8 (Razor demolition map):

For `TransactionsController`:

- `Index(...)` → `Redirect("/app/movements")` (drop all query params; SPA reconstructs).
- `Export(...)` → `Redirect("/app/movements")` (the SPA Export button replaces it).
- `Create()` (GET) → `Redirect("/app/movements/new?type=transaction")`.
- `Create(TransactionCreateViewModel)` (POST) → DELETE this overload entirely; the SPA POSTs JSON to `/api/transactions` directly. (Remove the `[HttpPost]` overload, the `[ValidateAntiForgeryToken]` attribute, and the body. Leave the GET-only `Create()` action above.)
- `Edit(Guid)` (GET) → `Redirect($"/app/movements/{id}/edit")`.
- `Edit(TransactionEditViewModel)` (POST) → DELETE.
- `Delete(Guid)` (GET) → `Redirect($"/app/movements/{id}/edit")` (Edit page hosts the danger-zone delete).
- `Delete(Guid, …)` (POST) → DELETE.

`ToggleCleared` and `BulkMarkCleared` actions stay marked `[Obsolete]` per Checkpoint A. **Don't** remove them in this task; they go in a future final-cleanup plan once the SPA toggle/bulk paths are confirmed in production.

For `TransfersController`: same pattern, no Export action exists, otherwise mirror.

After these changes, both controllers should be slim files with only:
- The class declaration.
- A constructor (untouched).
- `Index`, `Create` (GET only), `Edit` (GET only), `Delete` (GET only) → all 302 redirects.
- `ToggleCleared` (POST) — `[Obsolete]`, leave intact.
- For TransactionsController only: `Export` → 302, `BulkMarkCleared` → `[Obsolete]`, leave.

The dependency-injected services (`ITransactionService`, `ITransferService`, etc.) are no longer used by any of these actions. Remove them from the constructor parameter lists. The build will fail loudly if anything else in the controller still references them.

- [ ] **Step 1: Refactor `TransactionsController.cs`**

Read the current file. Rewrite the body. Sketch:

```csharp
using Microsoft.AspNetCore.Mvc;

namespace ProjectCeres.Controllers;

public class TransactionsController : Controller
{
    [HttpGet]
    public IActionResult Index() => Redirect("/app/movements");

    [HttpGet]
    public IActionResult Export() => Redirect("/app/movements");

    [HttpGet]
    public IActionResult Create() => Redirect("/app/movements/new?type=transaction");

    [HttpGet]
    public IActionResult Edit(Guid id) => Redirect($"/app/movements/{id}/edit");

    [HttpGet]
    public IActionResult Delete(Guid id) => Redirect($"/app/movements/{id}/edit");

    // [Obsolete] BulkMarkCleared and ToggleCleared stay until the SPA paths
    // are confirmed in production. Removed in the final SPA cleanup plan.
    [HttpPost]
    [Obsolete("Use PATCH /api/movements/{id}/cleared")]
    public IActionResult ToggleCleared(Guid id, bool cleared, ...)
    {
        // existing body — unchanged
    }

    [HttpPost]
    [Obsolete("Use POST /api/movements/bulk-cleared")]
    public IActionResult BulkMarkCleared(...)
    {
        // existing body — unchanged
    }
}
```

(Adjust to match the existing service injection: if `ToggleCleared` and `BulkMarkCleared` need services, keep those constructor parameters. If neither needs them, drop the constructor entirely.)

- [ ] **Step 2: Refactor `TransfersController.cs`** — same pattern, no Export, no BulkMarkCleared.

- [ ] **Step 3: Build the solution**

```bash
dotnet build
```

Expected: PASS. If something breaks, the most likely cause is a stray reference to one of the deleted POST overloads from a Razor view that still exists. Don't fix the view — it'll be deleted in Task 9.

- [ ] **Step 4: Run all tests** to confirm no API regression

```bash
dotnet test ProjectCeres.Tests
```

Expected: 423/423 PASS (the API-only test suite doesn't touch the Razor controllers; the Razor side has no integration tests).

- [ ] **Step 5: Commit**

```bash
git add ProjectCeres/Controllers/TransactionsController.cs \
        ProjectCeres/Controllers/TransfersController.cs
git commit -m "refactor(razor): TransactionsController + TransfersController page actions → 302 to /app/movements"
```

---

## Task 9: Delete the doomed Razor views

**Files (deleted):**
- `ProjectCeres/Views/Transactions/Create.cshtml`
- `ProjectCeres/Views/Transactions/Edit.cshtml`
- `ProjectCeres/Views/Transactions/Delete.cshtml`
- `ProjectCeres/Views/Transactions/Index.cshtml`
- `ProjectCeres/Views/Transfers/Create.cshtml`
- `ProjectCeres/Views/Transfers/Edit.cshtml`
- `ProjectCeres/Views/Transfers/Delete.cshtml`
- `ProjectCeres/Views/Transfers/Index.cshtml`
- `ProjectCeres/Views/Shared/_AttachmentWidget.cshtml`

- [ ] **Step 1: Confirm no remaining consumers of `_AttachmentWidget`**

```bash
grep -rn "_AttachmentWidget" ProjectCeres/Views/ ProjectCeres/Controllers/
```

Expected: zero matches (the four uses in the Tx + Tr Create/Edit views are about to be deleted; nothing else refers to it). If there ARE matches, investigate before deleting.

- [ ] **Step 2: Delete the files**

```bash
rm ProjectCeres/Views/Transactions/Create.cshtml \
   ProjectCeres/Views/Transactions/Edit.cshtml \
   ProjectCeres/Views/Transactions/Delete.cshtml \
   ProjectCeres/Views/Transactions/Index.cshtml \
   ProjectCeres/Views/Transfers/Create.cshtml \
   ProjectCeres/Views/Transfers/Edit.cshtml \
   ProjectCeres/Views/Transfers/Delete.cshtml \
   ProjectCeres/Views/Transfers/Index.cshtml \
   ProjectCeres/Views/Shared/_AttachmentWidget.cshtml
```

- [ ] **Step 3: Build**

```bash
dotnet build
```

Expected: PASS (the controllers no longer return `View()` so no view binding is needed).

- [ ] **Step 4: Run full test suite**

```bash
dotnet test ProjectCeres.Tests
```

Expected: all-green.

- [ ] **Step 5: Commit**

```bash
git add -A ProjectCeres/Views/
git commit -m "refactor(razor): delete Transactions + Transfers MVC views (replaced by SPA at /app/movements)"
```

---

## Task 10: Doc sync per spec §10

**Files:**
- Modify: `docs/planning-phase3.md`
- Modify: `docs/planning-phase3-spa-migration.md`
- Modify: `docs/api-contract.md`
- Modify: `docs/CHANGELOG.md`

### `docs/planning-phase3.md`

Find §14 step 6 ("Movements + Transactions + Transfers — highest daily usage"). Currently it lists Movements as ✓ migrated and Transactions/Transfers as pending. Update:

```
6. Movements + Transactions + Transfers — highest daily usage; includes quick-add, per-table search, saved searches
   - ✓ **Movements list page + quick-add (2026-04-30).** [existing line, unchanged]
   - ✓ **Full Transactions/Transfers/LiabilityPayments CRUD (2026-04-30).** SPA Movements supports Create/Edit/Delete via routed pages at `/app/movements/new` and `/app/movements/:id/edit`. Attachments via two-phase upload on Edit. Bulk-cleared and CSV export buttons in the page header. Razor `TransactionsController` and `TransfersController` page actions now 302-redirect to the SPA. See spec: `docs/superpowers/specs/2026-04-30-movements-crud.md`. Plans: `docs/superpowers/plans/2026-04-30-movements-crud-plan-{1,2,3}-*.md`.
   - Pending: Per-table search & saved searches (own brainstorm).
```

### `docs/planning-phase3-spa-migration.md`

Update §2 (controller table). The Transactions row currently lists Create as "partially served" with full CRUD pending; update to:

```
| `TransactionsController` | Index, Create, Edit, Delete | **Migrated (2026-04-30).** Page actions 302-redirect to `/app/movements*`. Razor views deleted. `BulkMarkCleared` and `ToggleCleared` POST actions remain `[Obsolete]` until final SPA cleanup. |
```

Same treatment for `TransfersController`.

In §5 (React Router route map), confirm the routes already include `/movements/new` and `/movements/:id/edit`. Add them if missing.

### `docs/api-contract.md`

Skim the file for the existing endpoint reference. Add (if missing) entries for:

- `GET /api/transactions/:id` — returns `TransactionEditDto`.
- `PUT /api/transactions/:id` — updates a transaction. Body: `UpdateTransactionRequest`. 422 on validation. 404 on missing.
- `DELETE /api/transactions/:id` — hard delete. 404 on missing.
- `POST /api/transactions/:id/attachments` — multipart upload. Returns `AttachmentDto`.
- `DELETE /api/transactions/attachments/:attachmentId` — deletes attachment.
- The Transfer and LiabilityPayment equivalents (LP has no attachment endpoints).
- `GET /api/movements/:id` — discriminator returns `{id, movementType}`.
- `POST /api/movements/bulk-cleared` — body `BulkClearedRequest`, returns `{cleared: int}`.
- `GET /api/movements/export.csv` — CSV download. Same query params as `GET /api/movements`.

If the file already has some of these, fold the rest in alongside.

### `docs/CHANGELOG.md`

Under `[Unreleased]`, add (or extend an existing Phase 3 section):

```markdown
### Added
- Full Movements CRUD on the SPA at `/app/movements*`: routed Create page (`/movements/new` with type picker), routed Edit page (`/movements/:id/edit`) with danger-zone delete, row-level ⋯ menu (Edit/Delete), type filter dropdown, attachment upload (two-phase save-first), bulk mark-cleared, CSV export.
- 22 typed API endpoints under `/api/transactions`, `/api/transfers`, `/api/liability-payments`, and `/api/movements` (see `docs/api-contract.md`).
- View-transition CSS hooks on movement rows and the form (animation upgrade is a follow-up).

### Changed
- Razor `TransactionsController` and `TransfersController` page actions (Index/Create/Edit/Delete) now 302-redirect to the SPA at `/app/movements*`.
- TopBar quick-add button and keyboard shortcut suppressed on `/app/movements*` routes.

### Removed
- Razor views for Transactions and Transfers (Index, Create, Edit, Delete) and `Views/Shared/_AttachmentWidget.cshtml`.
```

- [ ] **Step 1: Apply the four doc updates** (read each file first to find the right insertion points).

- [ ] **Step 2: Commit**

```bash
git add docs/planning-phase3.md \
        docs/planning-phase3-spa-migration.md \
        docs/api-contract.md \
        docs/CHANGELOG.md
git commit -m "docs(sync): movements CRUD migration complete; update planning + api-contract + CHANGELOG"
```

---

## Task 11: Manual smoke + final regression

**Files:** none (verification).

- [ ] **Step 1: Run the full client test suite**

```bash
pnpm --dir ProjectCeres.Client test
```

Expected: all-green.

- [ ] **Step 2: Run the production client build**

```bash
pnpm --dir ProjectCeres.Client build
```

Expected: PASS.

- [ ] **Step 3: Run the full server test suite**

```bash
dotnet test ProjectCeres.Tests
```

Expected: all-green.

- [ ] **Step 4: Build the whole .NET solution**

```bash
dotnet build
```

Expected: PASS, 0 warnings, 0 errors.

- [ ] **Step 5: Manual browser smoke**

Start the .NET app + Vite dev server. Walk through:

- `/Transactions` → 302 → `/app/movements` (URL bar updates to the SPA route).
- `/Transactions/Create` → 302 → `/app/movements/new?type=transaction` (the type picker pre-selects Transaction).
- `/Transactions/{id}/Edit` → 302 → `/app/movements/{id}/edit` (Edit page loads the right movement).
- `/Transfers` → 302 → `/app/movements`.
- `/Transfers/Create` → 302 → `/app/movements/new?type=transfer`.
- `/Transfers/{id}/Edit` → 302 → `/app/movements/{id}/edit`.
- On `/app/movements`, drop a date range and confirm `Mark visible cleared` enables. Click it; confirm the dialog count matches the visible row count. Confirm; verify the rows now show as cleared.
- Click `Export CSV` with a filter set; verify the downloaded CSV contains exactly the filtered rows.
- Open the Edit page on a transaction. Drag a receipt onto the dropzone; verify it uploads and appears in the attachment list. Click `×` on it; confirm the dialog; verify it's removed.
- On the Create page, pick a file via the disabled-attachment helper, save the transaction; verify the redirected Edit page auto-uploads the receipt.
- Open the Edit page on a Liability Payment; verify NO dropzone is rendered.

If anything fails, fix in the smallest commit possible and re-run.

---

## What Plan 3 ships — and what's done overall

After Task 11, the Movements CRUD slice (Plans 1, 2, 3) is complete. End-state:

- 22 typed API endpoints, fully tested.
- SPA `/app/movements*` is the canonical surface for movements:
  - List with text search + account filter + date range + type filter + pagination.
  - Routed Create + Edit pages with the shared `MovementForm` component.
  - Row-level ⋯ menu (Edit, Delete) with confirmation.
  - Two-phase attachment upload (Create → Edit hand-off via `location.state`).
  - Bulk mark-cleared and CSV export buttons in the page header.
- Razor `/Transactions` and `/Transfers` URLs all 302-redirect to the SPA.
- View-transition CSS hooks ready for a future animation polish PR.
- The TopBar quick-add (and its eventual keyboard shortcut) is suppressed on `/app/movements*` so the in-page form is the only Create surface there.

What's still pending after the slice (intentionally out of scope):

- **Saved searches & per-table search** — own brainstorm.
- **Animated list ↔ form transition** — view-transition hooks are placed; animation polish is a follow-up.
- **Global ⌘K behavior on `/app/movements*`** (filter out movement-type results) — folded into the search/saved-searches brainstorm.
- **Liability Payment attachments** — no entity today; revisit.
- **Razor `AttachmentsController` retirement** — file-serving still goes through Razor; the SPA-side download path moves in the final SPA cleanup plan.
- **`[Obsolete]` Razor POST endpoints removal** (`TransactionsController.BulkMarkCleared`, `ToggleCleared`, `TransfersController.ToggleCleared`) — final SPA cleanup.
