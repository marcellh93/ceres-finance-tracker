# Admin namespace

Code here may query across users. Everywhere else in `ProjectCeres/` must not.

`ADR-0065` reserves this namespace as the only place permitted to call
`IgnoreQueryFilters()`, and an architecture test
(`ArchitectureTests.IgnoreQueryFilters_only_appears_in_documented_exception_paths`) fails the build if
that call appears anywhere else.

**What belongs here:** platform-wide operations that are meaningless when scoped to
one user — role administration, the global category catalogue (Stage 15.7), platform
statistics, GDPR erasure.

**What does not:** anything a normal signed-in user triggers for their own data. If it
can be scoped with `.Owned(user)`, it belongs in `Services/`.

**Rules for code in here:**

1. Every cross-user query states its scope explicitly. Either `.Where(x => x.UserId ==
   targetUserId)` for a per-user admin action, or a comment saying why an unscoped
   aggregate is correct.
2. Every entry point is gated by `[Authorize(Roles = "Admin")]`. The attribute takes a
   literal string, so the value is repeated rather than referencing `AppRoles.Admin`;
   `AppRolesTests` pins the two in sync. Being in this namespace is not itself an
   authorization check.
3. Bypassing the filter is a deliberate act. If you are reaching for
   `IgnoreQueryFilters()` to make a test pass, the query is wrong.
