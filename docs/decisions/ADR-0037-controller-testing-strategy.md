# ADR 0037: Controller Testing Strategy

## Status: Accepted

## Context

Phase 1 left controller actions untested — documented as a known gap in `testing.md`.
The rationale was that controllers are thin HTTP handlers that delegate entirely to
services, which are already integration tested. Testing the controller layer would mostly
be testing that ASP.NET Core works, not that application code works.

Phase 2 changes the picture in two ways:

1. **API controllers in `Controllers/Api/`** have an explicit contract to protect: routing
   conventions, HTTP status codes, response shape, and error format (per `api-contract.md`
   and ADR-0035). Getting any of these wrong is a real bug that service-layer tests cannot
   catch.

2. **CSV import** involves file upload handling and a multi-step result. The question is
   whether the controller or the service is the right test boundary for this feature.

Three options were considered:

- Test all controllers (Razor + API) — too broad; Razor controllers are temporary
- Test API controllers only — proportional to risk and lifespan
- Test no controllers — leaves the API contract unprotected

## Decision

**API controllers in `Controllers/Api/` are integration tested using
`WebApplicationFactory<Program>` with `HttpClient`. Razor controllers remain untested.**

### API controllers — tested

`WebApplicationFactory<Program>` boots the full app in memory. Tests send real HTTP
requests and receive real HTTP responses. This catches:

- Routing mistakes (wrong URL, wrong HTTP method)
- Serialization issues (wrong field names, wrong types)
- Status code mistakes (returning 200 where 201 or 422 is required)
- Error shape deviations from `api-contract.md`
- `InvalidModelStateResponseFactory` producing the correct format

Every action in `Controllers/Api/` must have at least one happy-path integration test and
one test for the primary error case (validation failure or not found).

### Razor controllers — untested (accepted gap)

Razor controllers are thin glue code with a defined end-of-life in Phase 3. Writing
integration tests for controllers that will be deleted is wasted effort. The service layer
tests already cover the business logic that survives into Phase 3. This is an accepted gap,
not an oversight.

### CSV import — tested at the service level

CSV import involves file parsing, sign-flipping, category inference, and multi-row writes.
The import controller action is a Razor controller (multipart form submission from the
browser) — it falls under the accepted gap above.

`ImportService` is unit tested in isolation: parsing logic, sign convention, category
inference, and error accumulation are all exercised with in-memory CSV strings. The
full import path is covered by an integration test against the test database.
See ADR-0038 for the `ImportService` test approach.

### Test project structure addition

```
ProjectCeres.Tests/
  Integration/
    Api/                          ← new in Phase 2
      DashboardApiControllerTests.cs
      AccountsApiControllerTests.cs
      ...
```

## Consequences

**Positive:**
- API contract is protected from day one — routing, status codes, and response shape are
  verified automatically
- No test investment in Razor controllers that will be deleted in Phase 3
- `ImportService` logic is testable independently of HTTP infrastructure
- Clear rule: if it's in `Controllers/Api/`, it gets a test; if it's a Razor controller,
  it does not

**Negative:**
- `WebApplicationFactory<Program>` tests are slower than unit tests — they boot the full
  app and hit a real database. Acceptable given the value they provide for the API contract.
- The gap in Razor controller coverage remains through Phase 2. Acknowledged and documented.
