# ADR-0027 — Transaction History: Fixed Default List with Date Range Filter (Phase 1), Offset Pagination (Phase 3)

**Status:** Accepted

**Context:**

The Transaction History view needs a strategy for how many records to show and how users navigate beyond the default. Three options were evaluated:

1. **Offset/page-based:** `?page=2` in the URL, SQL `OFFSET`/`LIMIT`. Simple, linkable, but slow on large datasets as the offset grows.
2. **Cursor-based (keyset):** `?after=<lastId>`, SQL `WHERE Id > lastId LIMIT N`. Fast regardless of dataset size, but no random page access and no bookmarkable page URLs.
3. **Fixed list with no pagination:** server always returns the most recent N records. Simplest to build.

For Phase 1, the app is single-user with a small personal dataset. Building pagination machinery before it is needed is premature. However, a fixed list with no way to access older records is not acceptable — users need to be able to review past transactions without a hard cutoff.

## Decision

### Phase 1

The default Transaction History view returns the **most recent 50 transactions**, ordered by date descending, then `CreatedAt` descending (ADR-0013). No pagination controls.

An **advanced search / date range filter** is available on the same view. The user can select a start date and an end date and the app returns all transactions within that range with no record cap. This covers the case where a user needs to review a specific month, quarter, or year without artificial limits.

Additional filters (account, category) can be combined with the date range filter.

This matches the experience in banking apps: a default recent list for day-to-day use, and a date picker for historical lookup.

### Phase 3

When the app is hosted and multi-user, switch to **offset-based pagination** (`?page=1&pageSize=50`). This is Option 1 — simple to implement, URLs are bookmarkable and shareable, and it fits the existing `api-contract.md` convention. Cursor-based pagination (Option 2) is only worth revisiting if Phase 3 at scale shows offset queries becoming slow.

## Consequences

**Positive:**
- Phase 1 is simple to build — one query for the default view, one filtered query for the date range search
- No pagination infrastructure needed in Phase 1
- Date range filter gives users full access to their history without a hard cap
- Phase 3 path is clear and consistent with what is already documented in `api-contract.md`

**Negative:**
- The default 50-record list may not show all of a busy month if a user has more than 50 transactions; the date range filter is the workaround
- Switching from no-pagination to offset-based pagination in Phase 3 requires a controller and view update — not a schema change, but a UI change

**Related decisions:**
- ADR-0013: default sort order is `Date DESC, CreatedAt DESC`
- `api-contract.md`: the `?page=1&pageSize=50` convention applies from Phase 3; Phase 1 uses `?from=` and `?to=` date range parameters only
