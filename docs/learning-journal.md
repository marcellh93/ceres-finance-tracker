# Learning Journal

A running record of every dev-teacher session — what was built, what was explained,
what code was produced, and what decisions surfaced. Append a new entry at the end of
each session. Never edit past entries.

---

## How to read this

Each entry covers one session. The **Concepts** section is your reference for terms
explained — search here before asking "what was that thing called again?" The **Code**
section holds the key snippets produced, with the explanation attached. The **Decisions**
section tracks anything that was flagged for a future ADR or doc update.

---

<!-- SESSION ENTRIES GO BELOW THIS LINE -->

## 2026-04-12 — Project scaffold, entity models, and DbContext

### What was built

The Phase 1 project skeleton was created from scratch. This covered three roadmap steps: the solution scaffold (Step 1), the full entity model layer (Step 2), and the DbContext with seed data wired into the DI container (Step 3). Along the way, the runtime was upgraded from .NET 8 (the template default) through .NET 9 to .NET 10 LTS, and the frontend was stripped of all Bootstrap/jQuery scaffolding so the UI can be built from scratch. By the end of the session, `dotnet build` produced zero errors and zero warnings across both projects.

### Concepts covered

**Solution file (`.sln`)** — A container file that groups multiple projects together. Running `dotnet build` or `dotnet test` at the solution level operates on all projects at once.

**ASP.NET Core MVC** — A web framework where incoming HTTP requests are routed to Controller methods, which call services, then pass data to Razor Views that render HTML server-side.

**xUnit** — A test framework for .NET. Each method marked `[Fact]` is one test. The test runner discovers and executes these automatically with `dotnet test`.

**NuGet** — The package manager for .NET. Packages are declared in the `.csproj` file and downloaded on restore. Equivalent to npm for Node or pip for Python.

**`Npgsql.EntityFrameworkCore.PostgreSQL`** — The EF Core database provider for PostgreSQL. Without this, EF Core doesn't know how to talk to PostgreSQL — it only knows how to talk to a generic relational database.

**`Microsoft.EntityFrameworkCore.Design`** — A build-time-only package that enables the `dotnet ef` CLI commands (e.g. `migrations add`, `database update`). Marked `PrivateAssets=all` so it's not included in the published output.

**`Mime-Detective`** — A library that verifies file type by reading the file's actual bytes (magic bytes), not by trusting the file extension or the browser-declared MIME type. Required by the security model for file upload validation.

**Keg-only (Homebrew)** — When Homebrew installs a package as keg-only, it does not symlink the binary into `/opt/homebrew/bin`. This happens when multiple versions of the same tool are installed. You must add the specific version's bin path to `PATH` manually.

**`PATH`** — An environment variable that lists the directories the shell searches when you type a command. Adding `/opt/homebrew/opt/dotnet/bin` to PATH makes `dotnet` resolve to .NET 10 instead of the older version.

**`.zshrc`** — The zsh shell configuration file that runs every time a new terminal session opens. PATH exports placed here persist across sessions.

**Entity class** — A plain C# class whose properties map one-to-one to columns in a database table. EF Core reads these classes and uses them to generate the schema.

**`DbSet<T>`** — A property on the DbContext that represents a database table. `DbSet<Account>` lets you write LINQ queries that EF Core translates to SQL against the `Accounts` table.

**`DbContext`** — The EF Core class that manages the database connection, tracks entity changes, and translates LINQ to SQL. Your application's `AppDbContext` inherits from it.

**`OnModelCreating`** — A method on DbContext that you override to configure relationships and constraints that can't be expressed with simple attributes. This is where the Fluent API lives.

**Fluent API** — A method-chaining style of EF Core configuration (e.g. `.HasOne(...).WithMany(...).OnDelete(...)`) that describes relationships explicitly in code rather than relying on conventions.

**Navigation property** — A property on an entity class that holds a reference to a related entity (e.g. `Transaction.Account`). EF Core uses these to build JOIN queries and populate related objects.

**`DeleteBehavior.Restrict`** — Tells the database to reject a DELETE if any row in another table still references this row via a foreign key. Used here to enforce that accounts and categories cannot be hard-deleted while transactions reference them.

**`DeleteBehavior.SetNull`** — On delete, sets the FK column to null in child rows rather than blocking the delete or cascading it. Used for SavedReport's optional FK to Account and Category, where losing the filter preset is acceptable.

**`HasData`** — EF Core's seed data API. Data registered here is baked into the migration and applied when `database update` runs. Requires fixed primary keys so EF Core can detect changes between migrations.

**Anemic Domain Model** — A design pattern where entity classes are plain data containers with no business logic. All rules and behaviour live in service classes. This project uses this pattern intentionally (ADR-0017).

**Rich Domain Model** — The alternative to anemic: entities have private setters, constructors that enforce invariants, and methods that contain business logic. More idiomatic OOP, but adds complexity and requires extra EF Core configuration.

**`decimal(18,2)`** — The SQL precision annotation for financial amounts: 18 total digits, 2 decimal places. Applied via `[Column(TypeName = "decimal(18,2)")]` to ensure consistent precision across all monetary columns (ADR-0006).

**`DateOnly`** — A .NET type that stores a calendar date with no time component and no timezone. Used for transaction dates per ADR-0009 — the meaningful unit for a personal finance transaction is the date, not the exact timestamp.

**`Guid` (UUID)** — A 128-bit identifier generated client-side. Used as the primary key for all user-facing entities so that IDs in URLs are non-sequential and cannot be enumerated (ADR-0018).

**`Frequency` enum** — A C# enum whose values (Weekly, Biweekly, Monthly, Annual) EF Core stores as integers in the database. Used by `RecurringTransaction` to describe the schedule of a reminder.

**`null!`** — A C# null-forgiving operator used on navigation properties to tell the compiler "I know this looks nullable, but EF Core will always populate it before I use it." Required to satisfy nullable reference type analysis without making the property nullable.

**Connection string** — A string that tells the application how to connect to the database: hostname, database name, username, and password. Stored in `appsettings.json` and read by `Program.cs` at startup.

**Dependency Injection (DI)** — A pattern where objects declare what they need (e.g. `AppDbContext`) and the framework creates and provides those objects automatically. `builder.Services.AddDbContext<AppDbContext>(...)` registers the DbContext so any class that declares it in its constructor receives it.

**`appsettings.json`** — The primary configuration file for an ASP.NET Core app. Values here are available via `IConfiguration` at runtime. Sensitive values (passwords) should be overridden via environment variables or user secrets in production.

### Key code produced

**Transfer relationship configuration — why both FKs must be explicit:**
```csharp
modelBuilder.Entity<Transfer>()
    .HasOne(t => t.SourceAccount)
    .WithMany()
    .HasForeignKey(t => t.SourceAccountId)
    .OnDelete(DeleteBehavior.Restrict);

modelBuilder.Entity<Transfer>()
    .HasOne(t => t.DestAccount)
    .WithMany()
    .HasForeignKey(t => t.DestAccountId)
    .OnDelete(DeleteBehavior.Restrict);
```
Transfer references Account twice. EF Core's conventions can't tell which FK maps to which navigation property when there are two of them pointing at the same table. Without explicit configuration, the migration would fail or produce wrong column names.

**Seeded Guid pattern:**
```csharp
new Account
{
    Id = new Guid("10000000-0000-0000-0000-000000000001"),
    Name = "Cash",
    ...
}
```
Seed data requires fixed, known primary keys so EF Core can detect changes between migrations. Using a predictable pattern (prefix `10...` for accounts, `20...` for categories) makes them easy to read in the database and distinguish from user-generated records.

**DbContext registration in Program.cs:**
```csharp
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection")));
```
This registers the DbContext with ASP.NET Core's DI container so any service or controller that declares `AppDbContext` in its constructor receives one automatically. `UseNpgsql` tells EF Core to use PostgreSQL as the database provider.

### Commands run

- `dotnet new mvc -n ProjectCeres -o ProjectCeres` — Creates a new ASP.NET Core MVC project named `ProjectCeres` in the `ProjectCeres/` folder. `-n` sets the project name, `-o` sets the output directory.
- `dotnet new xunit -n ProjectCeres.Tests -o ProjectCeres.Tests` — Creates a new xUnit test project.
- `dotnet new sln -n ProjectCeres` — Creates a solution file named `ProjectCeres.sln`.
- `dotnet sln add <path>` — Adds a project to the solution file so it is included in solution-level builds and test runs.
- `dotnet add <project> package <name>` — Adds a NuGet package reference to a specific project.
- `dotnet add <project> package <name> --version "9.*"` — Adds a package pinned to the latest 9.x release. Used when the latest package requires a newer runtime than the project targets.
- `brew install dotnet@9` / `brew install dotnet@10` — Installs a specific .NET SDK version via Homebrew.
- `brew link --overwrite dotnet@9` — Forces Homebrew to symlink the keg-only package into the system PATH. Not used here — PATH was set manually instead.
- `dotnet build ProjectCeres.sln` — Compiles all projects in the solution. Reports errors and warnings.
- `dotnet test ProjectCeres.sln` — Discovers and runs all xUnit tests across all test projects in the solution.

### Decisions made

- **.NET 10 over .NET 9**: .NET 9 is STS with EOL in May 2026. .NET 10 is LTS, supported until November 2028. Captured in ADR-0030.
- **Mime-Detective package name**: The NuGet package is `Mime-Detective` (with a hyphen), not `MimeDetective`. The unversioned name does not exist on NuGet.
- **Anemic Domain Model for entities**: Entity classes are plain data containers — public getters and setters, no business logic. This is correct and intentional for this architecture (ADR-0017). Business rules live in the service layer, not inside entities.
- **`decimal(18,2)` via attribute**: Applied directly on model properties with `[Column(TypeName = "decimal(18,2)")]`. This is visible in code review and flows into generated migrations automatically (ADR-0006).
- **Frontend stripped to blank**: All Bootstrap, jQuery, and scaffold CSS/JS were removed from `wwwroot` and layouts. The UI will be built from scratch — no inherited framework styles.

### Open items

- `appsettings.json` connection string uses `postgres`/`postgres` as defaults. Must be updated to match the local PostgreSQL credentials before running Step 4 (migrations).
- Step 4 requires PostgreSQL to be running locally and the `project_ceres` database to either exist or be creatable by the configured user.

### What to cover next

Step 4 — run the initial migration (`dotnet ef migrations add InitialCreate`) and apply it to the database (`dotnet ef database update`). This is where the schema defined in the entity models and DbContext seed data becomes real tables in PostgreSQL.

---

## 2026-04-12 — Documentation sync and model corrections

### What was built

A documentation sync pass was run after completing Steps 1–3. The session caught three discrepancies between the code and the docs: the DbContext filename was wrong in the architecture doc, and two column constraints on `RecurringTransaction` were inverted relative to what the data model document specified. The model code was corrected to match the authoritative documentation. A Fluent API conversion was also added so the `Frequency` enum stores as a readable string in the database rather than an opaque integer.

### Concepts covered

**`/sync-docs`** — a skill that diffs the current session's changes against documentation, applies the routing rules in `doc-agent-instructions.md`, and updates the relevant docs. Its job is to keep docs and code in sync at the end of every session.

**Code-to-doc vs. doc-to-code** — when a discrepancy exists between a model file and a documentation spec, the authoritative source determines the direction of the fix. Here the data model document (`models.md`) had been carefully reasoned, so the code was changed to match it — not the other way around.

**Nullable value types in C#** — adding `?` to a value type like `decimal` or `int` makes it nullable. `decimal?` maps to a nullable column in the database (`NULL` is permitted). `decimal` (no `?`) maps to a NOT NULL column. This matters for EF Core migrations: the generated SQL column definition will include `NOT NULL` or not depending on this annotation in the model class.

**`HasConversion<string>()`** — an EF Core Fluent API call that instructs EF Core to convert a C# enum to its string name (e.g. `Frequency.Monthly` → `"Monthly"`) before writing to the database, and convert back on read. Without this, EF Core stores enums as their integer index (`0`, `1`, `2`, `3`), which is unreadable in the DB. Using `HasConversion<string>()` makes the stored value match what was documented as varchar in `models.md`.

**EF Core column type mapping** — EF Core derives the SQL column type from the C# property type. `decimal` → `numeric`; `int` → `integer`; `string` → `text` (Npgsql); `bool` → `boolean`; `Guid` → `uuid`; `DateOnly` → `date`. Custom types like `decimal(18,2)` require an explicit `[Column(TypeName = "decimal(18,2)")]` attribute.

**Documentation routing rules** — `doc-agent-instructions.md` defines a table mapping change types to destination files. Schema/column changes go to `models.md`. Layer/architecture changes go to `architecture.md`. The routing ensures no decision leaks into the wrong file.

**`git diff --name-only`** — lists only the filenames of changed files, not the content. Useful for a quick survey of scope before reading the full diff.

**Idempotent documentation** — a doc entry is idempotent if writing it twice would not contradict itself. ADRs are the canonical example: once numbered and written, they are never edited (a reversal gets a new ADR). Learning journal entries follow the same rule: append only.

### Key code produced

**Model nullability fix — `RecurringTransaction.cs`:**
```csharp
// Before (mismatched docs):
public decimal? EstimatedAmount { get; set; }
public int DayOfPeriod { get; set; }

// After (matches models.md):
public decimal EstimatedAmount { get; set; }
public int? DayOfPeriod { get; set; }
```
`EstimatedAmount` is NOT NULL because the reminder form always needs a default amount to pre-fill. `DayOfPeriod` is nullable because annual entries have their schedule driven entirely by `NextDueDate` — there is no meaningful "day of period" to store.

**Enum-to-string conversion — `AppDbContext.cs`:**
```csharp
modelBuilder.Entity<RecurringTransaction>()
    .Property(r => r.Frequency)
    .HasConversion<string>();
```
This single Fluent API call makes EF Core write `"Weekly"`, `"Monthly"` etc. to the column instead of `0`, `2`. No migration change is needed when adding this before the first migration is run — it will simply generate the correct `character varying` column type from the start.

### Commands run

- `dotnet build ProjectCeres/ProjectCeres.csproj` — compiles the main project. Used to confirm that the model changes did not introduce any compilation errors before proceeding.

### Decisions made

- **Code corrected to match docs, not vice versa**: `models.md` was the authoritative source for the `RecurringTransaction` column constraints. The reasoning (why `EstimatedAmount` is NOT NULL, why `DayOfPeriod` is nullable) was already documented there. Changing the docs to match an accidental code detail would have lost that reasoning.
- **`Frequency` stored as string**: Using `HasConversion<string>()` keeps the database readable and consistent with the `varchar` type recorded in `models.md`. The C# `Frequency` enum is retained for type safety in application code — no stringly-typed strings in the service layer.

### Open items

- Architecture doc now correctly references `AppDbContext.cs`. No further doc updates required from this session.
- Step 4 (migrations) still pending — PostgreSQL must be running and credentials confirmed before running `dotnet ef migrations add`.

### What to cover next

Step 4 — confirm the local PostgreSQL credentials, update `appsettings.json` if needed, create the `project_ceres` database, and run `dotnet ef migrations add InitialCreate` followed by `dotnet ef database update` to apply the schema for the first time.

---

## 2026-04-12 — Phase 1 complete retrospective

### What was built

The entirety of Phase 1 — a working local personal finance tracker that replaces spreadsheets. Starting from an empty ASP.NET Core MVC project, the following was built across multiple sessions: a full relational data model (13 entities), all CRUD controllers and Razor views for accounts, categories, transactions, transfers, recurring transactions, and settings, four financial reports (Net Worth Statement, Income & Expense Summary, Expense Breakdown by Category, Transaction History), a dashboard with month-to-date totals and savings rate, file attachment uploads with magic-byte MIME validation, opening balance management stored as a system transaction excluded from reports, locale-aware decimal input formatting with a custom model binder, an initial EF Core migration applied to a local PostgreSQL database, and a test suite with unit tests and integration tests backed by a real database with transaction rollback isolation.

### Concepts covered

**ASP.NET Core MVC** — A server-side web framework where the browser sends an HTTP request, a Controller method processes it, calls a Service for business logic, then passes a ViewModel to a Razor View which renders HTML and returns it to the browser. Every page load is a full round-trip — there is no client-side JavaScript in Phase 1.

**Razor View (`.cshtml`)** — A file that mixes HTML with C# using `@` syntax. The C# is evaluated on the server and replaced with values before the HTML is sent to the browser. Razor files are compiled — errors surface at build time, not at runtime.

**ViewModel** — A C# class that carries exactly the data a specific view needs. Controllers never pass EF Core entity objects directly to views; they map entity data to a ViewModel first. This decouples the database shape from the UI shape and prevents over-posting attacks (where a POST could bind fields the user shouldn't be able to set).

**Tag helpers (`asp-for`, `asp-action`, `asp-route-*`)** — Server-side HTML attributes processed by ASP.NET Core before the view is rendered. `asp-for="Amount"` wires a form input to the ViewModel's `Amount` property — it sets `id`, `name`, and validation attributes automatically. `asp-action` generates the correct form action URL. They eliminate hand-written URL construction and reduce the chance of mismatched form field names.

**`[ValidateAntiForgeryToken]`** — An attribute on POST controller actions that checks for a hidden anti-forgery token the framework automatically injects into every `<form>` rendered by Razor. Without it, a malicious third-party site could submit a form to the app on behalf of a logged-in user (CSRF attack). ASP.NET Core generates and validates the token automatically — you just must not forget the attribute.

**`ModelState.IsValid`** — The result of server-side validation after ASP.NET Core has tried to bind the incoming POST data to the ViewModel. Data Annotations on the ViewModel (`[Required]`, `[Range]`, `[StringLength]`) are evaluated automatically. If any fail, `IsValid` is false and the controller returns the view again with the errors — it never calls the service.

**`[Range(typeof(decimal), "...", "...", ParseLimitsInInvariantCulture = true)]`** — The `ParseLimitsInInvariantCulture = true` flag is essential when the system locale uses comma as the decimal separator (e.g. Spain). Without it, ASP.NET Core tries to parse the range boundary strings (`"-999999999999.99"`) using the system culture, which fails because it cannot interpret `.` as a decimal separator in a comma-decimal locale. The flag forces the boundary parsing to use invariant culture regardless.

**EF Core** — An Object-Relational Mapper (ORM) that lets you write C# LINQ queries instead of raw SQL. EF Core translates the LINQ to SQL, executes it against PostgreSQL, and maps the result rows back to C# objects. The mapping is defined by the entity class properties and configured in `AppDbContext`.

**`AppDbContext`** — The central EF Core class for a project. It holds a `DbSet<T>` property for each entity (which is how you query that table). It also contains `OnModelCreating` where you write Fluent API configuration: relationships, constraints, and seed data. The `DbContext` is registered in DI as Scoped — one instance per HTTP request.

**EF Core Fluent API vs Data Annotations** — Two ways to configure EF Core mappings. Data Annotations are attributes on the entity class (`[Required]`, `[MaxLength]`). Fluent API is code in `OnModelCreating` (`modelBuilder.Entity<T>().HasIndex(...).IsUnique()`). Fluent API is preferred for complex constraints (unique indexes, value conversions, relationships) because it keeps the entity class clean.

**EF Core migration** — A versioned snapshot of a schema change. When you change an entity class, EF Core cannot update the database automatically. You run `dotnet ef migrations add <Name>` to generate a C# class describing the SQL change (CREATE TABLE, ADD COLUMN, DROP COLUMN, etc.), then `dotnet ef database update` to apply it. Migrations accumulate — each one builds on the previous ones and they are applied in order, tracked in the `__EFMigrationsHistory` table.

**Seed data** — Initial rows inserted by EF Core the first time (or whenever the migration runs). Defined in `OnModelCreating` via `modelBuilder.Entity<T>().HasData(...)`. Used for system-defined lookup values that must always exist: `AccountType` (Asset, Liability), `CategoryType` (Income, Expense), `Currency` (EUR, USD, etc.), and the Opening Balance system category.

**Derived values** — Values that are always computed from stored data, never stored themselves. Account balance = SUM of transactions (income adds, expense subtracts). Net worth = assets − liabilities. Budget actual spend = SUM of linked transactions. Storing these would risk them getting out of sync with the underlying transactions. EF Core LINQ queries compute them on demand.

**`Transaction.Amount` always positive** — The sign of a transaction is not stored on the transaction itself. Direction is derived from the category's type: if `Category.CategoryType.Name == "Income"` it adds to balance; if `"Expense"` it subtracts. This is 3rd Normal Form — the income/expense direction is a property of the category, not of each individual transaction. Violating this rule (e.g. storing negative amounts for expenses) would make the formula inconsistent and break all balance calculations.

**`IsSystem` flag on `Category`** — Marks the Opening Balance category as system-managed. Any query that should show only user-facing transactions must filter `!t.Category.IsSystem`. This was added to: `GetIncomeExpenseSummaryAsync`, `GetExpenseBreakdownAsync`, `GetTransactionHistoryAsync`, `GetRecentAsync`, `CountAsync`, and the dashboard MTD query. Missing it in any one query causes opening balance transactions to appear as phantom income.

**`Transfer` has no category** — A transfer moves money between two accounts you own. It is neither income nor expense — it does not change your net worth. Giving it a category would make it appear in income/expense reports. Instead, transfers are stored in their own table with source and destination accounts, and every report query explicitly excludes transfers.

**Opening balance as a system transaction** — When an account is created or edited, the opening balance is stored as a `Transaction` linked to the system Opening Balance category (`IsSystem = true`). The known `CategoryId` is a hardcoded Guid (`20000000-0000-0000-0000-000000000001`) seeded into the database. The service finds the existing opening balance transaction by this Guid, then creates/updates/deletes it based on the new value. This means: (1) account balance calculation includes it (correct — the opening balance contributes to the actual balance), (2) all reports exclude it via the `IsSystem` filter (correct — it's not real income).

**Dependency Injection (DI)** — A pattern where objects declare their dependencies in their constructor instead of creating them themselves. ASP.NET Core's DI container reads these constructors and injects the correct objects automatically. Services are registered as `Scoped` (one instance per HTTP request) in `Program.cs`. Controllers and filters receive their services via constructor injection. This makes services independently testable — you can inject a mock instead of the real implementation.

**Primary constructor syntax (C# 12)** — `public class AccountService(AppDbContext db)` is shorthand for declaring a constructor that assigns `db` to a private field. Used throughout Phase 1 for all services. Cleaner than the traditional constructor-with-field-declaration pattern.

**`IAsyncActionFilter`** — An interface for global action filters. `NumberFormatActionFilter` implements it to run before every controller action, reading the Settings row and storing `NumberFormat` in `ViewData["NumberFormat"]`. Every view then uses this value to format amounts. Registered globally via `options.Filters.AddService<NumberFormatActionFilter>()` in `Program.cs` — no per-action attribute needed.

**`IModelBinder` / `IModelBinderProvider`** — Extension points in the ASP.NET Core model binding pipeline. `DecimalModelBinder` intercepts every `decimal` and `decimal?` form field and parses the raw string using the user's configured culture (comma or period decimal). `DecimalModelBinderProvider` tells the framework when to use it. Registered via `options.ModelBinderProviders.Insert(0, ...)` — `Insert(0, ...)` places it first so it takes priority over the default decimal binder.

**Why `type="text"` instead of `type="number"` for decimal inputs** — `type="number"` HTML inputs always submit values in invariant format (`.` as decimal separator), regardless of the user's locale. This makes them incompatible with a comma-decimal locale: if the user types `1.234,56`, the browser submits `1.234` (invalid). Switching to `type="text"` with explicit `id`, `name`, and `value` attributes lets the user type in their locale's format, and the custom model binder parses it correctly.

**Magic-byte MIME detection** — A security technique for validating uploaded file types. Instead of trusting the `Content-Type` header (which is user-controlled and can be forged) or the file extension (also user-controlled), the actual file bytes are inspected for the signature pattern at the start of the file — the "magic bytes". A PNG file always starts with `\x89PNG`; a PDF starts with `%PDF`. The Mime-Detective library performs this inspection. The allowed MIME type list is a fixed dictionary in the service code — the file extension is derived from that list, never from user input.

**`uploads/` outside `wwwroot/`** — File attachments are stored in a folder that is not publicly accessible via a URL. `wwwroot/` is served statically by ASP.NET Core's static file middleware — any file there is directly downloadable if you know the path. `uploads/` sits alongside `wwwroot/`, outside its scope. To serve an attachment, the controller reads the file bytes and returns a `File(bytes, contentType)` response, which allows the service to enforce access control and re-verify the MIME type at serve time.

**xUnit** — The unit testing framework used in `ProjectCeres.Tests`. Test methods are marked with `[Fact]`. The test runner discovers and executes them with `dotnet test`.

**FluentAssertions** — A library that provides readable assertion syntax: `.Should().Be(1600m)` instead of `Assert.Equal(1600m, result)`. Makes test failures easier to understand because the error messages describe the expectation in plain English.

**Integration test with transaction rollback** — Each integration test wraps its database operations in an explicit transaction that is rolled back in `DisposeAsync` (via `IAsyncLifetime`). This means every test starts from a clean database state without needing to delete data after each test. `IDbContextTransaction.RollbackAsync()` undoes all writes made during the test, leaving the database exactly as it was before the test ran.

**`IAsyncDisposable` / `DisposeAsync`** — The async equivalent of `IDisposable.Dispose()`. Used in `TestDbFixture` to roll back the database transaction asynchronously at the end of each test. xUnit calls `DisposeAsync` automatically for any test class that implements it.

### Key code produced

**Balance derivation formula (used in `AccountService.GetBalanceAsync` and `ReportService.GetNetWorthAsync`):**
```csharp
return transactions.Sum(t =>
    t.Category.CategoryType.Name == "Income" ? t.Amount : -t.Amount);
```
Every transaction amount is positive. This ternary applies the sign based on the category type: income adds, expense subtracts. The same formula is duplicated in both places intentionally — it is simple, and abstracting it would obscure the intent.

**Opening balance upsert in `AccountService.UpdateAsync`:**
```csharp
var existing = await db.Transactions
    .FirstOrDefaultAsync(t => t.AccountId == id && t.CategoryId == OpeningBalanceCategoryId);

if (existing is not null)
{
    if (openingBalance == 0)
        db.Transactions.Remove(existing);
    else
        existing.Amount = Math.Abs(openingBalance);
}
else if (openingBalance != 0)
{
    db.Transactions.Add(new Transaction { ... Amount = Math.Abs(openingBalance) ... });
}
```
Three cases: existing transaction + new value of 0 → delete it; existing transaction + non-zero → update amount; no existing + non-zero → create it. `Math.Abs` is applied because the direction is always encoded by the category type (Income), not the sign.

**Global `NumberFormatActionFilter` registered in `Program.cs`:**
```csharp
builder.Services.AddScoped<NumberFormatActionFilter>();
builder.Services.AddControllersWithViews(options =>
{
    options.Filters.AddService<NumberFormatActionFilter>();
    options.ModelBinderProviders.Insert(0, new DecimalModelBinderProvider());
});
```
Both the filter and the model binder are wired up in the same `AddControllersWithViews` call. `AddService<>` means the filter is resolved from DI (so it can receive `ISettingsService`). `Insert(0, ...)` ensures the decimal binder runs before the framework's default decimal binder.

**`DecimalModelBinder` — tolerant parse with fallback:**
```csharp
var culture = NumberFormatHelper.GetCulture(settings.NumberFormat);

if (decimal.TryParse(rawValue, NumberStyles.Number, culture, out var result))
{
    bindingContext.Result = ModelBindingResult.Success(result);
    return;
}
// Tolerant fallback — handles copy-pasted values from external sources
if (decimal.TryParse(rawValue, NumberStyles.Number, CultureInfo.InvariantCulture, out result))
{
    bindingContext.Result = ModelBindingResult.Success(result);
    return;
}
```
Try configured culture first. If that fails, fall back to invariant culture. This handles the common case of a user in comma-decimal mode pasting `1234.56` from a spreadsheet — it binds instead of failing with a cryptic validation error.

**Magic-byte upload in `FileAttachmentService.UploadAsync`:**
```csharp
var detectedMime = DetectMime(bytes);
if (!AllowedMimeTypes.TryGetValue(detectedMime, out var extension))
    throw new InvalidOperationException($"File type '{detectedMime}' is not allowed.");

var relativePath = Path.Combine("uploads", transactionId.ToString(), $"{Guid.NewGuid()}{extension}");
var fullPath = Path.Combine(env.ContentRootPath, relativePath);
```
The extension comes from the `AllowedMimeTypes` dictionary keyed on the detected MIME type — never from the uploaded filename. The stored filename on disk is a random Guid, making it unguessable. The original filename is stored in `FileName` column for display only.

**Integration test isolation with transaction rollback:**
```csharp
public async Task InitAsync()
{
    await Db.Database.MigrateAsync();          // idempotent — applies any pending migrations
    _transaction = await Db.Database.BeginTransactionAsync();  // opens wrapping transaction
}

public async ValueTask DisposeAsync()
{
    if (_transaction is not null)
        await _transaction.RollbackAsync();    // undo everything the test wrote
    await Db.DisposeAsync();
}
```
`MigrateAsync()` is idempotent — safe to call before every test. `BeginTransactionAsync()` wraps the test body. `RollbackAsync()` in `DisposeAsync` ensures no test leaves data behind, so tests can run in any order.

### Commands run

- `dotnet new sln -n ProjectCeres` — creates a solution file that groups related projects (main app + tests).
- `dotnet new mvc -n ProjectCeres` — scaffolds a new ASP.NET Core MVC project with the basic folder structure.
- `dotnet new xunit -n ProjectCeres.Tests` — scaffolds a new xUnit test project.
- `dotnet sln add ProjectCeres/ProjectCeres.csproj ProjectCeres.Tests/ProjectCeres.Tests.csproj` — adds both projects to the solution so `dotnet build` and `dotnet test` run from the root.
- `dotnet add package Npgsql.EntityFrameworkCore.PostgreSQL` — installs the EF Core provider for PostgreSQL.
- `dotnet add package Microsoft.EntityFrameworkCore.Design` — required for the `dotnet ef` CLI to scaffold migrations.
- `dotnet add package Mime-Detective` — magic-byte MIME detection library used for file attachment validation.
- `dotnet ef migrations add InitialCreate --project ProjectCeres` — generates the initial migration from the current EF Core model. Creates two files: the migration class (the change) and the Designer snapshot (the full model state after the change).
- `dotnet ef database update --project ProjectCeres` — connects to the PostgreSQL database and applies all pending migrations. On first run, creates all tables and inserts seed data.
- `dotnet ef migrations add RemoveDateSeparator --project ProjectCeres` — generated after `DateSeparator` was removed from the `Settings` model. Produced an `ALTER TABLE "Settings" DROP COLUMN "DateSeparator"` migration.
- `dotnet build ProjectCeres` — compiles the project. With the Tailwind MSBuild target in place, also runs `pnpm run build:css` beforehand.
- `dotnet test` — runs all tests in `ProjectCeres.Tests`. xUnit discovers and executes `[Fact]` methods automatically.
- `pnpm install` — resolves and installs packages from `package.json` into `node_modules/`. Run once after creating `package.json` and when adding new dependencies.

### Decisions made

- **No double-entry bookkeeping**: Accounts are Assets or Liabilities; Categories are Income or Expense. Balance and net worth are derived from those two axes. This matches how individuals think about money and avoids the debit/credit complexity of accounting systems.
- **`Transfer` excluded from income/expense reports**: A transfer is a movement between your own accounts — it does not change net worth. Giving it a category would pollute income/expense reports. It lives in its own table and is excluded from every report query by design.
- **Deactivate, never hard-delete, for Accounts and Categories**: Accounts and categories have transaction history. Deleting them would orphan historical transactions or require cascading deletes that destroy financial history. `IsActive = false` hides them from active use while preserving all records.
- **Hard-delete for Transactions and Transfers**: Transactions and transfers have no dependants — nothing else is keyed to them except attachments, which are also deleted. There is no audit requirement in Phase 1. Hard delete with a confirmation prompt is the correct approach.
- **Opening balance as a system transaction**: Storing the opening balance as a regular transaction with a system category means the balance derivation formula requires no special case — it just sums all transactions including the opening one. The `IsSystem` flag on the category is the single gate that excludes it from user-facing reports.
- **`IReportService` with a flat method list, not a Strategy pattern**: The four Phase 1 report types are fixed and known upfront. Strategy pattern overhead (an interface per report type, a factory to select it) adds complexity with no benefit when there are only four types and they will not be dynamically discovered or swapped. Revisit if Phase 2 introduces pluggable report types.
- **Magic-byte MIME validation over extension/Content-Type**: File extensions and `Content-Type` headers are both user-controlled and trivially forgeable. Magic-byte detection reads the actual binary content of the file. Combined with an allowlist of accepted MIME types and a system-generated storage path, this eliminates the main attack vectors for malicious file uploads.
- **Custom `DecimalModelBinder` over culture-switching middleware**: Switching the thread culture globally would affect all framework behaviour (date parsing, number parsing, string comparisons). A targeted model binder applies locale-aware parsing only to `decimal` and `decimal?` form fields, leaving everything else at invariant culture. Narrower scope, fewer side effects.
- **`type="text"` inputs for decimals**: `type="number"` inputs in browsers always submit values in invariant format (`.` decimal separator). This is incompatible with comma-decimal locales. `type="text"` with an explicit pre-filled `value` attribute and the custom model binder gives full control over both display format and parse format.

### Logic explained

**How a transaction create flows end-to-end:**
1. User fills in the Create Transaction form (`Views/Transactions/Create.cshtml`) and submits.
2. Browser POSTs to `TransactionsController.Create(TransactionCreateViewModel vm)`.
3. `DecimalModelBinder` intercepts the `Amount` field, reads the user's `NumberFormat` from Settings, and parses `"1.234,56"` as `1234.56m`.
4. `NumberFormatActionFilter` has already run (before the action), setting `ViewData["NumberFormat"]` — but this is only used on GET, not POST.
5. `ModelState.IsValid` checks all Data Annotations on the ViewModel. If any fail (e.g. Amount = 0 fails `[Range]`), the controller returns the view with errors.
6. If valid, the controller calls `transactionService.CreateAsync(...)`.
7. `TransactionService.CreateAsync` creates a new `Transaction` entity and calls `db.SaveChangesAsync()`.
8. Controller redirects to the index page. Browser issues a GET, which re-renders the list.

**How balance is derived and why opening balance is included:**
1. `AccountService.GetBalanceAsync(accountId)` queries all `Transaction` rows for that account, including those with `IsSystem = true`.
2. Each transaction is summed: `+Amount` if `CategoryType.Name == "Income"`, `-Amount` if `"Expense"`.
3. The Opening Balance transaction has `CategoryType.Name == "Income"` (it was seeded that way), so it adds to the balance.
4. This is correct: if your account started with €5,000 and you spent €200, your balance is €4,800 — the opening balance must be included.
5. Reports exclude it via `!t.Category.IsSystem` because it is not real income that should appear in an Income & Expense Summary.

**How the test transaction rollback works:**
1. Before each test: `InitAsync()` calls `MigrateAsync()` (idempotent schema sync) then opens a database transaction.
2. During the test: all EF Core operations (INSERT, UPDATE, DELETE) run inside that transaction but are not yet committed to disk.
3. After the test: `DisposeAsync()` calls `RollbackAsync()`, which tells PostgreSQL to discard all changes made since the transaction started.
4. The database is back to its state before the test ran. The next test starts clean without any setup/teardown scripts.

### Open items

- Budget controller and views: `BudgetService` and ViewModels exist but no controller or views have been built. This is the last missing CRUD feature of Phase 1.
- End-to-end visual review of Tailwind styles across all pages not yet done.
- Opening balance cutover UX is an open question in `docs/planning.md` — no implementation planned for Phase 1.

### What to cover next

Build the Budget controller and views (the last remaining Phase 1 CRUD feature), then do a complete visual review of the running app to confirm all pages render correctly and the Tailwind styles are applied consistently across every screen.

---

## 2026-04-12 — Bug fixes, Settings cleanup, and Tailwind CSS integration

### What was built

Three separate things happened this session. First, two bug fixes: opening balance transactions were leaking into the Transaction History report (a missing filter), and the `DateSeparator` column in Settings was identified as redundant (the separator is already encoded in `DateFormat`) and removed entirely from the database. Second, Tailwind CSS v3 was integrated into the project — a pnpm build pipeline was wired into the MSBuild pre-build step so that `dotnet build` and `dotnet run` automatically generate the CSS output without any manual step. Third, the session ended with a discussion about token efficiency in long Claude Code sessions and concrete steps to reduce cost.

### Concepts covered

**IsSystem flag (on Category)** — A boolean on the `Category` entity that marks certain categories as system-managed (e.g. the Opening Balance category). Code that queries for user-facing transactions must filter `!t.Category.IsSystem` to exclude these hidden system entries. Without the filter, system transactions appear in reports and history as if they were real user transactions.

**Redundant column** — A column whose value can always be derived from another column in the same row. `DateSeparator` was redundant because every `DateFormat` string already contains the separator character (e.g. `"DD/MM/YYYY"` implies `/`). Keeping both created a risk of them going out of sync. The fix: drop the column and treat the format string as the single source of truth.

**EF Core migration** — A versioned record of a schema change. When a column is removed from a C# model class, EF Core cannot apply that change to the database automatically — you must create a migration (`dotnet ef migrations add`) which generates a C# class describing the `ALTER TABLE DROP COLUMN` SQL, then apply it (`dotnet ef database update`). Migrations are cumulative: each one builds on the previous ones and they are applied in order.

**Tailwind CSS** — A utility-first CSS framework. Instead of writing custom CSS rules like `.btn { padding: 8px 16px; }`, you compose pre-built single-purpose classes directly in HTML (`class="px-4 py-2"`). Tailwind scans your source files at build time and generates only the CSS classes that are actually used — the output file is small even though the full Tailwind library is large.

**Tailwind CLI** — The standalone command-line tool that processes a Tailwind input CSS file and writes a minified output CSS file. It takes an input file (`-i`), an output path (`-o`), and optionally `--minify` (removes whitespace for smaller file size) or `--watch` (stays running and rebuilds whenever a source file changes).

**`@tailwind` directives** — Three special lines placed at the top of the Tailwind input CSS file: `@tailwind base` (browser resets and sensible defaults), `@tailwind components` (where `@apply`-based classes live), `@tailwind utilities` (all the utility classes like `px-4`, `text-sm`, etc.). They are processed by the Tailwind CLI and replaced with the actual CSS.

**`@apply`** — A Tailwind directive used inside a CSS rule to compose utility classes into a named component class. Example: `.btn-primary { @apply bg-gray-900 text-white px-4 py-2 rounded-md; }`. This lets you keep the existing class names in all your HTML/Razor files unchanged while still using Tailwind utilities to define their style. It is a Phase 1 shortcut — in Phase 2 the plan is to replace `@apply` patterns with inline utilities in React components.

**`@layer components`** — A Tailwind directive that wraps your `@apply`-based component definitions so they are inserted at the correct position in the CSS output (between base and utilities). This matters for specificity: utilities defined in `@layer utilities` can still override component styles, which is the intended behaviour.

**MSBuild `<Target>`** — An XML element in a `.csproj` file that defines a custom build step. `BeforeTargets="Build"` means this target runs before the standard compile step. `<Exec Command="..." WorkingDirectory="...">` runs a shell command. The result is that `dotnet build` and `dotnet run` automatically trigger the Tailwind CLI — no separate manual CSS build step is needed.

**pnpm** — A Node.js package manager (alternative to npm or yarn). Packages are installed from `package.json`'s `devDependencies`. `pnpm install` reads the manifest and installs packages into `node_modules/`. `pnpm run <script>` executes a named script from the `scripts` section of `package.json`.

**`tailwind.config.js`** — The Tailwind configuration file. The `content` array tells the Tailwind CLI which files to scan when deciding which utility classes to include in the output. For a Razor MVC project this must include all `.cshtml` files: `"./Views/**/*.cshtml"`.

**Context window / token leakage** — Every message in a Claude Code session carries the full conversation history (plus injected files, system reminders, etc.) as "tokens" — the units the model processes. Long sessions accumulate more tokens per message. When a session exceeds the context limit, the conversation is automatically summarised into a large block that is prepended to the next session, carrying its cost forward. Using `/clear` resets this accumulation. The IDE integration also injects the contents of open files as system reminders — each open file adds to the per-message token cost.

**Claude Code memory system** — A file-based persistence mechanism at `.claude/projects/.../memory/`. Memory files written during a session are loaded into the context of future sessions, so the model starts with relevant project knowledge without needing to re-explore the codebase. `MEMORY.md` is an index; individual `.md` files hold the actual content. Writing good memories reduces the number of file reads needed at the start of each session, which reduces token cost.

### Key code produced

**Missing `IsSystem` filter added to `GetTransactionHistoryAsync`:**
```csharp
var query = db.Transactions
    .Where(t => t.Date >= from && t.Date <= to
             && t.Account.CurrencyId == currencyId
             && !t.Category.IsSystem)  // ← added: excludes opening balance transactions
```
The filter was already present in `GetIncomeExpenseSummaryAsync` and `GetExpenseBreakdownAsync` but had been missed in the history query. Opening balances use a system category (`IsSystem = true`) so they are excluded from all user-facing views and reports.

**Tailwind MSBuild target in `ProjectCeres.csproj`:**
```xml
<Target Name="BuildTailwind" BeforeTargets="Build">
  <Exec Command="pnpm run build:css" WorkingDirectory="$(ProjectDir)" />
</Target>
```
`$(ProjectDir)` is an MSBuild built-in variable that expands to the directory of the `.csproj` file. This ensures `pnpm` runs from the right directory regardless of where `dotnet build` is invoked from.

**`@apply`-based component class (from `Styles/app.css`):**
```css
@layer components {
  .btn          { @apply inline-flex items-center px-4 py-2 rounded-md text-sm
                         font-medium transition-colors focus:outline-none
                         focus:ring-2 focus:ring-offset-2 no-underline; }
  .btn-primary  { @apply btn bg-gray-900 text-white hover:bg-gray-700
                         focus:ring-gray-900; }
}
```
`.btn-primary` composes `.btn` (which is itself an `@apply` class) plus colour overrides. This works because Tailwind resolves `@apply` references within the same `@layer`. The result: a single HTML class like `class="btn-primary"` in a Razor view produces a fully styled button without touching the view file.

### Commands run

- `pnpm install` — reads `package.json`, resolves and downloads all `devDependencies` into `node_modules/`. Run once when setting up the project or after adding a new package.
- `dotnet ef migrations add RemoveDateSeparator --project ProjectCeres` — scaffolds a new migration class describing the removal of the `DateSeparator` column. The migration name is arbitrary but should describe the change. EF Core compares the current C# model against the last migration snapshot to determine what SQL to generate.
- `dotnet ef database update --project ProjectCeres` — applies all pending migrations (those not yet recorded in the `__EFMigrationsHistory` table) to the local PostgreSQL database. Executes the `ALTER TABLE "Settings" DROP COLUMN "DateSeparator"` SQL generated by the previous command.
- `dotnet build ProjectCeres` — compiles the project and, via the custom MSBuild target, runs `pnpm run build:css` first. Used to verify the Tailwind integration was working end-to-end.

### Decisions made

- **`DateSeparator` removed**: The column was redundant — the separator character is already present inside `DateFormat` (e.g. `"DD/MM/YYYY"`). Keeping both created a maintenance risk of them diverging. Removed from model, ViewModel, service interface, service implementation, controller, view, seed data, and database via a dedicated migration. `docs/models.md` updated to reflect the removal.
- **Tailwind `@apply` approach for Phase 1**: Rather than rewriting ~35 CSS class names across all Razor views to use inline Tailwind utilities, the `@apply` approach was chosen. All existing class names (`.btn-primary`, `.data-table`, etc.) are preserved in the views and defined via `@apply` in `Styles/app.css`. This is explicitly a Phase 1 shortcut — Phase 2 will migrate to inline utilities alongside the React/shadcn/ui introduction.
- **shadcn/ui deferred to Phase 2**: shadcn/ui is a React component library and cannot be used in a server-side Razor MVC project. Documented in `CLAUDE.md` under "What NOT to Do" and in `docs/planning.md` Phase 2 section.
- **MSBuild pre-build target over separate script**: Wiring the CSS build into `dotnet build` ensures the output is always fresh without requiring the developer to remember to run a separate command. The trade-off is a ~150ms overhead on every build — acceptable for Phase 1.

### Open items

- `pnpm run watch:css` should be run in a second terminal during active view development to get instant CSS rebuilds without re-running `dotnet build`.
- Budget controller and views are still pending — `BudgetService` exists but has no corresponding controller or views.
- End-to-end visual review of the Tailwind styles across all pages has not been done yet.

### What to cover next

Build the Budget controller and views (the last missing CRUD feature in Phase 1), then do a complete end-to-end walkthrough of the running app to verify all pages render correctly with Tailwind styles applied.

---

## 2026-04-13 — Dashboard layout fixes and button styling

### What was built

Three CSS/UI issues were resolved in this session: (1) the Net Worth dashboard card was overflowing its bounds and causing other cards to stack incorrectly; (2) primary buttons were illegible on hover because an inherited anchor style was overriding the text colour; (3) button hover states and primary button colour were refined based on visual preference. No new features or data model changes were made.

### Concepts covered

**CSS specificity and cascade** — When two CSS rules target the same element and property, the one with higher specificity wins. In Tailwind, component layer classes defined with `@apply` have the same specificity as the utilities they expand into, but they are written earlier in the stylesheet than inline utility classes in the HTML. When a global rule like `a { hover:text-gray-900 }` is defined in `@layer base`, it can win over a component-layer hover override unless the component explicitly re-declares the same property.

**`@layer base` vs `@layer components`** — Tailwind processes layers in order: `base` → `components` → `utilities`. Rules in `base` apply globally (e.g. all `<a>` tags). Rules in `components` are opt-in via class names. When a button is rendered as an `<a>` tag, both layers apply — `base` sets the hover text colour globally, and `components` sets the button's background colour. Without an explicit `hover:text-*` on the button class, the base layer's hover text colour wins.

**Tailwind responsive grid prefixes (`sm:`, `lg:`)** — These prefixes apply a utility only at or above a given viewport width breakpoint (`sm` = 640px, `lg` = 1024px). Without a base (unprefixed) value, the browser uses the browser default or an earlier rule. `lg:col-span-2` alone means "span 2 columns at large screens" but at smaller screens no `col-span` rule exists, so the element can accidentally consume all available columns in a 2-column grid.

**`col-span-1` as a reset** — Adding `col-span-1` as the unprefixed base alongside `lg:col-span-2` explicitly pins the element to 1 column at all sizes below `lg`. Without it, in a `sm:grid-cols-2` layout, a card with only `lg:col-span-2` can implicitly span both columns, pushing other cards to the next row.

**Arbitrary values in Tailwind (`bg-[#248e38]`)** — Tailwind's JIT engine allows one-off custom values in square brackets directly in class names, bypassing the need to extend `tailwind.config.js`. The value is picked up at build time and a corresponding CSS rule is generated. Useful for exact brand colours.

**`hover:text-white` on buttons** — When a button is an `<a>` element, the global `a:hover` base rule sets text colour. Adding `hover:text-white` (or `hover:text-gray-700` for light buttons) to the component class explicitly overrides that inherited rule, ensuring text remains legible regardless of the base layer.

### Key code produced

**Dashboard grid with correct column spanning:**
```html
<div class="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-4 gap-6 mb-8">
    <section class="dashboard-card col-span-1 lg:col-span-2">
```
`col-span-1` is the base (mobile/tablet): Net Worth takes one column like every other card. `lg:col-span-2` kicks in at large screens only, giving Net Worth half the 4-column grid while Month to Date and Reminders each take one quarter. Without `col-span-1`, at the `sm` 2-column breakpoint the card would implicitly span both columns and force the other cards onto a second row.

**Fixed `.btn-primary` with hover text lock:**
```css
.btn-primary { @apply btn bg-[#248e38] text-white border-2 border-transparent
                      hover:bg-green-700 hover:text-white focus:ring-green-700; }
```
`hover:text-white` is required because the global `a { hover:text-gray-900 }` base rule would otherwise override the button text colour on hover, producing dark text on a dark background. `border-2 border-transparent` reserves 2px of border space in the default state so the layout does not shift when a visible border is applied on hover.

**Fixed `.btn-secondary` hover text:**
```css
.btn-secondary { @apply btn bg-white text-gray-700 border border-gray-300
                        hover:bg-gray-100 hover:text-gray-900 focus:ring-gray-400; }
```
Same principle — `hover:text-gray-900` prevents the inherited anchor hover rule from resetting text to a different shade unexpectedly.

### Commands run

- `pnpm --dir ProjectCeres run build:css` — runs the Tailwind CLI build from the repo root, targeting the `ProjectCeres` package directory. `--dir ProjectCeres` tells pnpm which workspace package to use. Needed after every change to `app.css` or any `.cshtml` file to regenerate `wwwroot/css/site.css`.

### Decisions made

- **Inline grid classes on the dashboard wrapper instead of `.dashboard-grid`**: The `.dashboard-grid` component class in `app.css` defined `lg:grid-cols-3`, which the CSS `@apply` layer prevented inline utility overrides from changing. Rather than adding another component class variant, the grid wrapper on the Dashboard page uses inline utilities directly (`grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-4`). This is a deliberate per-page override — the dashboard layout differs from the default grid.
- **`hover:text-*` added to all button variants**: All three button classes (`.btn-primary`, `.btn-secondary`, `.btn-danger`) now explicitly declare their hover text colour to prevent the global anchor hover rule from interfering. Applied consistently to avoid the same bug appearing on other button types.

### Logic explained

**Why the button text went dark on hover:**
1. `app.css` defines `a { @apply text-gray-700 hover:text-gray-900; }` in `@layer base` — this applies to every `<a>` tag.
2. Dashboard action buttons are rendered as `<a asp-controller="..." class="btn btn-primary">` — they are anchor tags, not `<button>` elements.
3. `.btn-primary` in `@layer components` sets `text-white` for the default state but did not declare a `hover:text-*` value.
4. On hover, the `@layer base` rule (`hover:text-gray-900`) applied unchallenged — dark gray text on a dark gray background, making the text nearly invisible.
5. Fix: add `hover:text-white` inside `.btn-primary` so the component layer explicitly overrides the base layer rule on hover.

### Open items

- `DayOfPeriod` on `RecurringTransaction` is stored but never read by `AdvanceDueDate` — it is a stub field. Either remove it from the form to avoid user confusion, or wire it up to snap the next due date to the configured day after advancing.
- Budget controller and views are still pending.

### What to cover next

Build the Budget controller and views (the last missing CRUD feature in Phase 1).
