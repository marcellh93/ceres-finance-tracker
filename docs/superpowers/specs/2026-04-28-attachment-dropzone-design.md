# Attachment Drop Zone — Design Spec

> **Date:** 2026-04-28
> **Scope:** Multi-file upload UX/UI for Transaction and Transfer attachment forms
> **Status:** Approved

---

## Summary

Replace the single `<input type="file">` on Transaction Create/Edit and Transfer Create/Edit with a multi-file drag-and-drop widget. Users can queue multiple files before submitting, see and remove pending files, and (on Edit forms) see and remove already-saved attachments.

---

## UX Structure

### Empty state (Create form, or Edit with no saved files)

The widget shows only the drop zone — no saved section. The drop zone contains:

- Lucide `cloud-upload` icon (stroke, `w-8 h-8`, gray)
- Bold label: "Drop files here or click to browse"
- Sub-label: "JPEG, PNG, GIF, WebP, PDF · Max 10 MB each"
- Clicking anywhere in the zone opens the file picker

### With pending files (files selected but not yet submitted)

The centred prompt collapses. Inside the drop zone, each pending file appears as a **row tile**:

- File type icon (Lucide `image` for images, `file-text` for PDFs) — left
- Filename + file size — centre, filename truncated with ellipsis if too long
- ✕ button (Lucide `x`, red) — right; removes the file from the queue client-side only, no server call

Below the tiles, a small hint replaces the full prompt:
> "Drop more or **click to browse**" (browse link in app green `#248e38`)

### Edit form — saved attachments

When a transaction/transfer already has saved attachments, a **green-tinted card** appears **above** the drop zone:

- Background `#f0fdf4`, border `#bbf7d0` (green-100/green-200)
- Section label: "Saved" (small uppercase, green-700)
- Each saved file as a row: Lucide `file` icon (green-600), filename as a download link, file size, trash icon (red)
- Clicking the trash icon opens a **shadcn/ui Dialog** asking "Delete this attachment?" with Confirm (red) and Cancel buttons
- Confirm triggers the existing delete POST; dialog closes on cancel with no action

The saved section is hidden entirely when there are no saved attachments.

---

## UI Styling

Follows the app's existing shadcn/ui + Tailwind token set.

| Element | Style |
|---|---|
| Drop zone border | `border-2 border-dashed border-gray-300 rounded-lg` |
| Drop zone background | `bg-white` |
| Drop zone active (drag over) | border color shifts to `border-green-400`, background `bg-green-50` |
| "Add files" label | `text-[10px] font-semibold text-gray-500 uppercase tracking-wide` |
| Cloud upload icon | Lucide `cloud-upload`, `w-8 h-8 text-gray-400` |
| Pending tile | `border border-gray-200 rounded-md bg-gray-50 px-3 py-2 flex items-center gap-2` |
| Saved section background | `bg-green-50 border border-green-200 rounded-lg` |
| Saved section label | `text-[10px] font-semibold text-green-700 uppercase tracking-wide` |
| Saved file row | `bg-white border border-green-100 rounded-md px-3 py-2 flex items-center gap-2` |
| Browse link | `text-[#248e38] font-medium` |
| Remove (pending) | Lucide `x`, `text-red-500`, no border |
| Remove (saved) | Lucide `trash-2`, `text-red-500`, triggers shadcn Dialog |

---

## Behaviour

### Multi-file selection
- `<input type="file" multiple>` — hidden, triggered by click or drop
- Files are accumulated client-side in a JS array; selecting again appends, does not replace
- Duplicate filenames within the same pending queue are allowed (user may want two differently-named files that happen to share a name)

### Drag and drop
- `dragover` / `dragleave` on the drop zone toggle the active border/background
- `drop` extracts `event.dataTransfer.files` and appends to the pending queue
- Dropping on the saved section has no effect (it is outside the drop zone)

### Client-side validation (before submit)
- File type: check `file.type` against the allowed MIME whitelist; show an inline error tile (red border, error message) for rejected files — do not add them to the queue
- File size: reject files > 10 MB with the same inline error treatment
- These are UI-only pre-checks; server-side validation in `FileAttachmentService` is the authoritative gate

### Form submission
- All pending files are submitted together as `Attachments[]` (array of `IFormFile`) in the same multipart POST as the rest of the form
- View models change: `IFormFile? Attachment` → `IList<IFormFile> Attachments` (nullable stays, empty list = no upload)
- Controller loops over `Attachments` and calls `attachmentService.UploadAsync` for each

### Removing a saved attachment
- Trash icon → shadcn Dialog: "Delete **{filename}**? This cannot be undone."
- Confirm → `fetch('POST /Attachments/Delete/{id}')` with the AntiForgery token in the request header — no page reload
- On success: the file row is removed from the DOM; if no saved files remain the saved section is hidden
- On error: dialog closes, an inline error message appears below the saved section ("Could not delete file — please try again")
- Cancel → dialog closes, nothing happens

### Removing a pending file
- ✕ on a pending tile → removes from the JS array and re-renders the tile list
- No server call; file never leaves the browser until form submit

---

## Scope

Applies to:
- `Views/Transactions/Create.cshtml` — drop zone only (no saved section)
- `Views/Transactions/Edit.cshtml` — saved section + drop zone
- `Views/Transfers/Create.cshtml` — drop zone only (Stage 4, same widget)
- `Views/Transfers/Edit.cshtml` — saved section + drop zone (Stage 4)

The widget is implemented as a self-contained Razor partial `_AttachmentWidget.cshtml` so both Transaction and Transfer views share identical markup and JS with no duplication.

---

## Files Affected

| File | Change |
|---|---|
| `ProjectCeres/ViewModels/TransactionCreateViewModel.cs` | `IFormFile? Attachment` → `IList<IFormFile>? Attachments` |
| `ProjectCeres/ViewModels/TransactionEditViewModel.cs` | Same |
| `ProjectCeres/Controllers/TransactionsController.cs` | Loop over `Attachments`, call `UploadAsync` per file |
| `ProjectCeres/Views/Transactions/Create.cshtml` | Replace file input with `_AttachmentWidget` partial |
| `ProjectCeres/Views/Transactions/Edit.cshtml` | Same; pass saved attachments to partial |
| `ProjectCeres/Views/Shared/_AttachmentWidget.cshtml` | New — drop zone markup + inline JS |
| `ProjectCeres/Views/Transfers/Create.cshtml` | Same as Transaction Create (Stage 4) |
| `ProjectCeres/Views/Transfers/Edit.cshtml` | Same as Transaction Edit (Stage 4) |
| `ProjectCeres/ViewModels/TransferCreateViewModel.cs` | `IList<IFormFile>? Attachments` (Stage 4) |
| `ProjectCeres/ViewModels/TransferEditViewModel.cs` | Same (Stage 4) |
| `ProjectCeres/Controllers/TransfersController.cs` | Loop over `Attachments` (Stage 4) |

---

## Out of Scope

- Image thumbnail previews (show icon only, not a rendered image preview)
- Upload progress bar (files upload on form submit, not async)
- Drag-to-reorder
- Total storage cap UI (enforced server-side only)
