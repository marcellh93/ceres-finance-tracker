# ADR-0082 — No CQRS and no MediatR; keep the Controller → Service pattern

> **Diataxis type:** Explanation — architectural decision record.

**Status:** Accepted — 2026-10-03 (Stage 14). Investigated on request; verdict is "do not adopt."

**Phase:** Phase 3 (Hosted Beta).

**Supersedes:** None.

**Related decisions:** ADR-0017 (SOLID / design patterns — Repository rejected, Factory deferred until warranted); ADR-0025 (error handling — typed domain exceptions + global middleware); ADR-0036 (ReportGeneratorFactory strategy pattern).

## Context

The question was raised whether to adopt **CQRS** (Command Query Responsibility Segregation — separate write-model from read-model) and/or **MediatR** (an in-process request/handler dispatch library for .NET) in Project Ceres. A dispatched architecture investigation read the current controllers/services, the governing ADRs, and external guidance (Martin Fowler, Microsoft .NET architecture, MediatR licensing).

Findings that drove the decision:

- **Controllers are already thin and services are already command-handler-shaped.** Controllers parse input → call one or more injected services → map to DTOs → map errors to HTTP status, with no business logic. `AccountService.TryCreateAsync` is a command handler in all but name. The Controller→Service seam MediatR is sold to create already exists via plain DI + interfaces.
- **MediatR went commercial (v13, July 2025, dual RPL-1.5 + paid license).** The .NET ecosystem (Clean Architecture template, major course authors) is documenting migration *away* from it. Adopting a dependency the ecosystem is leaving — for a solo learning project — is the wrong signal.
- **It would reopen ADR-0017.** That ADR rejected the Repository pattern for "two abstraction layers over the database with no practical benefit" — reasoning that applies almost verbatim to MediatR-as-dispatcher. ADR-0017's stated philosophy is "add indirection only when the concrete pain exists"; MediatR is indirection-first.
- **Full CQRS is overkill for this domain.** Ceres is a single-user, single-entry finance tracker on one Postgres instance with derived values computed on read by design (ADR-0024). Fowler and Microsoft both reserve CQRS for complex domains or read/write-scaling asymmetry, applied to *portions* of a system, never system-wide. None of those pressures exist here.
- **It would fight the existing guardrails.** CER007 and the architecture tests key on the controller→service seam; MediatR re-points everything at a dispatcher seam (pure churn, no isolation/security benefit — RLS lives at the DbContext layer regardless) and adds a second DI-scanning mechanism colliding with the Stage 13.a Scrutor convention, plus more `DiCompletenessCheck` surface.

## Decision

**Do not adopt CQRS, and do not adopt MediatR.** Keep the Controller → Service → DbContext pattern (ADR-0017).

Two cleanups the investigation surfaced are tracked independently of this decision (they align with existing ADRs rather than overturning one):

1. **Finish the CER007 migration** — pull the ~14 read-path `AppDbContext` injections behind service query methods, then raise `CER007` to warning/error. This gives the "reads and writes through one seam" discipline that CQRS is a heavyweight proxy for. (Already documented as known debt in `architecture.md` § Why controllers must not call DbContext.)
2. **Reconcile the error-signalling convention** — the codebase has drifted to three forms (`Result<T>`, thrown `InvalidOperationException`, and a result enum like `CloseTicketResult`) against ADR-0025's single chosen approach. Pick one, write it down, migrate. This is the one real smell the investigation found; MediatR would have papered over it.

## Consequences

**Positive:**
- No new dependency, no commercial license, no test-harness or DI-scan rework.
- Stays idiomatic and proportional to a solo-maintained CRUD app; consistent with ADR-0017.
- The learning value of CQRS/MediatR, if wanted, is available via a throwaway spike *outside* this repo.

**Negative / revisit trigger:**
- If Ceres later grows a genuinely complex read side (heavy cross-user analytics with denormalized projections, or a read replica), revisit **CQRS-on-that-portion-only** — never system-wide, and still without MediatR (plain query services suffice). That is a Phase 4+ "if the pain appears" trigger, consistent with ADR-0017's deferral philosophy.

Sources: [Martin Fowler — CQRS](https://martinfowler.com/bliki/CQRS.html); [Microsoft .NET — CQRS in eShopOnContainers](https://learn.microsoft.com/en-us/dotnet/architecture/microservices/microservice-ddd-cqrs-patterns/eshoponcontainers-cqrs-ddd-microservice); [Milan Jovanović — MediatR and MassTransit Going Commercial](https://www.milanjovanovic.tech/blog/mediatr-and-masstransit-going-commercial-what-this-means-for-you).
