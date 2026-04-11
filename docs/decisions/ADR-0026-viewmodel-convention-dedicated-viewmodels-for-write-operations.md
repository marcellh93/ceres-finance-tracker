# ADR-0026 — ViewModel Convention: Dedicated ViewModels for All Write Operations

**Status:** Accepted

**Context:**

ASP.NET Core MVC model binding can bind HTTP POST request fields directly to EF Core entity classes. This is convenient — no intermediate class is needed — but it exposes all properties of the entity to mass assignment. A form that shows only `Name` and `Amount` fields would silently accept and bind any other property submitted in the raw HTTP request (e.g. `IsActive`, `IsSystem`, `UserId`) if the entity is bound directly.

Additionally, EF Core entity classes carry navigation properties, tracking state, and database concerns that are irrelevant to HTTP input and create noise in form handling.

Several approaches were available:

- **Direct entity binding:** bind the entity class from the POST body directly — convenient but vulnerable to mass assignment
- **`[Bind]` attribute:** whitelist bindable properties on the entity — fragile, easy to miss a new property, couples the entity to HTTP concerns
- **Dedicated ViewModel per form:** a separate class per create/edit operation that declares only the fields the form is allowed to set
- **DTOs with AutoMapper:** dedicated transfer objects mapped automatically — reduces boilerplate but adds a dependency and hides the mapping logic

## Decision

Every create and edit form uses a dedicated ViewModel class (e.g. `AccountCreateViewModel`, `TransactionEditViewModel`). ViewModels declare only the fields the form is permitted to set. No EF Core entity is ever bound directly from a POST request.

Mapping from ViewModel to entity is done manually in the service layer. No AutoMapper in Phase 1 — the mapping code is explicit, visible, and testable.

Read-only display views may receive entity objects or lightweight read models directly — the mass assignment risk only applies to write operations.

Input validation via Data Annotations (`[Required]`, `[Range]`, `[MaxLength]`, etc.) is declared on the ViewModel, not on the entity. Controllers check `ModelState.IsValid` before calling any service.

## Consequences

**Positive:**
- Mass assignment is structurally prevented — only declared ViewModel properties can be bound
- The ViewModel is the explicit contract for what a form is allowed to change; adding a new entity property does not silently expose it to HTTP input
- Validation attributes live on the ViewModel, keeping entity classes free of HTTP concerns
- Manual mapping is verbose but intentional — every field assignment is visible and reviewable

**Negative:**
- More files: one ViewModel class per create form and per edit form
- Manual mapping adds boilerplate in service methods; if AutoMapper is introduced later (Phase 2+), the mapping conventions must be explicitly configured rather than relying on convention-based auto-mapping

**Related decisions:**
- ADR-0017: the service layer handles mapping from ViewModel to entity — this is business logic, not a controller responsibility
- ADR-0025: ViewModels handle input validation via ModelState; domain exceptions handle business rule violations after the ViewModel has already been validated
