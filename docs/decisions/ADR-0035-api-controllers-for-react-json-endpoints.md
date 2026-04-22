# ADR 0035: Dedicated API Controllers for React JSON Endpoints

## Status: Accepted

## Context

Phase 2 introduces React components embedded in Razor pages (per ADR-0014). React components
cannot consume Razor views — they require JSON responses. A decision is needed on where those
JSON-returning endpoints live.

Two options were considered:

**Option A — Secondary actions in existing Razor controllers**
Add `Json(...)` returning actions alongside existing view-returning actions in the same
controller class. No new files or folders.

Rejected because:
- Violates the Single Responsibility Principle — one controller handles two distinct concerns
  (HTML rendering and JSON data serving)
- Makes it harder to reason about what a controller does and harder to test each concern in isolation
- Mixes routing conventions (MVC page routes and API resource routes) in one file

**Option B — Dedicated `[ApiController]`-attributed controllers in `Controllers/Api/`**
Separate controller classes in a dedicated subfolder. Different routing convention (`/api/`
prefix). `[ApiController]` attribute changes framework behavior: automatic model validation,
`[FromBody]` inference, no view resolution.

## Decision

**Option B.** All JSON endpoints for React components live in dedicated `[ApiController]`
controllers under `Controllers/Api/`.

```
Controllers/
  AccountsController.cs          ← Razor views only (HTML)
  TransactionsController.cs      ← Razor views only (HTML)
  Api/
    DashboardApiController.cs    ← JSON only (React dashboard components)
    AccountsApiController.cs     ← JSON only (React account widgets)
```

Razor controllers are untouched and continue to serve HTML pages. API controllers are
additive — created only when a React component needs data. The two sets of controllers
coexist through Phase 2 and into Phase 3, where Razor controllers are progressively deleted
as pages are fully migrated to React.

### `InvalidModelStateResponseFactory` override

`[ApiController]` automatically returns a `400 Bad Request` using ASP.NET's default
validation error format when model binding fails. This conflicts with the error shape
documented in `api-contract.md`. The factory is overridden in `Program.cs` at the point
the first API controller is introduced:

```csharp
builder.Services.AddControllers()
    .ConfigureApiBehaviorOptions(options =>
    {
        options.InvalidModelStateResponseFactory = context =>
        {
            var errors = context.ModelState
                .Where(e => e.Value?.Errors.Count > 0)
                .SelectMany(e => e.Value!.Errors.Select(err => new
                {
                    field = e.Key,
                    message = err.ErrorMessage
                }));

            return new UnprocessableEntityObjectResult(new
            {
                error = new
                {
                    code = "VALIDATION_ERROR",
                    message = "One or more fields are invalid.",
                    details = errors
                }
            });
        };
    });
```

This ensures all validation failure responses from API controllers conform to the contract
in `api-contract.md` regardless of which controller produced them.

### Deletion path

When a page is fully migrated to React in Phase 3, its Razor controller action is deleted,
then the view, then the controller file if empty. No backwards-compatibility stubs are kept.
API controllers in `Controllers/Api/` become the only controllers once Phase 3 migration is
complete.

## Consequences

**Positive:**
- Single Responsibility enforced — each controller class has one job
- API controllers are independently testable from Razor controllers
- `Controllers/Api/` immediately signals to any reader that JSON endpoints live there
- Lays clean groundwork for Phase 3: API controllers remain, Razor controllers are deleted
- Error shape is consistent across all API endpoints from day one

**Negative:**
- More files than Option A — one extra controller per feature area that has React components
- `InvalidModelStateResponseFactory` must be wired up before the first API controller ships
  or the error contract is broken on first use
