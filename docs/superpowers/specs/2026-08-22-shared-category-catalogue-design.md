# Shared category catalogue with a per-user overlay

**Status:** Design approved, pending implementation plan
**Target:** Phase 3, three stages landing before Stage 15.5 (Onboarding wizard)
**Supersedes:** the per-user category-copy model introduced by `CategorySeedService`

## Problem

Every user gets a private copy of all 26 default categories. Registration runs
`CategorySeedService.CopyDefaultsForUserAsync`, which inserts 26 rows naming that
user as owner.

The shared test database shows what this costs at scale:

| Measure | Value |
| --- | --- |
| Category rows | 79,404 |
| Distinct names | 26 |
| Distinct `(Name, CategoryTypeId, IsSystem, IsReserved, LifestyleTag)` shapes | 26 |
| Users | 3,054 |

Twenty-six shapes stored 3,054 times. No user has ever diverged from the
template: zero deactivations, zero renames, zero lifestyle-tag changes. The
duplication carries no information.

Three consequences follow:

1. **Nothing prevents duplicates.** `Categories` has one unique index, on the
   surrogate key `Id`. There is no constraint on `(owner, name)`, and
   `CategoryService.TryCreateAsync` validates only that the category type
   exists. A user can create "Groceries" twice today.
2. **Adding a default is not possible.** A new default would have to be
   back-filled into every user's private set.
3. **The seeders can collide.** Migration `RemapSentinelToFirstUser` assigned the
   original Phase 1/2 categories to the first registered user without checking
   whether that user already had a seeded set. On the dev database the user
   registered 2026-05-16 and the migration ran afterwards, so all 26 defaults
   appeared twice in the Categories screen. Cleaned up 2026-08-22.

## Shape

Three concerns, currently conflated in one table, are separated.

**`Categories` — the catalogue.** One row per category that exists, holding the
facts true for everybody: name, type, system and reserved flags, lifestyle tag.
Gains a nullable `OwnerUserId`. `NULL` means a global row, admin-managed.
Non-null means a private row belonging to that user alone.

**`UserCategories` — the overlay.** One row per user-specific deviation from a
global category. Holds `UserId`, `CategoryId`, `IsActive`, and `NameOverride`.

**Everything else is unchanged.** `Transactions`, `RecurringTransactions`,
`CategoryBudgets` and `SavedReports` keep pointing at `Categories.Id`. Their
rows already carry `UserId`, so user scoping does not depend on the category row
being private.

### Overlay rows are sparse

A missing overlay row means "active, no override". Rows appear only when a user
deactivates or renames something.

Registration therefore writes **no** category rows at all. The alternative —
seeding 26 overlay rows per user — would store roughly 78,000 rows across the
current test population to express nothing, reproducing a smaller copy of the
problem being removed. Reads are the same either way: join the catalogue to the
overlay, keep rows where no overlay row exists or the overlay says active, and
prefer `NameOverride` over `Name` when present.

### What a user may change

| Field | Global category | Private category |
| --- | --- | --- |
| Name | Overridable, visible only to that user | Editable directly |
| Active | Per-user, visible only to that user | Editable directly |
| Lifestyle tag | **Not overridable** | Editable directly |
| Delete | Not permitted — deactivate only | Permitted when unused |

**Lifestyle tag is deliberately not overridable on global categories.** The
tag classifies spending as Needs, Wants or Savings, and the product uses that
classification to teach a budgeting discipline. Letting a user relabel a global
"TV & Entertainment" as a Need would put the product's name behind a claim it
does not support. Users keep full freedom through private categories, where any
tag is theirs to choose; the system simply does not promote it. This asymmetry
against `NameOverride` is intentional and should not be "fixed" for consistency.

A user cannot hard-delete a global category. The interface may present
deactivation as removal; the underlying row survives so history stays intact.

## Adding a global category later

New global categories apply retroactively — every user sees them. Where a user
already has a private category with a matching name (compared case-insensitively
and trimmed), the new global one arrives **deactivated for that user only**, via
an overlay row. They see no duplicate.

Those users get a dismissible prompt offering to merge their private category
into the new global one. **Merging is never automatic.** A name match does not
guarantee a meaning match: one user's "Transport" may be bicycle upkeep while the
global one means public transit. Automatic merging would silently rewrite the
meaning of that user's financial history, and it cannot be undone from the user's
side. Merging is therefore an explicit choice, shown with the number of
transactions that would move.

Merging repoints the user's `Transactions`, `RecurringTransactions`,
`CategoryBudgets` and `SavedReports` from the private row to the global one,
deletes the private row, and activates the global one for that user. It runs in
a single transaction.

## Access control

`AddIdentity<ApplicationUser, IdentityRole<Guid>>` is already registered in
`Program.cs`, and `AspNetRoles` / `AspNetUserRoles` exist in the schema. Both
tables are empty and no code checks a role today.

An `Admin` role gates catalogue writes. Reads stay open to every authenticated
user — the catalogue is shared by definition.

**Bootstrapping.** `ProjectCeres/Tools/SeedDevUser.cs` already creates a
confirmed account and prints a generated password, invoked as
`dotnet run --project ProjectCeres -- --seed-dev-user --email <addr> --generate-password`.
It gains an `--admin` flag and is renamed to reflect that it is no longer
development-only. Its environment gate changes from "Development only" to
"Development, or creating an admin when no admin exists" — so it cannot mint
additional admins in production. Shell access to the server is the practical
safeguard.

Deliberately rejected: marking the first registered account as admin. That is
the assumption in `RemapSentinelToFirstUser` that produced the duplicate
categories described above.

Once an admin exists, promoting another existing account is a control on the
admin screen. Email invitations for people without accounts are future work.

## Row visibility

`Categories` has PostgreSQL row-level security forced on. The current rule
admits a row only when its `UserId` equals the requesting user:

```
USING ("UserId" = current_setting('app.current_user_ref')::uuid)
```

A global row has no owner, so under this rule nobody would see the catalogue at
all. The rule becomes ownerless-or-mine:

```
USING ("OwnerUserId" IS NULL OR "OwnerUserId" = current_setting('app.current_user_ref')::uuid)
```

Writes stay owner-only, so no user can alter a global row through the normal
application connection. Admin catalogue writes run through the `Admin/`
namespace, which `ADR-0065` already reserves as the only place permitted to
bypass the per-user filter, backed by an architecture test. This work is the
first real code to live there and sets the precedent for later admin features.

`UserCategories` is user-owned and gets a standard owner-only policy, plus a row
in `UserOwnedTables.All` and the RLS parity test.

## Behaviour change: archiving

`CategoryPolicies.CanDeactivate` currently refuses when a category has
transactions — *"Reassign them before deactivating."* That rule protected a model
where archiving implied the row might disappear.

Under the overlay, deactivating a global category affects only one user's view.
The row survives and history keeps resolving. The transaction check therefore
**relaxes for global categories** and **stays for private ones**, where the row
really can be deleted. This is user-visible: archiving a global category with
history attached starts succeeding where it used to fail.

## Converting existing data

Applied in place. The dev database holds 26 rows with fixed seed identifiers and
3 genuinely user-created rows.

1. Add `OwnerUserId` to `Categories`, `UserCategories`, and the `Admin` role.
2. Rows whose `Id` matches the fixed seed pattern `20000000-0000-0000-0000-%`
   become global: `OwnerUserId` set to `NULL`.
3. All other rows become private: `OwnerUserId` set to the current `UserId`.
4. Drop the old `UserId` column from `Categories`.
5. Replace the row-visibility rule.

Transactions keep working throughout, because the rows they reference are
exactly the ones that become global — the Phase 1/2 originals their history was
always attached to. Nothing is repointed.

A `pg_dump` precedes the migration. The migration asserts afterwards that every
`Transactions.CategoryId` still resolves, and rolls back if not.

## Stages

| Stage | Scope | Risk |
| --- | --- | --- |
| 1 — Admin capability | `Admin` role, bootstrap flag on the seed tool, admin check, `Admin/` namespace boundary established, promote-existing-account control | Low |
| 2 — Category model | `OwnerUserId`, `UserCategories`, in-place conversion, RLS rewrite, read-path changes across 17 call sites in 5 files, archive-rule relaxation | **Highest** |
| 3 — Catalogue management | Admin screen for global categories, collision detection, merge prompt and merge execution | Medium |

Landing as Stages 15.6–15.8, after the onboarding wizard. Onboarding does not list
categories — its steps are preferences, accounts and opening balance — so it is
unaffected by the catalogue change beyond using the global "Opening Balance" row,
which survives the conversion unchanged.

## Testing

Per `docs/testing.md`, tests are written before the code they cover.

- Reads resolve a global category with no overlay row; with an inactive overlay
  row; with a name override. Two users' overrides never leak into each other.
- A user cannot write to a global row through the application connection.
- A non-admin is refused on every catalogue write path.
- Collision detection matches case-insensitively and ignoring surrounding spaces.
- Merge repoints all four referencing tables and is atomic under failure.
- Archiving a global category with transactions succeeds; deleting a private
  category with transactions still fails.
- The conversion migration leaves every `Transactions.CategoryId` resolvable.
- `UserCategories` appears in the RLS parity test.

## Out of scope

- Email invitations for accounts that do not exist yet.
- Admin dashboards, user management beyond promotion, usage statistics.
- Any shared-workspace or multi-account-per-organisation tenancy model.
- Uniqueness constraints on `Accounts`, `Budgets`, `RecurringTransactions`,
  which have the same missing-constraint gap and are not addressed here.
