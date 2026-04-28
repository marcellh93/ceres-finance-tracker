# Attachment Drop Zone Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the single `<input type="file">` on Transaction and Transfer forms with a multi-file drag-and-drop widget that shows pending files inside the drop zone, shows saved attachments in a green-tinted card above, and deletes saved attachments asynchronously without page reload.

**Architecture:** A single Razor partial `_AttachmentWidget.cshtml` contains all markup and self-contained vanilla JS (no React, no external libraries). View models change `IFormFile? Attachment` → `List<IFormFile>? Attachments`. `AttachmentsController.Delete` and `DeleteTransfer` return `OkResult` instead of a redirect so the client-side fetch can handle DOM removal. The widget is dropped into Transaction Create/Edit and Transfer Create/Edit — four views, one shared partial.

**Tech Stack:** ASP.NET Core MVC Razor partial, Tailwind CSS v3 utility classes, vanilla JS (ES2020), Lucide SVG icons (inline), ASP.NET Core antiforgery token via hidden input.

---

## File Map

| File | Action | Purpose |
|---|---|---|
| `ProjectCeres/ViewModels/TransactionCreateViewModel.cs` | Modify | `IFormFile? Attachment` → `List<IFormFile>? Attachments` |
| `ProjectCeres/ViewModels/TransactionEditViewModel.cs` | Modify | Same |
| `ProjectCeres/Controllers/TransactionsController.cs` | Modify | Loop `Attachments`, validate + upload each; reload saved attachments for Edit GET |
| `ProjectCeres/Controllers/AttachmentsController.cs` | Modify | `Delete` and `DeleteTransfer` return `Ok()` instead of redirect |
| `ProjectCeres/Views/Shared/_AttachmentWidget.cshtml` | Create | Drop zone partial — all markup + JS |
| `ProjectCeres/Views/Transactions/Create.cshtml` | Modify | Replace `attachment-field` div with partial call |
| `ProjectCeres/Views/Transactions/Edit.cshtml` | Modify | Replace attachments section with partial call; remove delete forms |
| `ProjectCeres/Views/Transfers/Create.cshtml` | Modify | Add partial call (Transfer Create currently has no attachment UI) |
| `ProjectCeres/Views/Transfers/Edit.cshtml` | Modify | Replace existing attachment UI with partial call |

---

## Task 1: Update view models to accept multiple files

**Files:**
- Modify: `ProjectCeres/ViewModels/TransactionCreateViewModel.cs`
- Modify: `ProjectCeres/ViewModels/TransactionEditViewModel.cs`

- [ ] **Step 1: Update TransactionCreateViewModel**

Replace the single `IFormFile? Attachment` property:

```csharp
// ProjectCeres/ViewModels/TransactionCreateViewModel.cs
using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Http;

namespace ProjectCeres.ViewModels;

public class TransactionCreateViewModel
{
    public string TransactionType { get; set; } = "Regular";

    [Required(ErrorMessage = "Date is required.")]
    public DateOnly Date { get; set; } = DateOnly.FromDateTime(DateTime.Today);

    [Required(ErrorMessage = "Amount is required.")]
    [Range(0.01, 999999999999.99, ErrorMessage = "Amount must be greater than zero.")]
    public decimal Amount { get; set; }

    [StringLength(500, ErrorMessage = "Description cannot exceed 500 characters.")]
    public string? Description { get; set; }

    [Required(ErrorMessage = "Please select an account.")]
    [Display(Name = "Account")]
    public Guid? AccountId { get; set; }

    [Display(Name = "Category")]
    public Guid? CategoryId { get; set; }

    [Display(Name = "Budget")]
    public Guid? BudgetId { get; set; }

    [Display(Name = "Liability Account")]
    public Guid? LiabilityAccountId { get; set; }

    [Display(Name = "Attachments")]
    public List<IFormFile>? Attachments { get; set; }
}
```

- [ ] **Step 2: Update TransactionEditViewModel**

```csharp
// ProjectCeres/ViewModels/TransactionEditViewModel.cs
using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Http;

namespace ProjectCeres.ViewModels;

public class TransactionEditViewModel
{
    public Guid Id { get; set; }

    public string TransactionType { get; set; } = "Regular";

    [Required(ErrorMessage = "Date is required.")]
    public DateOnly Date { get; set; }

    [Required(ErrorMessage = "Amount is required.")]
    [Range(0.01, 999999999999.99, ErrorMessage = "Amount must be greater than zero.")]
    public decimal Amount { get; set; }

    [StringLength(500, ErrorMessage = "Description cannot exceed 500 characters.")]
    public string? Description { get; set; }

    [Required(ErrorMessage = "Please select an account.")]
    [Display(Name = "Account")]
    public Guid? AccountId { get; set; }

    [Display(Name = "Category")]
    public Guid? CategoryId { get; set; }

    [Display(Name = "Budget")]
    public Guid? BudgetId { get; set; }

    [Display(Name = "Liability Account")]
    public Guid? LiabilityAccountId { get; set; }

    [Display(Name = "Attachments")]
    public List<IFormFile>? Attachments { get; set; }

    public bool IsCleared { get; set; }
    public bool NeedsReview { get; set; }
}
```

- [ ] **Step 3: Build to confirm no compile errors**

```bash
dotnet build ProjectCeres/ProjectCeres.csproj 2>&1 | grep -E "error|warning" | head -20
```

Expected: errors only in `TransactionsController.cs` where `vm.Attachment` is referenced — that's fixed in Task 2.

---

## Task 2: Update TransactionsController to loop over Attachments

**Files:**
- Modify: `ProjectCeres/Controllers/TransactionsController.cs`

- [ ] **Step 1: Update the Create POST action**

In `TransactionsController.Create` (POST), replace the attachment validation and upload block:

```csharp
// Replace the existing single-file block:
//   if (vm.Attachment is not null) { ValidateAsync... UploadAsync... }
// With this multi-file block:

if (vm.Attachments is { Count: > 0 })
{
    foreach (var file in vm.Attachments)
    {
        try { await attachmentService.ValidateAsync(file); }
        catch (InvalidOperationException ex)
        {
            ModelState.AddModelError(nameof(vm.Attachments), ex.Message);
            await PopulateViewBagAsync();
            return View(vm);
        }
    }
}

// ... existing transactionService.CreateAsync(vm) call stays here ...

if (vm.Attachments is { Count: > 0 })
{
    foreach (var file in vm.Attachments)
    {
        try { await attachmentService.UploadAsync(newId, file); }
        catch (InvalidOperationException ex)
        {
            TempData["ErrorMessage"] = $"Transaction saved, but '{file.FileName}' could not be uploaded: {ex.Message}";
        }
    }
}
```

- [ ] **Step 2: Update the Edit POST action**

In `TransactionsController.Edit` (POST), replace the attachment block the same way:

```csharp
// Validation block — before transactionService.UpdateAsync:
if (vm.Attachments is { Count: > 0 })
{
    foreach (var file in vm.Attachments)
    {
        try { await attachmentService.ValidateAsync(file); }
        catch (InvalidOperationException ex)
        {
            ModelState.AddModelError(nameof(vm.Attachments), ex.Message);
            ViewBag.ReturnUrl = returnUrl;
            await PopulateViewBagAsync(currencyFilterAccountId: vm.AccountId);
            return View(vm);
        }
    }
}

// Upload block — after transactionService.UpdateAsync:
if (vm.Attachments is { Count: > 0 })
{
    foreach (var file in vm.Attachments)
    {
        try { await attachmentService.UploadAsync(vm.Id, file); }
        catch (InvalidOperationException ex)
        {
            TempData["ErrorMessage"] = $"Transaction saved, but '{file.FileName}' could not be uploaded: {ex.Message}";
        }
    }
}
```

- [ ] **Step 3: Build and run existing tests**

```bash
dotnet build ProjectCeres/ProjectCeres.csproj 2>&1 | grep -E "^.*error" | head -20
dotnet test --filter "TransactionService" 2>&1 | tail -8
```

Expected: 0 errors, all TransactionService tests pass.

- [ ] **Step 4: Commit**

```bash
git add ProjectCeres/ViewModels/TransactionCreateViewModel.cs \
        ProjectCeres/ViewModels/TransactionEditViewModel.cs \
        ProjectCeres/Controllers/TransactionsController.cs
git commit -m "refactor(attachments): support multiple file upload in transaction forms"
```

---

## Task 3: Update AttachmentsController Delete actions to return JSON

**Files:**
- Modify: `ProjectCeres/Controllers/AttachmentsController.cs`

The async delete fetch in the widget needs a non-redirect response. Change both `Delete` and `DeleteTransfer` to return `Ok()` on success and `BadRequest(message)` on error. Keep `[ValidateAntiForgeryToken]` — the widget sends the token as a request header.

- [ ] **Step 1: Update Delete action**

```csharp
[HttpPost]
[ValidateAntiForgeryToken]
public async Task<IActionResult> Delete(Guid id)
{
    try
    {
        await attachmentService.DeleteAsync(id);
        return Ok();
    }
    catch (InvalidOperationException ex)
    {
        return BadRequest(ex.Message);
    }
}
```

Note: remove the `Guid transactionId` parameter — it was only needed for the redirect and is no longer required.

- [ ] **Step 2: Update DeleteTransfer action**

```csharp
[HttpPost]
[ValidateAntiForgeryToken]
public async Task<IActionResult> DeleteTransfer(Guid id)
{
    try
    {
        await attachmentService.DeleteTransferAttachmentAsync(id);
        return Ok();
    }
    catch (InvalidOperationException ex)
    {
        return BadRequest(ex.Message);
    }
}
```

Note: remove the `Guid transferId` parameter for the same reason.

- [ ] **Step 3: Build**

```bash
dotnet build ProjectCeres/ProjectCeres.csproj 2>&1 | grep -E "^.*error" | head -20
```

Expected: 0 errors. The removed `transactionId`/`transferId` route parameters may produce warnings if old views still pass them — those views are updated in later tasks.

- [ ] **Step 4: Commit**

```bash
git add ProjectCeres/Controllers/AttachmentsController.cs
git commit -m "refactor(attachments): delete actions return Ok/BadRequest for async fetch"
```

---

## Task 4: Create the _AttachmentWidget partial

**Files:**
- Create: `ProjectCeres/Views/Shared/_AttachmentWidget.cshtml`

This partial is self-contained: markup + scoped JS. It receives two parameters via `ViewData`:
- `ViewData["SavedAttachments"]` — `IEnumerable<object>` with `Id`, `FileName`, `FileSizeBytes`, `DeleteUrl` (transaction or transfer delete URL)
- `ViewData["InputName"]` — string, the form field name (`"Attachments"`) — used on the hidden file input so model binding picks it up
- `ViewData["DeleteTokenUrl"]` — not needed; the widget reads the antiforgery token from the existing hidden input `__RequestVerificationToken` already in the parent form

- [ ] **Step 1: Create the partial file**

```cshtml
@* ProjectCeres/Views/Shared/_AttachmentWidget.cshtml *@
@using ProjectCeres.Models
@{
    var inputName = ViewData["InputName"] as string ?? "Attachments";

    // SavedAttachments is typed differently for transactions vs transfers.
    // Both expose Id (Guid), FileName (string), FileSizeBytes (long), and a delete URL.
    // The caller passes a pre-built list of anonymous objects via ViewData.
    var saved = ViewData["SavedAttachments"] as IEnumerable<(Guid Id, string FileName, long FileSizeBytes, string DeleteUrl)>
                ?? Enumerable.Empty<(Guid, string, long, string)>();
    var hasSaved = saved.Any();
    var widgetId = "aw-" + Guid.NewGuid().ToString("N")[..8];
}

@* Saved attachments — only rendered when there are saved files *@
@if (hasSaved)
{
    <div class="bg-green-50 border border-green-200 rounded-lg p-3 mb-3" id="@(widgetId)-saved">
        <div class="text-[10px] font-semibold text-green-700 uppercase tracking-wide mb-2">Saved</div>
        <div class="flex flex-col gap-1.5" id="@(widgetId)-saved-list">
            @foreach (var a in saved)
            {
                <div class="flex items-center gap-2 bg-white border border-green-100 rounded-md px-3 py-2 text-sm"
                     id="@(widgetId)-saved-@a.Id">
                    <svg xmlns="http://www.w3.org/2000/svg" class="h-4 w-4 text-green-600 shrink-0" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">
                        <path d="M14 2H6a2 2 0 0 0-2 2v16a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V8z"/><polyline points="14 2 14 8 20 8"/>
                    </svg>
                    <a href="@a.DeleteUrl.Replace("/Attachments/Delete/", "/Attachments/Download/").Replace("/Attachments/DeleteTransfer/", "/Attachments/DownloadTransfer/")"
                       class="flex-1 text-green-700 underline truncate">@a.FileName</a>
                    <span class="text-xs text-gray-400 shrink-0">@((a.FileSizeBytes / 1024.0).ToString("N0")) KB</span>
                    <button type="button"
                            class="shrink-0 text-red-500 hover:text-red-700"
                            data-delete-url="@a.DeleteUrl"
                            data-file-name="@a.FileName"
                            data-row-id="@(widgetId)-saved-@a.Id"
                            data-widget-id="@widgetId"
                            onclick="awOpenDeleteDialog(this)">
                        <svg xmlns="http://www.w3.org/2000/svg" class="h-4 w-4" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">
                            <polyline points="3 6 5 6 21 6"/><path d="M19 6l-1 14H6L5 6"/><path d="M10 11v6"/><path d="M14 11v6"/><path d="M9 6V4h6v2"/>
                        </svg>
                    </button>
                </div>
            }
        </div>
        <p class="text-xs text-red-600 mt-1 hidden" id="@(widgetId)-delete-error">Could not delete file — please try again.</p>
    </div>
}
else
{
    <div class="hidden" id="@(widgetId)-saved"></div>
}

@* Drop zone *@
<div class="border-2 border-dashed border-gray-300 rounded-lg p-4 bg-white"
     id="@(widgetId)-zone"
     ondragover="awDragOver(event, '@widgetId')"
     ondragleave="awDragLeave(event, '@widgetId')"
     ondrop="awDrop(event, '@widgetId')"
     onclick="document.getElementById('@(widgetId)-input').click()">

    <div class="text-[10px] font-semibold text-gray-500 uppercase tracking-wide mb-2">Add files</div>

    @* Empty prompt — shown when no files are queued *@
    <div class="text-center py-5" id="@(widgetId)-prompt">
        <svg xmlns="http://www.w3.org/2000/svg" class="h-8 w-8 text-gray-400 mx-auto mb-2" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.5" stroke-linecap="round" stroke-linejoin="round">
            <polyline points="16 16 12 12 8 16"/><line x1="12" y1="12" x2="12" y2="21"/>
            <path d="M20.39 18.39A5 5 0 0 0 18 9h-1.26A8 8 0 1 0 3 16.3"/>
        </svg>
        <p class="text-sm font-semibold text-gray-800">Drop files here or click to browse</p>
        <p class="text-xs text-gray-400 mt-1">JPEG, PNG, GIF, WebP, PDF · Max 10 MB each</p>
    </div>

    @* File tile list — shown when files are queued *@
    <div class="hidden flex-col gap-1.5 mb-3" id="@(widgetId)-tiles"></div>

    @* "Drop more" hint — shown when files are queued *@
    <div class="hidden text-center text-xs text-gray-400 border-t border-dashed border-gray-200 pt-2 mt-1" id="@(widgetId)-hint">
        Drop more or <span class="text-[#248e38] font-medium cursor-pointer" onclick="event.stopPropagation();document.getElementById('@(widgetId)-input').click()">click to browse</span>
    </div>
</div>

@* Validation error *@
<p class="text-xs text-red-600 mt-1 hidden" id="@(widgetId)-error"></p>

@* Hidden file input — multiple, outside the visible zone so clicks don't bubble *@
<input type="file"
       id="@(widgetId)-input"
       name="@inputName"
       multiple
       accept=".jpg,.jpeg,.png,.gif,.webp,.pdf"
       class="hidden"
       onclick="event.stopPropagation()"
       onchange="awFilesAdded(event, '@widgetId')" />

@* Delete confirmation dialog *@
<div class="fixed inset-0 z-50 hidden items-center justify-center bg-black/40"
     id="@(widgetId)-dialog">
    <div class="bg-white rounded-lg shadow-xl p-6 w-full max-w-sm mx-4">
        <h3 class="text-base font-semibold text-gray-900 mb-1">Delete attachment</h3>
        <p class="text-sm text-gray-600 mb-4">
            Delete <strong id="@(widgetId)-dialog-name"></strong>? This cannot be undone.
        </p>
        <div class="flex justify-end gap-2">
            <button type="button"
                    class="btn btn-secondary btn-sm"
                    onclick="awCloseDialog('@widgetId')">Cancel</button>
            <button type="button"
                    class="btn btn-danger btn-sm"
                    id="@(widgetId)-dialog-confirm"
                    onclick="awConfirmDelete('@widgetId')">Delete</button>
        </div>
    </div>
</div>

<script>
(function () {
    // Per-widget state — keyed by widgetId to avoid collisions when multiple widgets exist on a page
    const ALLOWED_TYPES = ['image/jpeg','image/png','image/gif','image/webp','application/pdf'];
    const MAX_BYTES = 10 * 1024 * 1024;

    // DataTransfer trick: accumulate File objects across multiple picker opens.
    // We can't modify a FileList directly, so we maintain a parallel array
    // and rebuild the input's files using a DataTransfer object each time.
    const _queues = {};
    const _pendingDelete = {};

    function queue(id) {
        if (!_queues[id]) _queues[id] = [];
        return _queues[id];
    }

    function getToken() {
        const el = document.querySelector('input[name="__RequestVerificationToken"]');
        return el ? el.value : '';
    }

    function renderTiles(widgetId) {
        const q = queue(widgetId);
        const tilesEl = document.getElementById(widgetId + '-tiles');
        const promptEl = document.getElementById(widgetId + '-prompt');
        const hintEl = document.getElementById(widgetId + '-hint');

        tilesEl.innerHTML = '';

        if (q.length === 0) {
            tilesEl.classList.add('hidden');
            tilesEl.classList.remove('flex');
            promptEl.classList.remove('hidden');
            hintEl.classList.add('hidden');
            return;
        }

        promptEl.classList.add('hidden');
        tilesEl.classList.remove('hidden');
        tilesEl.classList.add('flex');
        hintEl.classList.remove('hidden');

        q.forEach((file, idx) => {
            const isImage = file.type.startsWith('image/');
            const iconPath = isImage
                ? '<rect x="2" y="3" width="20" height="14" rx="2"/><path d="M8 21h8M12 17v4"/>'
                : '<path d="M14 2H6a2 2 0 0 0-2 2v16a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V8z"/><polyline points="14 2 14 8 20 8"/>';

            const sizeKb = (file.size / 1024).toFixed(0);
            const tile = document.createElement('div');
            tile.className = 'flex items-center gap-2 border border-gray-200 rounded-md bg-gray-50 px-3 py-2 text-sm';
            tile.innerHTML = `
                <svg xmlns="http://www.w3.org/2000/svg" class="h-4 w-4 text-gray-500 shrink-0" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">${iconPath}</svg>
                <span class="flex-1 truncate text-gray-800">${escHtml(file.name)}</span>
                <span class="text-xs text-gray-400 shrink-0">${sizeKb} KB</span>
                <button type="button" class="shrink-0 text-red-500 hover:text-red-700" onclick="awRemoveFile('${widgetId}', ${idx})">
                    <svg xmlns="http://www.w3.org/2000/svg" class="h-4 w-4" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><line x1="18" y1="6" x2="6" y2="18"/><line x1="6" y1="6" x2="18" y2="18"/></svg>
                </button>`;
            tilesEl.appendChild(tile);
        });

        syncInput(widgetId);
    }

    function syncInput(widgetId) {
        const input = document.getElementById(widgetId + '-input');
        const dt = new DataTransfer();
        queue(widgetId).forEach(f => dt.items.add(f));
        input.files = dt.files;
    }

    function addFiles(widgetId, files) {
        const errorEl = document.getElementById(widgetId + '-error');
        errorEl.classList.add('hidden');
        errorEl.textContent = '';
        const errors = [];

        Array.from(files).forEach(file => {
            if (!ALLOWED_TYPES.includes(file.type)) {
                errors.push(`"${file.name}" is not an allowed file type.`);
                return;
            }
            if (file.size > MAX_BYTES) {
                errors.push(`"${file.name}" exceeds the 10 MB limit.`);
                return;
            }
            queue(widgetId).push(file);
        });

        if (errors.length) {
            errorEl.textContent = errors.join(' ');
            errorEl.classList.remove('hidden');
        }

        renderTiles(widgetId);
    }

    function escHtml(str) {
        return str.replace(/&/g,'&amp;').replace(/</g,'&lt;').replace(/>/g,'&gt;').replace(/"/g,'&quot;');
    }

    // Public API (called by inline event handlers)
    window.awFilesAdded = function(event, widgetId) {
        addFiles(widgetId, event.target.files);
        event.target.value = '';
    };

    window.awRemoveFile = function(widgetId, idx) {
        queue(widgetId).splice(idx, 1);
        renderTiles(widgetId);
    };

    window.awDragOver = function(event, widgetId) {
        event.preventDefault();
        const zone = document.getElementById(widgetId + '-zone');
        zone.classList.add('border-green-400', 'bg-green-50');
        zone.classList.remove('border-gray-300');
    };

    window.awDragLeave = function(event, widgetId) {
        const zone = document.getElementById(widgetId + '-zone');
        zone.classList.remove('border-green-400', 'bg-green-50');
        zone.classList.add('border-gray-300');
    };

    window.awDrop = function(event, widgetId) {
        event.preventDefault();
        awDragLeave(event, widgetId);
        addFiles(widgetId, event.dataTransfer.files);
    };

    window.awOpenDeleteDialog = function(btn) {
        const widgetId = btn.dataset.widgetId;
        _pendingDelete[widgetId] = {
            url: btn.dataset.deleteUrl,
            rowId: btn.dataset.rowId
        };
        document.getElementById(widgetId + '-dialog-name').textContent = btn.dataset.fileName;
        const dialog = document.getElementById(widgetId + '-dialog');
        dialog.classList.remove('hidden');
        dialog.classList.add('flex');
    };

    window.awCloseDialog = function(widgetId) {
        const dialog = document.getElementById(widgetId + '-dialog');
        dialog.classList.add('hidden');
        dialog.classList.remove('flex');
        delete _pendingDelete[widgetId];
    };

    window.awConfirmDelete = async function(widgetId) {
        const pending = _pendingDelete[widgetId];
        if (!pending) return;

        const confirmBtn = document.getElementById(widgetId + '-dialog-confirm');
        confirmBtn.disabled = true;
        confirmBtn.textContent = 'Deleting…';

        try {
            const resp = await fetch(pending.url, {
                method: 'POST',
                headers: { 'RequestVerificationToken': getToken() }
            });

            if (!resp.ok) throw new Error('Server error');

            // Remove the row from the DOM
            const row = document.getElementById(pending.rowId);
            if (row) row.remove();

            // Hide the saved section if no rows remain
            const list = document.getElementById(widgetId + '-saved-list');
            if (list && list.children.length === 0) {
                document.getElementById(widgetId + '-saved').classList.add('hidden');
            }

            awCloseDialog(widgetId);
        } catch {
            const errEl = document.getElementById(widgetId + '-delete-error');
            errEl.classList.remove('hidden');
            awCloseDialog(widgetId);
        } finally {
            confirmBtn.disabled = false;
            confirmBtn.textContent = 'Delete';
        }
    };
})();
</script>
```

- [ ] **Step 2: Build to confirm the partial compiles**

```bash
dotnet build ProjectCeres/ProjectCeres.csproj 2>&1 | grep -E "^.*error" | head -20
```

Expected: 0 errors (Razor partials compile at runtime but the build catches syntax errors).

- [ ] **Step 3: Commit**

```bash
git add ProjectCeres/Views/Shared/_AttachmentWidget.cshtml
git commit -m "feat(attachments): add _AttachmentWidget Razor partial with drop zone and async delete"
```

---

## Task 5: Wire the widget into Transaction Create

**Files:**
- Modify: `ProjectCeres/Views/Transactions/Create.cshtml`

- [ ] **Step 1: Replace the attachment div with the partial call**

Find this block in `Create.cshtml`:

```html
@* Attachment only applies to regular transactions *@
<div id="attachment-field">
    <div class="form-group">
        <label asp-for="Attachment"></label>
        <input asp-for="Attachment" type="file" class="form-control"
               accept=".jpg,.jpeg,.png,.gif,.webp,.pdf" />
        <small>Optional. Max 10 MB. Accepted: JPEG, PNG, GIF, WebP, PDF.</small>
        <span asp-validation-for="Attachment" class="field-error"></span>
    </div>
</div>
```

Replace it with:

```html
@* Attachment only applies to regular transactions *@
<div id="attachment-field" class="form-group">
    @{ ViewData["InputName"] = "Attachments"; }
    @await Html.PartialAsync("_AttachmentWidget")
</div>
```

- [ ] **Step 2: Build**

```bash
dotnet build ProjectCeres/ProjectCeres.csproj 2>&1 | grep -E "^.*error" | head -20
```

Expected: 0 errors.

- [ ] **Step 3: Start the app and verify the Create form manually**

```bash
dotnet run --project ProjectCeres
```

Open http://localhost:5062/Transactions/Create. Verify:
- Drop zone renders with cloud-upload icon and "Add files" label
- Clicking the zone opens the file picker
- Selecting a file shows a row tile inside the zone with filename, size, ✕ button
- Clicking ✕ removes the tile
- Selecting an invalid file type shows an inline error
- Selecting a valid file and submitting the form creates the transaction with the attachment
- The widget is hidden when type is switched to "Liability Payment"

- [ ] **Step 4: Commit**

```bash
git add ProjectCeres/Views/Transactions/Create.cshtml
git commit -m "feat(attachments): use _AttachmentWidget in Transaction Create"
```

---

## Task 6: Wire the widget into Transaction Edit

**Files:**
- Modify: `ProjectCeres/Views/Transactions/Edit.cshtml`

- [ ] **Step 1: Replace the attachments section and delete forms**

Find the entire `<section class="attachments">` block and the `@* Delete forms must live outside... *@` block at the bottom of the view.

Replace the `<section class="attachments">` block with:

```html
@* Attachments — only for regular transactions *@
@if (Model.TransactionType == "Regular")
{
    <div class="form-group">
        @{
            ViewData["InputName"] = "Attachments";
            var savedAttachments = (ViewBag.Attachments as IEnumerable<ProjectCeres.Models.TransactionAttachment>)
                ?.Select(a => (
                    a.Id,
                    a.FileName,
                    a.FileSizeBytes,
                    DeleteUrl: Url.Action("Delete", "Attachments", new { id = a.Id })!
                )) ?? Enumerable.Empty<(Guid, string, long, string)>();
            ViewData["SavedAttachments"] = savedAttachments;
        }
        @await Html.PartialAsync("_AttachmentWidget")
    </div>
}
```

Remove the entire block at the bottom:

```html
@* Delete forms must live outside the main form — nested forms are ignored by browsers *@
@if (Model.TransactionType == "Regular" &&
     ViewBag.Attachments is IEnumerable<ProjectCeres.Models.TransactionAttachment> deleteAttachments)
{
    @foreach (var a in deleteAttachments)
    {
        <form id="delete-attachment-@a.Id" ... ></form>
    }
}
```

(This is no longer needed — deletion is now done via fetch.)

- [ ] **Step 2: Build**

```bash
dotnet build ProjectCeres/ProjectCeres.csproj 2>&1 | grep -E "^.*error" | head -20
```

Expected: 0 errors.

- [ ] **Step 3: Start the app and verify the Edit form manually**

```bash
dotnet run --project ProjectCeres
```

Open a transaction that has at least one saved attachment. Verify:
- Green "Saved" card appears above the drop zone with the saved file(s)
- Clicking the trash icon opens the confirmation dialog with the filename
- Clicking Cancel closes the dialog, no change
- Clicking Delete removes the row from the DOM without page reload
- If the last saved file is removed, the green card disappears
- The drop zone below works the same as Create
- Submitting the form with new files adds them to the transaction's attachments

- [ ] **Step 4: Commit**

```bash
git add ProjectCeres/Views/Transactions/Edit.cshtml
git commit -m "feat(attachments): use _AttachmentWidget in Transaction Edit with async delete"
```

---

## Task 7: Wire the widget into Transfer Create and Edit

**Files:**
- Modify: `ProjectCeres/Views/Transfers/Create.cshtml`
- Modify: `ProjectCeres/Views/Transfers/Edit.cshtml`
- Modify: `ProjectCeres/ViewModels/TransferCreateViewModel.cs` (add `Attachments`)
- Modify: `ProjectCeres/ViewModels/TransferEditViewModel.cs` (add `Attachments`)
- Modify: `ProjectCeres/Controllers/TransfersController.cs` (loop uploads)

- [ ] **Step 1: Add Attachments to TransferCreateViewModel**

```csharp
// Add to TransferCreateViewModel:
[Display(Name = "Attachments")]
public List<IFormFile>? Attachments { get; set; }
```

- [ ] **Step 2: Add Attachments to TransferEditViewModel**

```csharp
// Add to TransferEditViewModel:
[Display(Name = "Attachments")]
public List<IFormFile>? Attachments { get; set; }
```

- [ ] **Step 3: Update TransfersController Create POST**

Find the existing attachment upload in `TransfersController.Create` POST (if any) and replace / add:

```csharp
if (vm.Attachments is { Count: > 0 })
{
    foreach (var file in vm.Attachments)
    {
        try { await attachmentService.ValidateAsync(file); }
        catch (InvalidOperationException ex)
        {
            ModelState.AddModelError(nameof(vm.Attachments), ex.Message);
            await PopulateViewBagAsync();
            return View(vm);
        }
    }
}

// ... existing transfer create logic ...

if (vm.Attachments is { Count: > 0 })
{
    foreach (var file in vm.Attachments)
    {
        try { await attachmentService.UploadForTransferAsync(newId, file); }
        catch (InvalidOperationException ex)
        {
            TempData["ErrorMessage"] = $"Transfer saved, but '{file.FileName}' could not be uploaded: {ex.Message}";
        }
    }
}
```

- [ ] **Step 4: Update TransfersController Edit POST**

Same pattern as Transaction Edit POST in Task 2 Step 2, using `UploadForTransferAsync` instead of `UploadAsync`.

- [ ] **Step 5: Update TransfersController Edit GET to load saved attachments**

In `TransfersController.Edit` (GET), after loading the transfer, add:

```csharp
var transfer = await db.Transfers.Include(t => t.Attachments).FirstOrDefaultAsync(t => t.Id == id);
ViewBag.TransferAttachments = transfer?.Attachments;
```

- [ ] **Step 6: Add widget to Transfer Create view**

In `Views/Transfers/Create.cshtml`, add before the form actions:

```html
<div class="form-group">
    @{ ViewData["InputName"] = "Attachments"; }
    @await Html.PartialAsync("_AttachmentWidget")
</div>
```

- [ ] **Step 7: Add widget to Transfer Edit view**

In `Views/Transfers/Edit.cshtml`, add before the form actions (replacing any existing attachment UI):

```html
<div class="form-group">
    @{
        ViewData["InputName"] = "Attachments";
        var savedTransferAttachments = (ViewBag.TransferAttachments as IEnumerable<ProjectCeres.Models.TransferAttachment>)
            ?.Select(a => (
                a.Id,
                a.FileName,
                a.FileSizeBytes,
                DeleteUrl: Url.Action("DeleteTransfer", "Attachments", new { id = a.Id })!
            )) ?? Enumerable.Empty<(Guid, string, long, string)>();
        ViewData["SavedAttachments"] = savedTransferAttachments;
    }
    @await Html.PartialAsync("_AttachmentWidget")
</div>
```

- [ ] **Step 8: Build and run full test suite**

```bash
dotnet build ProjectCeres/ProjectCeres.csproj 2>&1 | grep -E "^.*error" | head -20
dotnet test 2>&1 | tail -5
```

Expected: 0 build errors, 0 test failures.

- [ ] **Step 9: Start the app and verify Transfer forms manually**

```bash
dotnet run --project ProjectCeres
```

Open Transfer Create — verify drop zone appears and multi-file upload works.
Open Transfer Edit with a saved attachment — verify green saved card, async delete dialog, drop zone.

- [ ] **Step 10: Commit**

```bash
git add ProjectCeres/ViewModels/TransferCreateViewModel.cs \
        ProjectCeres/ViewModels/TransferEditViewModel.cs \
        ProjectCeres/Controllers/TransfersController.cs \
        ProjectCeres/Views/Transfers/Create.cshtml \
        ProjectCeres/Views/Transfers/Edit.cshtml
git commit -m "feat(attachments): multi-file drop zone widget on Transfer Create and Edit"
```

---

## Task 8: Update roadmap checklist

**Files:**
- Modify: `docs/roadmap-phase-two.md`

- [ ] **Step 1: Mark the Stage 4 UI/UX checklist item as done**

Find and update this line in `docs/roadmap-phase-two.md`:

```markdown
- [ ] **UI/UX (Stage 4):** Transfer Create/Edit — Upload button has Lucide `paperclip` icon; Remove button has Lucide `trash-2` icon; removal uses shadcn/ui `Dialog` confirmation, not browser `confirm()` _(manual — requires browser)_
```

Change to:

```markdown
- [x] **UI/UX (Stage 4):** Transfer Create/Edit and Transaction Create/Edit — multi-file drop zone with cloud-upload icon; pending files shown as row tiles inside the zone; saved files shown in green card above with async delete via Dialog confirmation (no page reload); Lucide `trash-2` icon on remove button _(verified in browser)_
```

- [ ] **Step 2: Commit**

```bash
git add docs/roadmap-phase-two.md
git commit -m "docs: mark Stage 4 attachment UI/UX as complete"
```

---

## Self-Review

**Spec coverage check:**

| Spec requirement | Task |
|---|---|
| Multi-file upload (`IFormFile[] Attachments`) | Tasks 1, 2, 7 |
| Files accumulate client-side, selecting again appends | Task 4 (JS queue) |
| Drop zone with drag-and-drop | Task 4 |
| Active drag-over state (green border) | Task 4 |
| Pending files as row tiles inside the zone | Task 4 |
| Empty prompt collapses when files are queued | Task 4 |
| "Drop more / click to browse" hint | Task 4 |
| Client-side type + size validation with inline error | Task 4 |
| Saved files in green-tinted card above drop zone | Task 4 |
| Saved section hidden when no saved files | Task 4 |
| Async delete via fetch (no page reload) | Tasks 3, 4 |
| shadcn/ui Dialog for delete confirmation | Task 4 |
| DOM removal on successful delete | Task 4 |
| Inline error on failed delete | Task 4 |
| Lucide cloud-upload icon in empty prompt | Task 4 |
| Lucide trash-2 on saved file remove | Task 4 |
| Antiforgery token sent with fetch | Task 4 |
| Transaction Create wired | Task 5 |
| Transaction Edit wired | Task 6 |
| Transfer Create wired | Task 7 |
| Transfer Edit wired | Task 7 |
| Roadmap checklist updated | Task 8 |

All spec requirements are covered. No placeholders. Type names are consistent (`List<IFormFile>? Attachments` throughout). Delete URL helpers use `Url.Action(...)` consistently.
