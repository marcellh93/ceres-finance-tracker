# Worked Examples — bad → good

Ten side-by-side rewrites grounded in real Ceres terminology. Use these as templates for the three-layer pattern.

---

## 1. `MapWhen`

**Bad**
> The fix uses `MapWhen` (an ASP.NET Core branch-the-pipeline primitive) to send only asset-file requests to Vite.

**Good**
> **Container.** ASP.NET Core handles a request by passing it through an ordered list of small steps — authentication, logging, routing, etc. That ordered list is the **request pipeline**.
>
> **Term.** `MapWhen` is an instruction that says: "starting here, if the request matches some test, run a separate short list of steps just for it; otherwise leave the main list alone."
>
> **Why-here.** Without it, Vite's dev-asset code sat on the main list and answered every URL by serving its `index.html` — hijacking `/login` and `/app/`. The alternative would be to give every route a prefix like `/api/`, which is a stage-sized refactor. `MapWhen` is sprint-sized.

---

## 2. `IServiceCollection`

**Bad**
> Register the service in the DI container by adding it to `IServiceCollection`.

**Good**
> **Container.** ASP.NET Core's app builder keeps a registry of objects the app knows how to construct on demand — controllers, services, the database connection, etc. When a piece of code declares it needs one, the runtime looks up how to make it.
>
> **Term.** `IServiceCollection` is that registry. `services.AddSingleton<IFoo, Foo>()` is the line that puts an entry in: "when something asks for `IFoo`, hand it the same `Foo` instance every time."
>
> **Why-here.** We're adding the new service to this registry so any controller / job / middleware that depends on it doesn't have to construct it manually. Sprint-sized.

---

## 3. `SecurityStamp`

**Bad**
> Bump the `SecurityStamp` to invalidate all existing sessions.

**Good**
> **Container.** ASP.NET Core Identity lets one user be signed in on multiple devices at once. Each session cookie carries a snapshot of a per-user string so the server can spot stale cookies and reject them.
>
> **Term.** That per-user string is the **security stamp**. Changing it means every cookie issued with the old value becomes invalid on next request, so every device gets signed out.
>
> **Why-here.** On password change we want every other logged-in device to drop. Bumping the stamp is the one-line way; the alternative is to track and revoke each session individually, which is stage-sized work. Sprint-sized.

---

## 4. `RowVersion` / optimistic concurrency

**Bad**
> Add a `RowVersion` column for optimistic concurrency.

**Good**
> **Container.** When two requests both read the same database row and try to save edits, only one should win — otherwise we silently overwrite the other's changes. **Optimistic concurrency** is the strategy of letting both requests read freely, but rejecting the second save if the row has moved since the first read.
>
> **Term.** `RowVersion` is a column that auto-increments on every save. EF Core remembers the value it read; on save, it tells the database "only update this row if `RowVersion` is still the value I read." If someone else got there first, the database changes nothing and EF Core raises a conflict.
>
> **Why-here.** Account balances are derived, but Movement edits race when the user has two tabs open. Without this column, the second save wins silently; with it, the second save returns a "someone else changed this" error the UI can show. Sprint-sized.

---

## 5. `IgnoreQueryFilters`

**Bad**
> Use `IgnoreQueryFilters()` to bypass the global filter for this pre-auth check.

**Good**
> **Container.** Phase 3 wired EF Core to automatically attach a `WHERE UserId = currentUser` clause to every query against user-owned entities. That's our multi-user data isolation safety net.
>
> **Term.** `IgnoreQueryFilters()` is an opt-out on a single query — it tells EF Core "don't attach the user-id where-clause to this query, return rows across users." Every call site is allow-listed in `ArchitectureTests.cs` with a justification.
>
> **Why-here.** Token-verification (password reset, lockout unlock, email change) runs *before* we know which user is asking — the cookie isn't valid yet. We need to find the token row regardless of which user it belongs to. The alternative would be to thread the user-id explicitly through these flows, which is stage-sized. The allow-listed `IgnoreQueryFilters()` is sprint-sized + caught by the architecture test.

---

## 6. CSRF token

**Bad**
> The fetch wrapper attaches the CSRF token automatically.

**Good**
> **Container.** A browser will automatically send your login cookie with any request to our domain — including requests triggered by another site's JavaScript while you happen to be logged in. Without protection, attacker.com can make your browser POST to our `/transactions` endpoint and act as you.
>
> **Term.** A **CSRF token** is a short string the server hands the browser in a separate, JavaScript-readable cookie. Every state-changing request must echo it back in a header. Attacker.com can trigger your browser to send the login cookie, but it cannot read the CSRF cookie (because of the same-origin rule), so it can't supply the matching header. The server rejects the request.
>
> **Why-here.** Every POST/PUT/DELETE from the SPA needs to attach this header. The fetch wrapper does it once so every caller is automatically safe — the alternative is to remember it at every call site, which is the same kind of mistake we already saw in Phase 2. Sprint-sized.

---

## 7. `[Authorize(Policy = "X")]`

**Bad**
> Decorate the controller with `[Authorize(Policy = "AdminOnly")]`.

**Good**
> **Container.** ASP.NET Core lets you tag a controller or action with a named rule that the runtime checks before the action runs. If the rule fails, the request gets a 403.
>
> **Term.** `[Authorize(Policy = "AdminOnly")]` says "check the rule named AdminOnly before letting this action run." The rule itself is defined elsewhere — it can demand a claim, a role, a custom check, anything.
>
> **Why-here.** We want admin endpoints behind a single named rule, not behind hand-rolled `if (user.Roles.Contains(...))` checks in every action. Sprint-sized + makes the test surface trivial: one rule definition, applied N places.

---

## 8. `INotifyPropertyChanged` (hypothetical)

**Bad**
> The model implements `INotifyPropertyChanged` so the UI reacts to changes.

**Good**
> **Container.** Some UI frameworks watch an object for changes and re-render whatever depends on the changed field. To do that, the object has to *announce* when one of its properties is updated.
>
> **Term.** `INotifyPropertyChanged` is the interface that defines the announcement: an event the framework subscribes to, fired by the object whenever one of its setters changes a value.
>
> **Why-here.** Skipped — we don't use this in Ceres. Listed only as a template.

---

## 9. `AsNoTracking()`

**Bad**
> Add `AsNoTracking()` so EF doesn't track these entities.

**Good**
> **Container.** When EF Core reads a row, it normally keeps a copy in memory so it can detect changes you make to it and turn those into UPDATE statements on save. That tracking has a cost on big read-only queries.
>
> **Term.** `AsNoTracking()` tells EF "don't bother remembering these rows — I'm only reading." The query returns the data and forgets about it; you cannot edit and save these entities, which is fine when you only need them for display.
>
> **Why-here.** The dashboard reads several hundred Movement rows for the recent-activity panel and never edits any of them. Without `AsNoTracking()` we'd pay the change-tracker cost for every render. Sprint-sized perf win, no behavior change.

---

## 10. "Use Server-Side Events instead of polling"

**Bad**
> We could switch from polling to SSE for the live updates.

**Good**
> **Container.** Right now the recurring-transaction dashboard asks the server every 10 seconds "anything new?". That's **polling** — short, repeated requests. **Server-Sent Events (SSE)** is a different shape: the client opens one connection, the server holds it open and pushes new data down whenever it appears.
>
> **Term.** SSE works over plain HTTP — no separate protocol like WebSockets — and is one-way (server → client). Each "event" is a small text payload the browser receives via an `EventSource` object.
>
> **Why-here.** Polling fires a request every 10s per logged-in user; at 10k users that's 1k requests/sec doing mostly nothing. SSE drops that to 10k *idle* open connections. Pros: better real-time UX, less load. Cons: long-lived connections complicate horizontal scaling (sticky session per user) and most proxies time out idle connections in 60s, so we'd need a heartbeat. **Stage-sized** — touches the dashboard, the reverse proxy config, and the load-test plan. Not in scope for the current phase; logging this for `planning-future.md`.

---

## How to use these examples

When you're about to drop a framework term in a decision-asking reply:

1. Find the closest analogue above.
2. Use the same three-layer structure.
3. If the term isn't here, write a new entry — and add it.

The examples are templates, not gospel. The pattern (container → term → why-here, no jargon-in-glosses) is the gospel.
