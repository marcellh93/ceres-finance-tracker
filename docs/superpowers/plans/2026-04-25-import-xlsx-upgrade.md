# Import System Upgrade — CSV + XLSX Support Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Upgrade the import system to accept both CSV and XLSX bank exports by introducing an `IImportParser` abstraction, splitting parsing from orchestration, and renaming the format-specific `CsvImportProfile` entity to the format-agnostic `ImportProfile`.

**Architecture:** A new `IImportParser` interface with `CsvImportParser` and `ExcelImportParser` implementations is introduced. An `ImportParserFactory` routes to the correct parser based on an `ImportFormat` enum stored on `ImportProfile`. `ImportService` becomes format-blind — it calls the factory, gets back `ParsedImportRow` objects, and runs the existing pipeline unchanged.

**Tech Stack:** .NET 10, ASP.NET Core MVC, Entity Framework Core, PostgreSQL, CsvHelper (existing), ClosedXML (new — MIT licensed), xUnit + FluentAssertions + Moq.

---

## File Map

### New files

| File | Purpose |
|---|---|
| `ProjectCeres/Models/ImportFormat.cs` | `ImportFormat` enum — `Csv`, `Excel` |
| `ProjectCeres/Services/IImportParser.cs` | Parser abstraction — `Format` + `ParseAsync` |
| `ProjectCeres/Services/CsvImportParser.cs` | CSV parsing logic extracted from `ImportService` |
| `ProjectCeres/Services/ExcelImportParser.cs` | XLSX parsing via ClosedXML |
| `ProjectCeres/Services/ImportParserFactory.cs` | Routes `ImportFormat` → `IImportParser` |
| `ProjectCeres.Tests/Integration/ImportProfileServiceTests.cs` | Renamed from `CsvImportProfileServiceTests` |
| `ProjectCeres.Tests/Unit/ImportParserFactoryTests.cs` | Factory routing unit tests |
| `ProjectCeres.Tests/Unit/ExcelImportParserTests.cs` | Excel parser unit tests |
| `ProjectCeres.Tests/Fixtures/valid_import.xlsx` | 10-row single-sheet fixture |
| `ProjectCeres.Tests/Fixtures/multi_sheet.xlsx` | 2-sheet fixture — valid data on first sheet |
| `ProjectCeres.Tests/Fixtures/named_sheet.xlsx` | Single sheet named "Transactions" |
| `docs/decisions/ADR-0059-import-parser-abstraction.md` | New ADR |

### Modified files

| File | Change |
|---|---|
| `ProjectCeres/Models/CsvImportProfile.cs` | Rename class → `ImportProfile`; add `Format`, `SheetName` |
| `ProjectCeres/ViewModels/CsvColumnMappings.cs` | Rename class → `ImportColumnMappings`; add `SheetName` |
| `ProjectCeres/ViewModels/CsvImportProfileViewModel.cs` | Rename + update property types |
| `ProjectCeres/ViewModels/CsvImportProfileCreateViewModel.cs` | Rename + update property types |
| `ProjectCeres/ViewModels/CsvImportProfileEditViewModel.cs` | Rename + update property types |
| `ProjectCeres/Services/ICsvImportProfileService.cs` | Rename interface + update signatures |
| `ProjectCeres/Services/CsvImportProfileService.cs` | Rename class + update to use `ImportProfile` |
| `ProjectCeres/Services/IImportService.cs` | Update `ImportAsync` signature |
| `ProjectCeres/Services/ImportService.cs` | Remove parsing logic; delegate to `ImportParserFactory` |
| `ProjectCeres/Controllers/CsvImportProfilesController.cs` | Rename class + update ViewModel references |
| `ProjectCeres/Controllers/Api/ImportApiController.cs` | Update to use `ImportColumnMappings` |
| `ProjectCeres/Data/AppDbContext.cs` | Rename `DbSet`; update `modelBuilder` config |
| `ProjectCeres/Program.cs` | Update DI registrations |
| `ProjectCeres.Tests/Integration/ImportServiceTests.cs` | Add XLSX integration test |
| `ProjectCeres.Tests/Integration/ImportApiTests.cs` | Invert XLSX rejection test → acceptance test |
| `ProjectCeres.Tests/Integration/CsvImportProfileServiceTests.cs` | Delete (replaced by `ImportProfileServiceTests`) |
| `docs/decisions/ADR-0038-import-service-test-approach.md` | Status → Superseded by ADR-0059 |
| `docs/decisions/ADR-0046-ofx-import-deferred.md` | Add note about `IImportParser` making OFX easy later |
| `docs/decisions/ADR-0047-csv-column-mapping-ux.md` | Rename profile/mappings references; add `Format`/`SheetName` |
| `docs/architecture.md` | Add `ImportParserFactory` to "What Lives Where" |
| `docs/testing.md` | Update import test inventory |
| `docs/security-model.md` | Add Import files subsection |
| `docs/roadmap-phase-two.md` | Update Stage 3 + Verification Checklist |

---

## Task 1: Add ClosedXML NuGet Package

**Files:**
- Modify: `ProjectCeres/ProjectCeres.csproj`

- [ ] **Step 1: Add the package**

```bash
cd ProjectCeres && dotnet add package ClosedXML
```

Expected output includes: `PackageReference for package 'ClosedXML'`

- [ ] **Step 2: Verify build passes**

```bash
dotnet build ProjectCeres
```

Expected: `Build succeeded. 0 Warning(s). 0 Error(s).`

- [ ] **Step 3: Commit**

```bash
git add ProjectCeres/ProjectCeres.csproj
git commit -m "feat: add ClosedXML for Excel import support"
```

---

## Task 2: Add `ImportFormat` Enum

**Files:**
- Create: `ProjectCeres/Models/ImportFormat.cs`

- [ ] **Step 1: Create the enum**

```csharp
// ProjectCeres/Models/ImportFormat.cs
namespace ProjectCeres.Models;

public enum ImportFormat
{
    Csv,
    Excel
}
```

- [ ] **Step 2: Build to confirm no errors**

```bash
dotnet build ProjectCeres
```

Expected: `Build succeeded.`

- [ ] **Step 3: Commit**

```bash
git add ProjectCeres/Models/ImportFormat.cs
git commit -m "feat: add ImportFormat enum (Csv, Excel)"
```

---

## Task 3: Rename `CsvColumnMappings` → `ImportColumnMappings` + Add `SheetName`

The type name `CsvColumnMappings` is format-specific. It becomes `ImportColumnMappings`. A `SheetName` property is added for Excel sheet selection.

**Files:**
- Modify: `ProjectCeres/ViewModels/CsvColumnMappings.cs`

- [ ] **Step 1: Update the file — rename class and add `SheetName`**

Replace the entire file contents:

```csharp
// ProjectCeres/ViewModels/CsvColumnMappings.cs
namespace ProjectCeres.ViewModels;

public class ImportColumnMappings
{
    public string DateColumn { get; set; } = string.Empty;
    public string AmountColumn { get; set; } = string.Empty;
    public string DescriptionColumn { get; set; } = string.Empty;
    public string? CategoryColumn { get; set; }
    /// <summary>When true, negate debit values to positive amounts on import.</summary>
    public bool FlipDebitSign { get; set; }
    /// <summary>Excel only. If set, read this worksheet by name. If null, read the first worksheet.</summary>
    public string? SheetName { get; set; }
}
```

- [ ] **Step 2: Build — expect compilation errors on all `CsvColumnMappings` references**

```bash
dotnet build ProjectCeres
```

Expected: Multiple errors like `The type or namespace name 'CsvColumnMappings' could not be found`. This is expected — you will fix them in the tasks that follow.

- [ ] **Step 3: Commit the rename**

```bash
git add ProjectCeres/ViewModels/CsvColumnMappings.cs
git commit -m "feat: rename CsvColumnMappings to ImportColumnMappings, add SheetName"
```

---

## Task 4: Rename `CsvImportProfile` Model + Add `Format` and `SheetName`

**Files:**
- Modify: `ProjectCeres/Models/CsvImportProfile.cs`

- [ ] **Step 1: Update the model**

Replace the entire file contents:

```csharp
// ProjectCeres/Models/CsvImportProfile.cs
using ProjectCeres.Models;

namespace ProjectCeres.Models;

public class ImportProfile
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string ColumnMappings { get; set; } = "{}";
    public ImportFormat Format { get; set; } = ImportFormat.Csv;
    public string? SheetName { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? DeletedAt { get; set; }
}
```

- [ ] **Step 2: Commit**

```bash
git add ProjectCeres/Models/CsvImportProfile.cs
git commit -m "feat: rename CsvImportProfile to ImportProfile, add Format and SheetName"
```

---

## Task 5: Update ViewModels to Use `ImportColumnMappings` and `ImportProfile`

**Files:**
- Modify: `ProjectCeres/ViewModels/CsvImportProfileViewModel.cs`
- Modify: `ProjectCeres/ViewModels/CsvImportProfileCreateViewModel.cs`
- Modify: `ProjectCeres/ViewModels/CsvImportProfileEditViewModel.cs`

- [ ] **Step 1: Update `CsvImportProfileViewModel.cs`**

Replace entire file:

```csharp
// ProjectCeres/ViewModels/CsvImportProfileViewModel.cs
using ProjectCeres.Models;

namespace ProjectCeres.ViewModels;

public class ImportProfileViewModel
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public ImportColumnMappings Mappings { get; set; } = new();
    public ImportFormat Format { get; set; }
    public string? SheetName { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? DeletedAt { get; set; }

    public int DaysUntilPurge =>
        DeletedAt.HasValue
            ? Math.Max(0, 90 - (int)(DateTime.UtcNow - DeletedAt.Value).TotalDays)
            : 0;
}
```

- [ ] **Step 2: Update `CsvImportProfileCreateViewModel.cs`**

Replace entire file:

```csharp
// ProjectCeres/ViewModels/CsvImportProfileCreateViewModel.cs
using System.ComponentModel.DataAnnotations;
using ProjectCeres.Models;

namespace ProjectCeres.ViewModels;

public class ImportProfileCreateViewModel
{
    [Required(ErrorMessage = "Name is required.")]
    [StringLength(100)]
    public string Name { get; set; } = string.Empty;

    public ImportFormat Format { get; set; } = ImportFormat.Csv;
    public ImportColumnMappings Mappings { get; set; } = new();
}
```

- [ ] **Step 3: Update `CsvImportProfileEditViewModel.cs`**

Replace entire file:

```csharp
// ProjectCeres/ViewModels/CsvImportProfileEditViewModel.cs
using System.ComponentModel.DataAnnotations;
using ProjectCeres.Models;

namespace ProjectCeres.ViewModels;

public class ImportProfileEditViewModel
{
    public Guid Id { get; set; }

    [Required(ErrorMessage = "Name is required.")]
    [StringLength(100)]
    public string Name { get; set; } = string.Empty;

    public ImportFormat Format { get; set; }
    public ImportColumnMappings Mappings { get; set; } = new();
}
```

- [ ] **Step 4: Commit**

```bash
git add ProjectCeres/ViewModels/CsvImportProfileViewModel.cs \
        ProjectCeres/ViewModels/CsvImportProfileCreateViewModel.cs \
        ProjectCeres/ViewModels/CsvImportProfileEditViewModel.cs
git commit -m "feat: rename import profile ViewModels, add Format field"
```

---

## Task 6: Rename `ICsvImportProfileService` + `CsvImportProfileService`

**Files:**
- Modify: `ProjectCeres/Services/ICsvImportProfileService.cs`
- Modify: `ProjectCeres/Services/CsvImportProfileService.cs`

- [ ] **Step 1: Update the interface**

Replace entire file:

```csharp
// ProjectCeres/Services/ICsvImportProfileService.cs
using ProjectCeres.Models;
using ProjectCeres.ViewModels;

namespace ProjectCeres.Services;

public interface IImportProfileService
{
    Task<IEnumerable<ImportProfileViewModel>> GetAllActiveAsync();
    Task<IEnumerable<ImportProfileViewModel>> GetRecentlyDeletedAsync();
    Task<ImportProfileViewModel?> GetByIdAsync(Guid id);
    Task<Guid> CreateAsync(string name, ImportFormat format, ImportColumnMappings mappings);
    Task UpdateAsync(Guid id, string name, ImportColumnMappings mappings);
    Task DeleteAsync(Guid id);
    Task RecoverAsync(Guid id);
}
```

- [ ] **Step 2: Update the service implementation**

Replace entire file:

```csharp
// ProjectCeres/Services/CsvImportProfileService.cs
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using ProjectCeres.Data;
using ProjectCeres.Models;
using ProjectCeres.ViewModels;

namespace ProjectCeres.Services;

public class ImportProfileService(AppDbContext db) : IImportProfileService
{
    private static readonly JsonSerializerOptions JsonOpts =
        new() { PropertyNameCaseInsensitive = true };

    public async Task<IEnumerable<ImportProfileViewModel>> GetAllActiveAsync()
    {
        var profiles = await db.ImportProfiles
            .Where(p => p.DeletedAt == null)
            .OrderBy(p => p.Name)
            .ToListAsync();

        return profiles.Select(ToViewModel);
    }

    public async Task<IEnumerable<ImportProfileViewModel>> GetRecentlyDeletedAsync()
    {
        var cutoff = DateTime.UtcNow.AddDays(-90);

        var profiles = await db.ImportProfiles
            .Where(p => p.DeletedAt != null && p.DeletedAt > cutoff)
            .OrderByDescending(p => p.DeletedAt)
            .ToListAsync();

        return profiles.Select(ToViewModel);
    }

    public async Task<ImportProfileViewModel?> GetByIdAsync(Guid id)
    {
        var profile = await db.ImportProfiles.FindAsync(id);
        return profile is null ? null : ToViewModel(profile);
    }

    public async Task<Guid> CreateAsync(string name, ImportFormat format, ImportColumnMappings mappings)
    {
        var profile = new ImportProfile
        {
            Id             = Guid.NewGuid(),
            Name           = name,
            Format         = format,
            SheetName      = mappings.SheetName,
            ColumnMappings = JsonSerializer.Serialize(mappings),
            CreatedAt      = DateTime.UtcNow
        };

        db.ImportProfiles.Add(profile);
        await db.SaveChangesAsync();
        return profile.Id;
    }

    public async Task UpdateAsync(Guid id, string name, ImportColumnMappings mappings)
    {
        var profile = await db.ImportProfiles.FindAsync(id)
            ?? throw new InvalidOperationException($"Import profile {id} not found.");

        profile.Name           = name;
        profile.SheetName      = mappings.SheetName;
        profile.ColumnMappings = JsonSerializer.Serialize(mappings);
        await db.SaveChangesAsync();
    }

    public async Task DeleteAsync(Guid id)
    {
        var profile = await db.ImportProfiles.FindAsync(id)
            ?? throw new InvalidOperationException($"Import profile {id} not found.");

        profile.DeletedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
    }

    public async Task RecoverAsync(Guid id)
    {
        var profile = await db.ImportProfiles.FindAsync(id)
            ?? throw new InvalidOperationException($"Import profile {id} not found.");

        profile.DeletedAt = null;
        await db.SaveChangesAsync();
    }

    private static ImportProfileViewModel ToViewModel(ImportProfile p) => new()
    {
        Id        = p.Id,
        Name      = p.Name,
        Format    = p.Format,
        SheetName = p.SheetName,
        Mappings  = JsonSerializer.Deserialize<ImportColumnMappings>(p.ColumnMappings, JsonOpts) ?? new(),
        CreatedAt = p.CreatedAt,
        DeletedAt = p.DeletedAt
    };
}
```

- [ ] **Step 3: Commit**

```bash
git add ProjectCeres/Services/ICsvImportProfileService.cs \
        ProjectCeres/Services/CsvImportProfileService.cs
git commit -m "feat: rename ICsvImportProfileService/CsvImportProfileService to IImportProfileService/ImportProfileService"
```

---

## Task 7: Update `AppDbContext` — Rename DbSet + Configure New Columns

**Files:**
- Modify: `ProjectCeres/Data/AppDbContext.cs`

- [ ] **Step 1: Find the `CsvImportProfiles` DbSet line (line 27) and replace**

Old:
```csharp
public DbSet<CsvImportProfile> CsvImportProfiles => Set<CsvImportProfile>();
```

New:
```csharp
public DbSet<ImportProfile> ImportProfiles => Set<ImportProfile>();
```

- [ ] **Step 2: Find the `modelBuilder.Entity("ProjectCeres.Models.CsvImportProfile"` block (around line 471) and update it**

The existing block looks like:
```csharp
modelBuilder.Entity<CsvImportProfile>(entity =>
{
    entity.Property(p => p.ColumnMappings)
        .HasColumnType("jsonb");
});
```

Replace with:
```csharp
modelBuilder.Entity<ImportProfile>(entity =>
{
    entity.ToTable("ImportProfiles");
    entity.Property(p => p.ColumnMappings).HasColumnType("jsonb");
    entity.Property(p => p.Format)
        .HasConversion<string>()
        .HasMaxLength(20)
        .HasDefaultValue(ImportFormat.Csv);
});
```

- [ ] **Step 3: Add the using for `ProjectCeres.Models` if not already present at top of file**

Check the top of `AppDbContext.cs`. If `using ProjectCeres.Models;` is not there, add it.

- [ ] **Step 4: Build**

```bash
dotnet build ProjectCeres
```

Expected: errors only about remaining `CsvImportProfile` references in the controller and DI — not in `AppDbContext.cs` itself.

- [ ] **Step 5: Commit**

```bash
git add ProjectCeres/Data/AppDbContext.cs
git commit -m "feat: rename CsvImportProfiles DbSet to ImportProfiles, configure Format column"
```

---

## Task 8: Update Controller + Program.cs DI

**Files:**
- Modify: `ProjectCeres/Controllers/CsvImportProfilesController.cs`
- Modify: `ProjectCeres/Controllers/Api/ImportApiController.cs`
- Modify: `ProjectCeres/Program.cs`

- [ ] **Step 1: Update `CsvImportProfilesController.cs`**

Replace entire file:

```csharp
// ProjectCeres/Controllers/CsvImportProfilesController.cs
using Microsoft.AspNetCore.Mvc;
using ProjectCeres.Models;
using ProjectCeres.Services;
using ProjectCeres.ViewModels;

namespace ProjectCeres.Controllers;

public class ImportProfilesController(IImportProfileService profileService) : Controller
{
    public async Task<IActionResult> Index()
    {
        var active  = await profileService.GetAllActiveAsync();
        var deleted = await profileService.GetRecentlyDeletedAsync();
        ViewBag.DeletedProfiles = deleted;
        return View(active);
    }

    public IActionResult Create() => View(new ImportProfileCreateViewModel());

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(ImportProfileCreateViewModel vm)
    {
        if (!ModelState.IsValid) return View(vm);

        try
        {
            await profileService.CreateAsync(vm.Name, vm.Format, vm.Mappings);
            TempData["SuccessMessage"] = $"Import profile \"{vm.Name}\" created.";
            return RedirectToAction(nameof(Index));
        }
        catch (InvalidOperationException ex)
        {
            ModelState.AddModelError(string.Empty, ex.Message);
            return View(vm);
        }
    }

    public async Task<IActionResult> Edit(Guid id)
    {
        var profile = await profileService.GetByIdAsync(id);
        if (profile is null) return NotFound();

        var vm = new ImportProfileEditViewModel
        {
            Id       = profile.Id,
            Name     = profile.Name,
            Format   = profile.Format,
            Mappings = profile.Mappings
        };
        return View(vm);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(ImportProfileEditViewModel vm)
    {
        if (!ModelState.IsValid) return View(vm);

        try
        {
            await profileService.UpdateAsync(vm.Id, vm.Name, vm.Mappings);
            TempData["SuccessMessage"] = $"Import profile \"{vm.Name}\" updated.";
            return RedirectToAction(nameof(Index));
        }
        catch (InvalidOperationException ex)
        {
            ModelState.AddModelError(string.Empty, ex.Message);
            return View(vm);
        }
    }

    public async Task<IActionResult> Delete(Guid id)
    {
        var profile = await profileService.GetByIdAsync(id);
        if (profile is null) return NotFound();
        return View(profile);
    }

    [HttpPost, ActionName("Delete")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteConfirmed(Guid id)
    {
        try
        {
            var profile = await profileService.GetByIdAsync(id);
            if (profile is null) return NotFound();
            await profileService.DeleteAsync(id);
            TempData["SuccessMessage"] = $"Import profile \"{profile.Name}\" deleted. Recoverable for 90 days.";
        }
        catch (InvalidOperationException ex)
        {
            TempData["ErrorMessage"] = ex.Message;
        }

        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Recover(Guid id)
    {
        try
        {
            var profile = await profileService.GetByIdAsync(id);
            if (profile is null) return NotFound();
            await profileService.RecoverAsync(id);
            TempData["SuccessMessage"] = $"Import profile \"{profile.Name}\" restored.";
        }
        catch (InvalidOperationException ex)
        {
            TempData["ErrorMessage"] = ex.Message;
        }

        return RedirectToAction(nameof(Index));
    }
}
```

- [ ] **Step 2: Update `ImportApiController.cs`**

Replace entire file:

```csharp
// ProjectCeres/Controllers/Api/ImportApiController.cs
using Microsoft.AspNetCore.Mvc;
using ProjectCeres.Services;
using ProjectCeres.ViewModels;

namespace ProjectCeres.Controllers.Api;

[ApiController]
[Route("api/import")]
public class ImportApiController(IImportService importService) : ControllerBase
{
    [HttpPost]
    [Consumes("multipart/form-data")]
    public async Task<IActionResult> Import([FromForm] ImportRequestViewModel vm)
    {
        if (!ModelState.IsValid)
            return ValidationProblem(ModelState);

        var mappings = new ImportColumnMappings
        {
            DateColumn        = vm.DateColumn!,
            AmountColumn      = vm.AmountColumn!,
            DescriptionColumn = vm.DescriptionColumn!,
            CategoryColumn    = vm.CategoryColumn,
            FlipDebitSign     = vm.FlipDebitSign
        };

        try
        {
            var result = await importService.ImportAsync(
                vm.File!, vm.AccountId!.Value, vm.CategoryId!.Value, mappings);

            return Ok(result);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }
}
```

- [ ] **Step 3: Update `Program.cs` DI registrations**

Find these two lines (around line 68–69):
```csharp
builder.Services.AddScoped<ICsvImportProfileService, CsvImportProfileService>();
builder.Services.AddScoped<IImportService, ImportService>();
```

Replace with:
```csharp
builder.Services.AddScoped<IImportProfileService, ImportProfileService>();
builder.Services.AddScoped<IImportService, ImportService>();
```

- [ ] **Step 4: Build — should have only migration-related issues, not type errors**

```bash
dotnet build ProjectCeres
```

Expected: `Build succeeded.` (All type references are now consistent.)

- [ ] **Step 5: Commit**

```bash
git add ProjectCeres/Controllers/CsvImportProfilesController.cs \
        ProjectCeres/Controllers/Api/ImportApiController.cs \
        ProjectCeres/Program.cs
git commit -m "feat: update controller and DI to use ImportProfileService and ImportColumnMappings"
```

---

## Task 9: EF Core Migration — Rename Table + Add `Format` and `SheetName`

**Files:**
- Create: new migration file (auto-generated)
- Modify: `ProjectCeres/Migrations/AppDbContextModelSnapshot.cs` (auto-generated)

- [ ] **Step 1: Create the migration**

```bash
cd ProjectCeres && dotnet ef migrations add RenameToImportProfile
```

- [ ] **Step 2: Open the generated migration file and inspect it**

The file will be at `ProjectCeres/Migrations/<timestamp>_RenameToImportProfile.cs`.

The `Up()` method must contain:
- `RenameTable(name: "CsvImportProfiles", newName: "ImportProfiles")`
- `AddColumn` for `Format` (varchar, not null, default `'Csv'`)
- `AddColumn` for `SheetName` (varchar, nullable)

If you see `DropTable` + `CreateTable` instead of `RenameTable`, stop. This means EF did not detect the rename — it saw a deletion and a new creation. Fix it by manually editing the `Up()` and `Down()` methods to use `RenameTable` / `RenameTable` (reverse) instead. EF does not auto-detect renames; it must be hand-edited.

Expected `Up()` after any needed correction:
```csharp
protected override void Up(MigrationBuilder migrationBuilder)
{
    migrationBuilder.RenameTable(
        name: "CsvImportProfiles",
        newName: "ImportProfiles");

    migrationBuilder.AddColumn<string>(
        name: "Format",
        table: "ImportProfiles",
        type: "varchar(20)",
        nullable: false,
        defaultValue: "Csv");

    migrationBuilder.AddColumn<string>(
        name: "SheetName",
        table: "ImportProfiles",
        type: "character varying(100)",
        maxLength: 100,
        nullable: true);
}

protected override void Down(MigrationBuilder migrationBuilder)
{
    migrationBuilder.DropColumn(name: "SheetName", table: "ImportProfiles");
    migrationBuilder.DropColumn(name: "Format",    table: "ImportProfiles");

    migrationBuilder.RenameTable(
        name: "ImportProfiles",
        newName: "CsvImportProfiles");
}
```

- [ ] **Step 3: Apply the migration**

```bash
dotnet ef database update
```

Expected: `Done.`

- [ ] **Step 4: Run existing tests — all must pass**

```bash
cd .. && dotnet test ProjectCeres.Tests
```

Expected: 0 failed. If any test fails, stop and investigate before continuing.

- [ ] **Step 5: Commit**

```bash
git add ProjectCeres/Migrations/
git commit -m "feat: migrate CsvImportProfiles → ImportProfiles, add Format and SheetName columns"
```

---

## Task 10: Create `IImportParser` Interface + `CsvImportParser`

**Files:**
- Create: `ProjectCeres/Services/IImportParser.cs`
- Create: `ProjectCeres/Services/CsvImportParser.cs`

- [ ] **Step 1: Write the failing unit test first**

Create `ProjectCeres.Tests/Unit/CsvImportParserTests.cs`:

```csharp
using System.Globalization;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Moq;
using ProjectCeres.Models;
using ProjectCeres.Services;
using ProjectCeres.ViewModels;

namespace ProjectCeres.Tests.Unit;

public class CsvImportParserTests
{
    private static IFormFile CsvFormFile(string content, string fileName = "test.csv")
    {
        var bytes  = System.Text.Encoding.UTF8.GetBytes(content);
        var stream = new MemoryStream(bytes);
        var file   = new Mock<IFormFile>();
        file.Setup(f => f.FileName).Returns(fileName);
        file.Setup(f => f.Length).Returns(stream.Length);
        file.Setup(f => f.CopyToAsync(It.IsAny<Stream>(), It.IsAny<CancellationToken>()))
            .Returns<Stream, CancellationToken>((dest, ct) =>
            {
                stream.Position = 0;
                return stream.CopyToAsync(dest, ct);
            });
        return file.Object;
    }

    [Fact]
    public async Task ParseAsync_ValidCsv_ReturnsParsedRows()
    {
        var csv = "Date,Amount,Description\n2024-01-01,100.00,Rent\n2024-01-02,50.00,Groceries";
        var file = CsvFormFile(csv);
        var mappings = new ImportColumnMappings
        {
            DateColumn        = "Date",
            AmountColumn      = "Amount",
            DescriptionColumn = "Description"
        };

        var parser = new CsvImportParser();
        var rows   = await parser.ParseAsync(file, mappings);

        rows.Should().HaveCount(2);
        rows[0].Date.Should().Be(new DateOnly(2024, 1, 1));
        rows[0].Amount.Should().Be(100.00m);
        rows[0].Description.Should().Be("Rent");
    }

    [Fact]
    public async Task ParseAsync_NegativeDebit_FlippedToPositive()
    {
        var csv  = "Date,Amount,Description\n2024-01-01,-75.00,Electricity";
        var file = CsvFormFile(csv);
        var mappings = new ImportColumnMappings
        {
            DateColumn        = "Date",
            AmountColumn      = "Amount",
            DescriptionColumn = "Description",
            FlipDebitSign     = true
        };

        var parser = new CsvImportParser();
        var rows   = await parser.ParseAsync(file, mappings);

        rows[0].Amount.Should().Be(75.00m);
    }

    [Fact]
    public void Format_IsCsv()
    {
        new CsvImportParser().Format.Should().Be(ImportFormat.Csv);
    }
}
```

- [ ] **Step 2: Run — confirm it fails**

```bash
dotnet test ProjectCeres.Tests --filter "CsvImportParserTests"
```

Expected: FAIL — `CsvImportParser` does not exist yet.

- [ ] **Step 3: Create `IImportParser`**

```csharp
// ProjectCeres/Services/IImportParser.cs
using Microsoft.AspNetCore.Http;
using ProjectCeres.Models;
using ProjectCeres.ViewModels;

namespace ProjectCeres.Services;

public interface IImportParser
{
    ImportFormat Format { get; }
    Task<IReadOnlyList<ParsedImportRow>> ParseAsync(IFormFile file, ImportColumnMappings mappings);
}
```

- [ ] **Step 4: Create `CsvImportParser` — extract parsing logic from `ImportService`**

```csharp
// ProjectCeres/Services/CsvImportParser.cs
using System.Globalization;
using CsvHelper;
using CsvHelper.Configuration;
using Microsoft.AspNetCore.Http;
using ProjectCeres.Models;
using ProjectCeres.ViewModels;

namespace ProjectCeres.Services;

public class CsvImportParser : IImportParser
{
    public ImportFormat Format => ImportFormat.Csv;

    public async Task<IReadOnlyList<ParsedImportRow>> ParseAsync(IFormFile file, ImportColumnMappings mappings)
    {
        var rows = new List<ParsedImportRow>();

        using var memStream = new MemoryStream();
        await file.CopyToAsync(memStream);
        memStream.Position = 0;

        using var reader = new StreamReader(memStream);
        using var csv    = new CsvReader(reader, new CsvConfiguration(CultureInfo.InvariantCulture)
        {
            MissingFieldFound = null,
            HeaderValidated   = null
        });

        await csv.ReadAsync();
        csv.ReadHeader();

        while (await csv.ReadAsync())
        {
            var dateStr   = csv.GetField(mappings.DateColumn) ?? string.Empty;
            var amountStr = csv.GetField(mappings.AmountColumn) ?? string.Empty;
            var desc      = csv.GetField(mappings.DescriptionColumn);
            var category  = mappings.CategoryColumn is not null
                ? csv.GetField(mappings.CategoryColumn)
                : null;

            if (!DateOnly.TryParseExact(dateStr, ["yyyy-MM-dd", "dd/MM/yyyy", "MM/dd/yyyy"], null,
                    DateTimeStyles.None, out var date))
                continue;

            if (!decimal.TryParse(amountStr, NumberStyles.Any, CultureInfo.InvariantCulture, out var amount))
                continue;

            if (mappings.FlipDebitSign && amount < 0)
                amount = -amount;

            rows.Add(new ParsedImportRow
            {
                Date         = date,
                Amount       = amount,
                Description  = desc,
                CategoryName = category
            });
        }

        return rows;
    }
}
```

- [ ] **Step 5: Run tests — confirm they pass**

```bash
dotnet test ProjectCeres.Tests --filter "CsvImportParserTests"
```

Expected: PASS.

- [ ] **Step 6: Commit**

```bash
git add ProjectCeres/Services/IImportParser.cs \
        ProjectCeres/Services/CsvImportParser.cs \
        ProjectCeres.Tests/Unit/CsvImportParserTests.cs
git commit -m "feat: add IImportParser interface and CsvImportParser (extracted from ImportService)"
```

---

## Task 11: Create XLSX Fixture Files

The tests for `ExcelImportParser` need real `.xlsx` files. Create them using a small C# script run once, or create them manually with Excel/LibreOffice. The simplest approach is a one-shot script.

**Files:**
- Create: `ProjectCeres.Tests/Fixtures/valid_import.xlsx`
- Create: `ProjectCeres.Tests/Fixtures/multi_sheet.xlsx`
- Create: `ProjectCeres.Tests/Fixtures/named_sheet.xlsx`

- [ ] **Step 1: Create a temporary fixture generator script**

Create `tools/CreateXlsxFixtures.cs` (you will delete this after running it):

```csharp
// tools/CreateXlsxFixtures.cs
// Run with: dotnet script tools/CreateXlsxFixtures.cs
// Or: add temporarily to a console project and run dotnet run

using ClosedXML.Excel;

var fixturesDir = Path.Combine("..", "ProjectCeres.Tests", "Fixtures");
Directory.CreateDirectory(fixturesDir);

// --- valid_import.xlsx --- 10 rows, single sheet
{
    using var wb = new XLWorkbook();
    var ws = wb.AddWorksheet("Sheet1");
    ws.Cell(1, 1).Value = "Date";
    ws.Cell(1, 2).Value = "Amount";
    ws.Cell(1, 3).Value = "Description";
    ws.Cell(1, 4).Value = "Category";

    var rows = new[]
    {
        ("2024-01-01", "120.00", "Rent",          "Housing"),
        ("2024-01-02", "45.50",  "Groceries",     "Food"),
        ("2024-01-03", "30.00",  "Electric bill", "Utilities"),
        ("2024-01-04", "15.00",  "Coffee shop",   "Food"),
        ("2024-01-05", "200.00", "Freelance",     "Income"),
        ("2024-01-06", "80.00",  "Phone bill",    "Utilities"),
        ("2024-01-07", "25.00",  "Books",         "Education"),
        ("2024-01-08", "60.00",  "Dinner out",    "Food"),
        ("2024-01-09", "10.00",  "Parking",       "Transport"),
        ("2024-01-10", "500.00", "Client payment","Income"),
    };

    for (int i = 0; i < rows.Length; i++)
    {
        ws.Cell(i + 2, 1).Value = rows[i].Item1;
        ws.Cell(i + 2, 2).Value = rows[i].Item2;
        ws.Cell(i + 2, 3).Value = rows[i].Item3;
        ws.Cell(i + 2, 4).Value = rows[i].Item4;
    }
    wb.SaveAs(Path.Combine(fixturesDir, "valid_import.xlsx"));
    Console.WriteLine("Created valid_import.xlsx");
}

// --- multi_sheet.xlsx --- 2 sheets: valid data on first, garbage on second
{
    using var wb = new XLWorkbook();
    var ws1 = wb.AddWorksheet("Sheet1");
    ws1.Cell(1, 1).Value = "Date";
    ws1.Cell(1, 2).Value = "Amount";
    ws1.Cell(1, 3).Value = "Description";
    ws1.Cell(2, 1).Value = "2024-02-01";
    ws1.Cell(2, 2).Value = "99.00";
    ws1.Cell(2, 3).Value = "First sheet row";

    var ws2 = wb.AddWorksheet("Sheet2");
    ws2.Cell(1, 1).Value = "garbage";
    ws2.Cell(1, 2).Value = "not parseable";

    wb.SaveAs(Path.Combine(fixturesDir, "multi_sheet.xlsx"));
    Console.WriteLine("Created multi_sheet.xlsx");
}

// --- named_sheet.xlsx --- single sheet named "Transactions"
{
    using var wb = new XLWorkbook();
    var ws = wb.AddWorksheet("Transactions");
    ws.Cell(1, 1).Value = "Date";
    ws.Cell(1, 2).Value = "Amount";
    ws.Cell(1, 3).Value = "Description";
    ws.Cell(2, 1).Value = "2024-03-01";
    ws.Cell(2, 2).Value = "77.00";
    ws.Cell(2, 3).Value = "Named sheet row";

    wb.SaveAs(Path.Combine(fixturesDir, "named_sheet.xlsx"));
    Console.WriteLine("Created named_sheet.xlsx");
}

Console.WriteLine("All fixtures created.");
```

- [ ] **Step 2: Run the script**

The easiest way is to add a temporary console project, paste the script, add ClosedXML to it, and run it. Or run it as a dotnet-script if you have that tool. Either way, confirm the three `.xlsx` files exist in `ProjectCeres.Tests/Fixtures/`:

```bash
ls ProjectCeres.Tests/Fixtures/*.xlsx
```

Expected:
```
ProjectCeres.Tests/Fixtures/multi_sheet.xlsx
ProjectCeres.Tests/Fixtures/named_sheet.xlsx
ProjectCeres.Tests/Fixtures/valid_import.xlsx
```

- [ ] **Step 3: Verify the `xlsx_attempt.xlsx` fixture (already exists) is still present**

```bash
ls ProjectCeres.Tests/Fixtures/xlsx_attempt.xlsx
```

Expected: file exists. This fixture was previously used to test XLSX rejection — it will be repurposed in Task 14.

- [ ] **Step 4: Add fixtures to git and commit**

```bash
git add ProjectCeres.Tests/Fixtures/valid_import.xlsx \
        ProjectCeres.Tests/Fixtures/multi_sheet.xlsx \
        ProjectCeres.Tests/Fixtures/named_sheet.xlsx
git commit -m "test: add XLSX fixture files for ExcelImportParser tests"
```

---

## Task 12: Create `ExcelImportParser`

**Files:**
- Create: `ProjectCeres/Services/ExcelImportParser.cs`
- Create: `ProjectCeres.Tests/Unit/ExcelImportParserTests.cs`

- [ ] **Step 1: Write the failing tests**

Create `ProjectCeres.Tests/Unit/ExcelImportParserTests.cs`:

```csharp
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Moq;
using ProjectCeres.Models;
using ProjectCeres.Services;
using ProjectCeres.ViewModels;

namespace ProjectCeres.Tests.Unit;

public class ExcelImportParserTests
{
    private static readonly string FixturesDir =
        Path.Combine(AppContext.BaseDirectory, "Fixtures");

    private static IFormFile XlsxFormFile(string fileName)
    {
        var path   = Path.Combine(FixturesDir, fileName);
        var bytes  = File.ReadAllBytes(path);
        var stream = new MemoryStream(bytes);
        var file   = new Mock<IFormFile>();
        file.Setup(f => f.FileName).Returns(fileName);
        file.Setup(f => f.Length).Returns(stream.Length);
        file.Setup(f => f.CopyToAsync(It.IsAny<Stream>(), It.IsAny<CancellationToken>()))
            .Returns<Stream, CancellationToken>((dest, ct) =>
            {
                stream.Position = 0;
                return stream.CopyToAsync(dest, ct);
            });
        return file.Object;
    }

    private static ImportColumnMappings StandardMappings(string? sheetName = null) => new()
    {
        DateColumn        = "Date",
        AmountColumn      = "Amount",
        DescriptionColumn = "Description",
        SheetName         = sheetName
    };

    [Fact]
    public async Task ParseAsync_ValidXlsx_Returns10Rows()
    {
        var file   = XlsxFormFile("valid_import.xlsx");
        var parser = new ExcelImportParser();

        var rows = await parser.ParseAsync(file, StandardMappings());

        rows.Should().HaveCount(10);
        rows[0].Date.Should().Be(new DateOnly(2024, 1, 1));
        rows[0].Amount.Should().Be(120.00m);
        rows[0].Description.Should().Be("Rent");
    }

    [Fact]
    public async Task ParseAsync_MultiSheet_ReadsFirstSheetByDefault()
    {
        var file   = XlsxFormFile("multi_sheet.xlsx");
        var parser = new ExcelImportParser();

        var rows = await parser.ParseAsync(file, StandardMappings());

        rows.Should().HaveCount(1);
        rows[0].Description.Should().Be("First sheet row");
    }

    [Fact]
    public async Task ParseAsync_NamedSheet_ReadsCorrectSheet()
    {
        var file   = XlsxFormFile("named_sheet.xlsx");
        var parser = new ExcelImportParser();

        var rows = await parser.ParseAsync(file, StandardMappings(sheetName: "Transactions"));

        rows.Should().HaveCount(1);
        rows[0].Description.Should().Be("Named sheet row");
        rows[0].Amount.Should().Be(77.00m);
    }

    [Fact]
    public async Task ParseAsync_SpoofedFile_ThrowsInvalidOperationException()
    {
        // xlsx_attempt.xlsx is not a valid XLSX — it is a spoofed file used in previous tests
        // to verify rejection. Here we verify magic bytes check rejects it.
        // If xlsx_attempt.xlsx happens to be a valid XLSX, replace this test with a
        // hand-crafted byte array that starts with non-PK bytes.
        var spoofedBytes = new byte[] { 0xFF, 0xFE, 0x00, 0x01 }; // not PK magic bytes
        var stream       = new MemoryStream(spoofedBytes);
        var file         = new Mock<IFormFile>();
        file.Setup(f => f.FileName).Returns("fake.xlsx");
        file.Setup(f => f.Length).Returns(spoofedBytes.Length);
        file.Setup(f => f.CopyToAsync(It.IsAny<Stream>(), It.IsAny<CancellationToken>()))
            .Returns<Stream, CancellationToken>((dest, ct) =>
            {
                stream.Position = 0;
                return stream.CopyToAsync(dest, ct);
            });

        var parser = new ExcelImportParser();
        var act    = async () => await parser.ParseAsync(file.Object, StandardMappings());

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*not a valid Excel file*");
    }

    [Fact]
    public void Format_IsExcel()
    {
        new ExcelImportParser().Format.Should().Be(ImportFormat.Excel);
    }
}
```

- [ ] **Step 2: Run — confirm all fail**

```bash
dotnet test ProjectCeres.Tests --filter "ExcelImportParserTests"
```

Expected: FAIL — `ExcelImportParser` does not exist.

- [ ] **Step 3: Implement `ExcelImportParser`**

```csharp
// ProjectCeres/Services/ExcelImportParser.cs
using System.Globalization;
using ClosedXML.Excel;
using Microsoft.AspNetCore.Http;
using ProjectCeres.Models;
using ProjectCeres.ViewModels;

namespace ProjectCeres.Services;

public class ExcelImportParser : IImportParser
{
    // XLSX files are ZIP archives — magic bytes are PK (0x50 0x4B 0x03 0x04)
    private static readonly byte[] XlsxMagicBytes = [0x50, 0x4B, 0x03, 0x04];

    public ImportFormat Format => ImportFormat.Excel;

    public async Task<IReadOnlyList<ParsedImportRow>> ParseAsync(IFormFile file, ImportColumnMappings mappings)
    {
        using var memStream = new MemoryStream();
        await file.CopyToAsync(memStream);
        memStream.Position = 0;

        // Magic bytes check — reject spoofed files before opening with ClosedXML
        var header = new byte[4];
        var read   = await memStream.ReadAsync(header, 0, 4);
        if (read < 4 || !header.SequenceEqual(XlsxMagicBytes))
            throw new InvalidOperationException(
                "The uploaded file does not appear to be a valid Excel file. " +
                "Please re-export from your bank and try again.");

        memStream.Position = 0;

        using var wb = new XLWorkbook(memStream);

        var ws = mappings.SheetName is not null
            ? wb.Worksheet(mappings.SheetName)
            : wb.Worksheets.First();

        // Find header row — row 1
        var headerRow = ws.Row(1);
        var colIndex  = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var cell in headerRow.CellsUsed())
            colIndex[cell.GetString()] = cell.Address.ColumnNumber;

        if (!colIndex.TryGetValue(mappings.DateColumn, out var dateCol))
            throw new InvalidOperationException($"Column '{mappings.DateColumn}' not found in worksheet.");
        if (!colIndex.TryGetValue(mappings.AmountColumn, out var amountCol))
            throw new InvalidOperationException($"Column '{mappings.AmountColumn}' not found in worksheet.");
        if (!colIndex.TryGetValue(mappings.DescriptionColumn, out var descCol))
            throw new InvalidOperationException($"Column '{mappings.DescriptionColumn}' not found in worksheet.");

        int? catCol = null;
        if (mappings.CategoryColumn is not null && colIndex.TryGetValue(mappings.CategoryColumn, out var cc))
            catCol = cc;

        var rows      = new List<ParsedImportRow>();
        var lastRow   = ws.LastRowUsed()?.RowNumber() ?? 1;

        for (int r = 2; r <= lastRow; r++)
        {
            var row = ws.Row(r);

            var dateStr   = row.Cell(dateCol).GetString();
            var amountStr = row.Cell(amountCol).GetString();
            var desc      = row.Cell(descCol).GetString();
            var category  = catCol.HasValue ? row.Cell(catCol.Value).GetString() : null;

            if (string.IsNullOrWhiteSpace(dateStr) && string.IsNullOrWhiteSpace(amountStr))
                continue; // skip empty rows

            if (!DateOnly.TryParseExact(dateStr, ["yyyy-MM-dd", "dd/MM/yyyy", "MM/dd/yyyy"], null,
                    DateTimeStyles.None, out var date))
                continue;

            if (!decimal.TryParse(amountStr, NumberStyles.Any, CultureInfo.InvariantCulture, out var amount))
                continue;

            if (mappings.FlipDebitSign && amount < 0)
                amount = -amount;

            rows.Add(new ParsedImportRow
            {
                Date         = date,
                Amount       = amount,
                Description  = string.IsNullOrWhiteSpace(desc) ? null : desc,
                CategoryName = string.IsNullOrWhiteSpace(category) ? null : category
            });
        }

        return rows;
    }
}
```

- [ ] **Step 4: Run tests — all must pass**

```bash
dotnet test ProjectCeres.Tests --filter "ExcelImportParserTests"
```

Expected: PASS (5 tests).

- [ ] **Step 5: Commit**

```bash
git add ProjectCeres/Services/ExcelImportParser.cs \
        ProjectCeres.Tests/Unit/ExcelImportParserTests.cs
git commit -m "feat: add ExcelImportParser with magic bytes check and sheet selection"
```

---

## Task 13: Create `ImportParserFactory`

**Files:**
- Create: `ProjectCeres/Services/ImportParserFactory.cs`
- Create: `ProjectCeres.Tests/Unit/ImportParserFactoryTests.cs`

- [ ] **Step 1: Write the failing tests**

```csharp
// ProjectCeres.Tests/Unit/ImportParserFactoryTests.cs
using FluentAssertions;
using ProjectCeres.Models;
using ProjectCeres.Services;

namespace ProjectCeres.Tests.Unit;

public class ImportParserFactoryTests
{
    private static ImportParserFactory BuildFactory() =>
        new(new CsvImportParser(), new ExcelImportParser());

    [Fact]
    public void GetParser_CsvFormat_ReturnsCsvImportParser()
    {
        var factory = BuildFactory();
        var parser  = factory.GetParser(ImportFormat.Csv);
        parser.Should().BeOfType<CsvImportParser>();
    }

    [Fact]
    public void GetParser_ExcelFormat_ReturnsExcelImportParser()
    {
        var factory = BuildFactory();
        var parser  = factory.GetParser(ImportFormat.Excel);
        parser.Should().BeOfType<ExcelImportParser>();
    }

    [Fact]
    public void GetParser_UnknownFormat_ThrowsArgumentOutOfRangeException()
    {
        var factory = BuildFactory();
        var act     = () => factory.GetParser((ImportFormat)99);
        act.Should().Throw<ArgumentOutOfRangeException>();
    }
}
```

- [ ] **Step 2: Run — confirm all fail**

```bash
dotnet test ProjectCeres.Tests --filter "ImportParserFactoryTests"
```

Expected: FAIL — `ImportParserFactory` does not exist.

- [ ] **Step 3: Implement `ImportParserFactory`**

```csharp
// ProjectCeres/Services/ImportParserFactory.cs
using ProjectCeres.Models;

namespace ProjectCeres.Services;

public class ImportParserFactory(CsvImportParser csvParser, ExcelImportParser excelParser)
{
    public IImportParser GetParser(ImportFormat format) => format switch
    {
        ImportFormat.Csv   => csvParser,
        ImportFormat.Excel => excelParser,
        _ => throw new ArgumentOutOfRangeException(nameof(format), format, "No parser registered for this format.")
    };
}
```

- [ ] **Step 4: Register in `Program.cs`**

Add these lines after the existing import service registrations (around line 68):

```csharp
builder.Services.AddSingleton<CsvImportParser>();
builder.Services.AddSingleton<ExcelImportParser>();
builder.Services.AddSingleton<ImportParserFactory>();
```

- [ ] **Step 5: Run tests — all must pass**

```bash
dotnet test ProjectCeres.Tests --filter "ImportParserFactoryTests"
```

Expected: PASS (3 tests).

- [ ] **Step 6: Commit**

```bash
git add ProjectCeres/Services/ImportParserFactory.cs \
        ProjectCeres.Tests/Unit/ImportParserFactoryTests.cs \
        ProjectCeres/Program.cs
git commit -m "feat: add ImportParserFactory; register parsers in DI"
```

---

## Task 14: Refactor `ImportService` + `IImportService` to Use `ImportParserFactory`

**Files:**
- Modify: `ProjectCeres/Services/IImportService.cs`
- Modify: `ProjectCeres/Services/ImportService.cs`

- [ ] **Step 1: Update `IImportService`**

Replace entire file:

```csharp
// ProjectCeres/Services/IImportService.cs
using Microsoft.AspNetCore.Http;
using ProjectCeres.ViewModels;

namespace ProjectCeres.Services;

public interface IImportService
{
    Task<IReadOnlyList<ParsedImportRow>> ParseAsync(IFormFile file, ImportColumnMappings mappings);
    string GenerateFingerprint(DateOnly date, decimal amount, string? description, Guid accountId);
    Task<ImportResult> ImportAsync(IFormFile file, Guid accountId, Guid categoryId, ImportColumnMappings mappings);
}
```

- [ ] **Step 2: Update `ImportService`**

Replace entire file:

```csharp
// ProjectCeres/Services/ImportService.cs
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using ProjectCeres.Data;
using ProjectCeres.Models;
using ProjectCeres.ViewModels;

namespace ProjectCeres.Services;

public class ImportService(
    ImportParserFactory parserFactory,
    AppDbContext? db = null,
    ITransactionService? transactionService = null) : IImportService
{
    private static readonly string[] AllowedExtensions = [".csv", ".xlsx"];

    public async Task<IReadOnlyList<ParsedImportRow>> ParseAsync(IFormFile file, ImportColumnMappings mappings)
    {
        var extension = Path.GetExtension(file.FileName).ToLowerInvariant();
        var format    = extension switch
        {
            ".csv"  => ImportFormat.Csv,
            ".xlsx" => ImportFormat.Excel,
            _       => throw new InvalidOperationException(
                           $"Unsupported file format '{extension}'. Please upload a CSV or Excel (.xlsx) file.")
        };

        var parser = parserFactory.GetParser(format);
        return await parser.ParseAsync(file, mappings);
    }

    public string GenerateFingerprint(DateOnly date, decimal amount, string? description, Guid accountId)
    {
        var raw  = $"{date:yyyy-MM-dd}|{amount:F2}|{description ?? ""}|{accountId}";
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(raw));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    public async Task<ImportResult> ImportAsync(
        IFormFile file, Guid accountId, Guid categoryId, ImportColumnMappings mappings)
    {
        if (db is null || transactionService is null)
            throw new InvalidOperationException("ImportService requires db and transactionService for ImportAsync.");

        var rows   = await ParseAsync(file, mappings);
        var result = new ImportResult();

        var existingTxns = await db.Transactions
            .Where(t => t.AccountId == accountId)
            .Select(t => new { t.Date, t.Amount, t.Description })
            .ToListAsync();

        foreach (var row in rows)
        {
            try
            {
                var isDuplicate = existingTxns.Any(e =>
                    e.Amount == row.Amount &&
                    e.Description == row.Description &&
                    Math.Abs((e.Date.ToDateTime(TimeOnly.MinValue) - row.Date.ToDateTime(TimeOnly.MinValue)).TotalDays) <= 1);

                var vm = new TransactionCreateViewModel
                {
                    Date        = row.Date,
                    Amount      = row.Amount,
                    Description = row.Description,
                    AccountId   = accountId,
                    CategoryId  = categoryId
                };

                var txId = await transactionService.CreateAsync(vm);

                if (isDuplicate)
                {
                    await transactionService.MarkNeedsReviewAsync(txId, needsReview: true);
                    result.RowsFlagged++;
                }
                else
                {
                    await transactionService.MarkClearedAsync(txId, cleared: true);
                    result.RowsImported++;
                }
            }
            catch (Exception ex)
            {
                result.RowsFailed++;
                result.Errors.Add($"Row {row.Date} {row.Amount}: {ex.Message}");
            }
        }

        return result;
    }
}
```

- [ ] **Step 3: Build**

```bash
dotnet build ProjectCeres
```

Expected: `Build succeeded.`

- [ ] **Step 4: Run all tests**

```bash
dotnet test ProjectCeres.Tests
```

Expected: 0 failed. The existing `ImportServiceIntegrationTests` must still pass against CSV fixtures.

- [ ] **Step 5: Commit**

```bash
git add ProjectCeres/Services/IImportService.cs \
        ProjectCeres/Services/ImportService.cs
git commit -m "feat: refactor ImportService to delegate parsing to ImportParserFactory"
```

---

## Task 15: Add XLSX Integration Test + Invert XLSX API Test

**Files:**
- Modify: `ProjectCeres.Tests/Integration/ImportServiceTests.cs`
- Modify: `ProjectCeres.Tests/Integration/ImportApiTests.cs`

- [ ] **Step 1: Add XLSX integration test to `ImportServiceIntegrationTests`**

Open `ProjectCeres.Tests/Integration/ImportServiceTests.cs`. Add this test after the existing tests:

```csharp
[Fact]
public async Task ImportAsync_ValidXlsx_Inserts10TransactionsAllCleared()
{
    var file   = FileFromFixture("valid_import.xlsx");
    var result = await _service.ImportAsync(file, _accountId, HousingCategoryId, StandardMappings());

    result.RowsImported.Should().Be(10);
    result.RowsFlagged.Should().Be(0);
    result.RowsFailed.Should().Be(0);

    var dbCount = await _fixture.Db.Transactions
        .CountAsync(t => t.AccountId == _accountId);
    dbCount.Should().Be(10);

    var allCleared = await _fixture.Db.Transactions
        .Where(t => t.AccountId == _accountId)
        .AllAsync(t => t.IsCleared);
    allCleared.Should().BeTrue();
}
```

Note: `StandardMappings()` already returns the correct column names matching the `valid_import.xlsx` fixture (Date, Amount, Description, Category). The `FileFromFixture` helper works for any extension.

- [ ] **Step 2: Update `ImportApiTests` — invert the XLSX rejection test**

Open `ProjectCeres.Tests/Integration/ImportApiTests.cs`. Find `PostImport_XlsxFile_Returns400WithMessage` and replace it with:

```csharp
[Fact]
public async Task PostImport_ValidXlsxFile_Returns200OrValidation()
{
    var xlsxPath = Path.Combine(FixturesDir, "valid_import.xlsx");
    using var xlsxContent = new StreamContent(File.OpenRead(xlsxPath));
    xlsxContent.Headers.ContentType =
        new System.Net.Http.Headers.MediaTypeHeaderValue(
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet");

    using var form = new MultipartFormDataContent();
    form.Add(xlsxContent, "file", "valid_import.xlsx");
    form.Add(new StringContent("Date"),        "dateColumn");
    form.Add(new StringContent("Amount"),      "amountColumn");
    form.Add(new StringContent("Description"), "descriptionColumn");
    // accountId is required — without it we expect 422, not 400
    // This test asserts XLSX is no longer rejected at the format level (no longer 400)
    var response = await _client.PostAsync("/api/import", form);

    response.StatusCode.Should().NotBe(System.Net.HttpStatusCode.BadRequest,
        "XLSX files should no longer be rejected at the format level");
}
```

- [ ] **Step 3: Run the new tests**

```bash
dotnet test ProjectCeres.Tests --filter "ImportAsync_ValidXlsx|PostImport_ValidXlsx"
```

Expected: PASS.

- [ ] **Step 4: Run full test suite**

```bash
dotnet test ProjectCeres.Tests
```

Expected: 0 failed.

- [ ] **Step 5: Rename `CsvImportProfileServiceTests.cs`**

Delete the old file and create the renamed version:

```bash
git rm ProjectCeres.Tests/Integration/CsvImportProfileServiceTests.cs
```

Then create `ProjectCeres.Tests/Integration/ImportProfileServiceTests.cs` with the same contents but:
- Class name: `ImportProfileServiceTests`
- Service type: `ImportProfileService` (instead of `CsvImportProfileService`)
- All `CsvColumnMappings` → `ImportColumnMappings`
- All `CsvImportProfileService` references → `ImportProfileService`
- `ValidMappings()` helper already named correctly — no change needed

The full file:

```csharp
// ProjectCeres.Tests/Integration/ImportProfileServiceTests.cs
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using ProjectCeres.Models;
using ProjectCeres.Services;
using ProjectCeres.ViewModels;

namespace ProjectCeres.Tests.Integration;

[Collection("IntegrationTests")]
public class ImportProfileServiceTests : IAsyncLifetime
{
    private readonly TestDbFixture _fixture = new();
    private ImportProfileService _service = null!;

    public async Task InitializeAsync()
    {
        await _fixture.InitAsync();
        _service = new ImportProfileService(_fixture.Db);
    }

    public async Task DisposeAsync() => await _fixture.DisposeAsync();

    private static ImportColumnMappings ValidMappings() => new()
    {
        DateColumn        = "Date",
        AmountColumn      = "Amount",
        DescriptionColumn = "Description",
        CategoryColumn    = "Category"
    };

    [Fact]
    public async Task CreateProfile_WithValidMappings_SucceedsAndMappingsRetrievable()
    {
        var mappings = ValidMappings();

        var id = await _service.CreateAsync("Bank A", ImportFormat.Csv, mappings);

        var retrieved = await _service.GetByIdAsync(id);
        retrieved.Should().NotBeNull();
        retrieved!.Name.Should().Be("Bank A");
        retrieved.Format.Should().Be(ImportFormat.Csv);
        retrieved.Mappings.DateColumn.Should().Be("Date");
        retrieved.Mappings.AmountColumn.Should().Be("Amount");
        retrieved.Mappings.DescriptionColumn.Should().Be("Description");
        retrieved.Mappings.CategoryColumn.Should().Be("Category");
        retrieved.DeletedAt.Should().BeNull();
    }

    [Fact]
    public async Task CreateProfile_ExcelFormat_PersistsFormat()
    {
        var id = await _service.CreateAsync("BBVA Excel", ImportFormat.Excel, ValidMappings());

        var retrieved = await _service.GetByIdAsync(id);
        retrieved!.Format.Should().Be(ImportFormat.Excel);
    }

    [Fact]
    public async Task SoftDeleteProfile_SetsDeletedAt_ExcludedFromActiveList()
    {
        var id = await _service.CreateAsync("Bank B", ImportFormat.Csv, ValidMappings());

        await _service.DeleteAsync(id);

        var activeList = await _service.GetAllActiveAsync();
        activeList.Should().NotContain(p => p.Id == id);

        var deletedList = await _service.GetRecentlyDeletedAsync();
        deletedList.Should().Contain(p => p.Id == id);
    }

    [Fact]
    public async Task GetRecentlyDeletedAsync_ExcludesProfilesDeletedMoreThan90DaysAgo()
    {
        var old = new ImportProfile
        {
            Id             = Guid.NewGuid(),
            Name           = "Old Bank",
            ColumnMappings = "{}",
            Format         = ImportFormat.Csv,
            CreatedAt      = DateTime.UtcNow.AddDays(-100),
            DeletedAt      = DateTime.UtcNow.AddDays(-91)
        };
        _fixture.Db.ImportProfiles.Add(old);
        await _fixture.Db.SaveChangesAsync();

        var deletedList = await _service.GetRecentlyDeletedAsync();

        deletedList.Should().NotContain(p => p.Id == old.Id);
    }

    [Fact]
    public async Task UpdateMappings_NewMappingsRetrievable()
    {
        var id = await _service.CreateAsync("Bank C", ImportFormat.Csv, ValidMappings());

        var updated = new ImportColumnMappings
        {
            DateColumn        = "Txn Date",
            AmountColumn      = "Debit",
            DescriptionColumn = "Memo",
            CategoryColumn    = null
        };
        await _service.UpdateAsync(id, "Bank C Renamed", updated);

        var retrieved = await _service.GetByIdAsync(id);
        retrieved!.Name.Should().Be("Bank C Renamed");
        retrieved.Mappings.DateColumn.Should().Be("Txn Date");
        retrieved.Mappings.AmountColumn.Should().Be("Debit");
        retrieved.Mappings.DescriptionColumn.Should().Be("Memo");
        retrieved.Mappings.CategoryColumn.Should().BeNull();
    }

    [Fact]
    public async Task RecoverDeletedProfile_WithinWindow_RestoredToActiveList()
    {
        var id = await _service.CreateAsync("Bank D", ImportFormat.Csv, ValidMappings());
        await _service.DeleteAsync(id);

        await _service.RecoverAsync(id);

        var activeList = await _service.GetAllActiveAsync();
        activeList.Should().Contain(p => p.Id == id);

        var deletedList = await _service.GetRecentlyDeletedAsync();
        deletedList.Should().NotContain(p => p.Id == id);
    }
}
```

- [ ] **Step 6: Run full suite**

```bash
dotnet test ProjectCeres.Tests
```

Expected: 0 failed.

- [ ] **Step 7: Commit**

```bash
git add ProjectCeres.Tests/Integration/ImportServiceTests.cs \
        ProjectCeres.Tests/Integration/ImportApiTests.cs \
        ProjectCeres.Tests/Integration/ImportProfileServiceTests.cs
git commit -m "test: add XLSX integration tests, invert XLSX API test, rename profile service tests"
```

---

## Task 16: Update ADRs and Docs

**Files:**
- Modify: `docs/decisions/ADR-0038-import-service-test-approach.md`
- Modify: `docs/decisions/ADR-0046-ofx-import-deferred.md`
- Modify: `docs/decisions/ADR-0047-csv-column-mapping-ux.md`
- Create: `docs/decisions/ADR-0059-import-parser-abstraction.md`
- Modify: `docs/architecture.md`
- Modify: `docs/testing.md`
- Modify: `docs/security-model.md`
- Modify: `docs/roadmap-phase-two.md`

- [ ] **Step 1: Supersede ADR-0038**

At the top of `docs/decisions/ADR-0038-import-service-test-approach.md`, change the status line:

Old:
```
## Status: Accepted
```

New:
```
## Status: Superseded by ADR-0059
```

Add at the bottom:
```
## Superseded By

ADR-0059 (Import Parser Abstraction and Multi-Format Support) supersedes the file format
enforcement section of this ADR. The "XLSX and other formats are rejected" decision has
been reversed — XLSX is now supported. The test approach (unit + integration) is unchanged.
```

- [ ] **Step 2: Add note to ADR-0046**

At the bottom of `docs/decisions/ADR-0046-ofx-import-deferred.md`, add:

```
## Update (2026-04-25)

The `IImportParser` abstraction introduced in ADR-0059 makes OFX a straightforward
addition when the time comes: implement `OFXImportParser : IImportParser`, add one
line to `ImportParserFactory`, register in DI. No changes to `ImportService` or the
DB schema are required.
```

- [ ] **Step 3: Update ADR-0047**

In `docs/decisions/ADR-0047-csv-column-mapping-ux.md`:

a) In the `CsvImportProfile schema` section, replace the schema block with:

```
ImportProfile
  Id          uuid NOT NULL
  Name        varchar NOT NULL
  Mappings    jsonb NOT NULL   ← { "date": "Fecha", "amount": "Importe", ... }
  Format      varchar NOT NULL DEFAULT 'Csv'   ← 'Csv' or 'Excel'
  SheetName   varchar NULL     ← Excel only; null = read first worksheet
  CreatedAt   datetime NOT NULL
  DeletedAt   datetime NULL
```

b) Replace all occurrences of `CsvImportProfile` with `ImportProfile` and `CsvColumnMappings` with `ImportColumnMappings` in the document.

- [ ] **Step 4: Create ADR-0059**

```markdown
# ADR 0059: Import Parser Abstraction and Multi-Format Support

## Status: Accepted

## Context

Field evidence shows that BBVA Spain and other EU banks export statements as Excel (.xlsx)
natively and do not reliably offer CSV. ADR-0038 deferred XLSX support pending real-world
usage data. That data now exists.

`ImportService` previously handled both format detection and import orchestration. Adding
XLSX by extending a single service would violate SRP and OCP — every new format would
require modifying the service.

## Decision

Introduce `IImportParser` / `ImportParserFactory` to decouple format-specific parsing from
the import orchestration pipeline.

### Interface

```csharp
public interface IImportParser
{
    ImportFormat Format { get; }
    Task<IReadOnlyList<ParsedImportRow>> ParseAsync(IFormFile file, ImportColumnMappings mappings);
}
```

### Implementations

- `CsvImportParser` — existing CSV logic, extracted from `ImportService`
- `ExcelImportParser` — new, uses ClosedXML (MIT licensed). Reads first worksheet by
  default; reads named worksheet if `ImportColumnMappings.SheetName` is set.

### Factory

```csharp
public class ImportParserFactory
{
    public IImportParser GetParser(ImportFormat format) => format switch
    {
        ImportFormat.Csv   => _csvParser,
        ImportFormat.Excel => _excelParser,
        _ => throw new ArgumentOutOfRangeException(nameof(format))
    };
}
```

### `ImportProfile` entity

`CsvImportProfile` renamed to `ImportProfile`. Two columns added:
- `Format varchar NOT NULL DEFAULT 'Csv'` — stored as `ImportFormat` enum string
- `SheetName varchar NULL` — Excel sheet name override; null = first sheet

### `ImportService`

Format-blind. Detects format from file extension, calls `ImportParserFactory.GetParser`,
receives `IReadOnlyList<ParsedImportRow>`, runs unchanged duplicate detection + write pipeline.

### Adding a new format

Implement `IImportParser`, add one line to `ImportParserFactory`, register in DI. No other
changes required.

## Formats supported

| Format | Phase |
|---|---|
| CSV | Phase 2 |
| Excel (.xlsx) | Phase 2 |
| OFX | Deferred (see ADR-0046) |
| PDF | Deferred (see import-process-review.md) |

## Security

XLSX files are validated via magic bytes check (`PK\x03\x04`) before being opened by
ClosedXML. Import files are parsed in memory and never written to the filesystem.
A 10 MB size limit is enforced at the controller level before any parsing.

## Consequences

**Positive:**
- Adding a new import format is a new class + one factory line — no service layer changes
- Each parser is independently testable in isolation
- `ImportService` has one responsibility: orchestration

**Negative:**
- ClosedXML is a new dependency — adds ~2 MB to the published output
- Two test layers to maintain (unit for parsers, integration for full pipeline)
```

- [ ] **Step 5: Update `architecture.md` "What Lives Where" section**

Find the `IImportService.cs / ImportService.cs` line and add below it:

```
    IImportParser.cs                              ← parser abstraction — Format + ParseAsync
    CsvImportParser.cs                            ← CSV parsing via CsvHelper
    ExcelImportParser.cs                          ← XLSX parsing via ClosedXML; magic bytes check; sheet selection
    ImportParserFactory.cs                        ← routes ImportFormat → IImportParser; one line per format
    IImportProfileService.cs / ImportProfileService.cs  ← CRUD + soft delete for ImportProfile
```

- [ ] **Step 6: Update `testing.md`**

In the Phase 1 Service Coverage table (or Phase 2 equivalent), update the import rows:

```
| `CsvImportParser`    | `Unit/CsvImportParserTests.cs`          |
| `ExcelImportParser`  | `Unit/ExcelImportParserTests.cs`        |
| `ImportParserFactory`| `Unit/ImportParserFactoryTests.cs`      |
| `ImportService`      | `Integration/ImportServiceTests.cs`     |
| `ImportProfileService` | `Integration/ImportProfileServiceTests.cs` |
```

- [ ] **Step 7: Update `security-model.md`**

In the **File Handling Rules** section, add a new subsection after "Serving (security)":

```markdown
### Import files (upload-only, not stored)

Import files (CSV, XLSX) are parsed in memory and discarded. They are never written to
`uploads/` or any other filesystem path.

1. **File size limit:** enforce a maximum of 10 MB at the controller level before any
   parsing begins. Return 400 if exceeded. Prevents ClosedXML from loading an
   unbounded workbook into memory.

2. **XLSX magic bytes check:** XLSX files are ZIP archives with magic bytes `PK\x03\x04`
   (bytes 0–3). `ExcelImportParser` verifies this before opening with ClosedXML. A file
   with a `.xlsx` extension that fails the check throws `InvalidOperationException` with
   a user-facing message. This check runs before any library code processes the bytes.

3. **No filesystem persistence:** parsed `ParsedImportRow` objects are the only output
   that persists beyond the request — as `Transaction` rows in the database. The original
   file is never saved.
```

- [ ] **Step 8: Update `roadmap-phase-two.md` Verification Checklist**

In the **CSV Import** section of the Verification Checklist:

a) Remove this item:
```
- [x] Upload an `.xlsx` file → rejected with "Only CSV files are supported. Please export your bank statement as CSV."
```

b) Add these items:
```
- [ ] Upload a valid `.xlsx` file with a correctly configured Excel profile → transactions imported; summary shows correct counts
- [ ] Upload `.xlsx` with multiple worksheets and no SheetName set on profile → first sheet is read; import succeeds
- [ ] Upload `.xlsx` with SheetName set on profile → named sheet is read; other sheets ignored
- [ ] Upload `.xlsx` file that fails magic bytes check (spoofed extension) → rejected with user-facing error
- [ ] Upload `.xlsx` file exceeding 10 MB size limit → rejected before parsing
- [ ] Upload `.csv` file with a profile where Format = 'Csv' → still works; no regression
- [ ] `ImportFormat.Excel` profile routes to `ExcelImportParser`; `ImportFormat.Csv` routes to `CsvImportParser` _(covered by ImportParserFactoryTests)_
```

- [ ] **Step 9: Build and test**

```bash
dotnet build ProjectCeres && dotnet test ProjectCeres.Tests
```

Expected: `Build succeeded.` / 0 failed.

- [ ] **Step 10: Commit**

```bash
git add docs/decisions/ADR-0038-import-service-test-approach.md \
        docs/decisions/ADR-0046-ofx-import-deferred.md \
        docs/decisions/ADR-0047-csv-column-mapping-ux.md \
        docs/decisions/ADR-0059-import-parser-abstraction.md \
        docs/architecture.md \
        docs/testing.md \
        docs/security-model.md \
        docs/roadmap-phase-two.md
git commit -m "docs: update ADRs and docs for import system upgrade (CSV + XLSX)"
```

---

## Task 17: Final Verification

- [ ] **Step 1: Full build**

```bash
dotnet build ProjectCeres
```

Expected: `Build succeeded. 0 Warning(s). 0 Error(s).`

- [ ] **Step 2: Full test suite**

```bash
dotnet test ProjectCeres.Tests
```

Expected: 0 failed.

- [ ] **Step 3: Verify no remaining `CsvColumnMappings` references in source**

```bash
grep -r "CsvColumnMappings" ProjectCeres/ ProjectCeres.Tests/ --include="*.cs"
```

Expected: no output.

- [ ] **Step 4: Verify no remaining `CsvImportProfile` type references in source**

```bash
grep -r "CsvImportProfile\b" ProjectCeres/ ProjectCeres.Tests/ --include="*.cs"
```

Expected: no output. (The old `.cs` file is still named `CsvImportProfile.cs` on disk — that is fine, the class inside is `ImportProfile`.)

- [ ] **Step 5: Verify `ImportProfiles` table exists in the test database**

```bash
dotnet ef database update --project ProjectCeres
```

Expected: `No pending migrations.`

- [ ] **Step 6: Final commit**

```bash
git add -A
git commit -m "feat: import system upgrade complete — CSV + XLSX support via IImportParser abstraction"
```
