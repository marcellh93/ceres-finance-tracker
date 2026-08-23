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
2. Every entry point is gated by `[RequireAdmin]` at the **class** level, never per action.
   Do not use `[Authorize(Roles = "Admin")]`: role claims are baked into the auth cookie at
   sign-in and `SessionRevocationValidator` never re-issues the principal, so a role granted
   after login stays invisible and a role revoked after login is still honoured until the
   cookie expires. `[RequireAdmin]` is backed by the `AdminLive` policy, which reads
   membership from the database on every request. Class-level because a per-action check is
   satisfiable by omission — a future action that simply forgets it is admin-only in name
   and authenticated-only in fact. Being in this namespace is not itself an authorization
   check.
3. Bypassing the filter is a deliberate act. If you are reaching for
   `IgnoreQueryFilters()` to make a test pass, the query is wrong.
