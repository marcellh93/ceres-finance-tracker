# Stage 12.6 — Support Conversation Model

**Status:** Design approved 2026-08-27. Ready for implementation plan.

**Supersedes:** the single-message support ticket shipped in Stage 12.5 Tasks 11–12
(`docs/superpowers/specs/2026-06-30-stage-12-sessions-support-spa-design.md` § Commit 4).
That work built ticket creation, an API, message-less attachments, notification, rate
limiting, and a per-user quota — but no conversation. A support ticket without
back-and-forth is a suggestion box, not support. This stage replaces the data shape while
relocating (not redoing) 12.5's security work.

**Roadmap slot:** new top-level Stage 12.6, between Stage 12.5 (deferred items) and Stage 13
(GDPR baseline). The `/support` SPA page — deferred out of 12.5's Task 13 because it could
not be built well on the conversation-less model — ships here.

---

## Goal

A user files a support ticket and holds a real conversation with the operator: they write,
the operator replies, the ticket's status tracks whose turn it is, and either side sees the
full thread. Built in-house, shaped so a future Zendesk handoff is an adapter rather than a
rewrite. Solo-operator beta: the operator answers through a minimal admin endpoint, not a
console.

## Locked decisions (settled with the user during brainstorm, 2026-08-27)

These are constraints, not open questions. The design below satisfies them.

1. **A ticket owns an ordered conversation of messages**, user- and agent-authored, not a
   single `Message` field. `SupportTicket.Message` becomes the first `SupportMessage`.
   Attachments hang off a **message**, not the ticket.
2. **Build our own, provider-agnostic.** A nullable `ExternalRef` and a status-mapping seam
   keep a Zendesk adapter a later addition. No Zendesk integration in this stage.
3. **Solo-operator beta.** Agent messages are stored in our DB; the operator-reply surface
   is one `[RequireAdmin]` endpoint reusing the existing admin path, not an agent console.
   The admin ticket-list UI stays deferred (roadmap § 12.5.2).
4. **Five statuses:** `Open`, `Pending`, `OnHold`, `Solved`, `Closed` (stored as int; do not
   reorder — the ordinals are the database contract).
5. **The follow-up chain (`PrecedingTicketId`) is kept** and is orthogonal to conversations:
   a Closed ticket that held a full conversation can still spawn a follow-up.
6. **Thread presentation:** a slide-in sheet whose open state is reflected in the URL
   (`/support/<id>` opens that thread in the sheet on load). Not inline-in-list (too crowded
   for a conversation), not a bare modal (no URL to deep-link, and the design system flags
   "modal as first thought").

---

## Section 1 — Data model

### `SupportTicket` (modified)

```
SupportTicket : IUserOwned
  Id            Guid
  UserId        Guid
  Subject       string
  Status        SupportTicketStatus   // now 5 values (was 4)
  Priority      SupportTicketPriority // unchanged
  PrecedingTicketId  Guid?            // unchanged — the follow-up chain
  ExternalRef   string?               // NEW — a correlation-id column, null today (see § ExternalRef)
  CreatedAt     DateTime
  UpdatedAt     DateTime
  Messages      ICollection<SupportMessage>   // NEW — replaces Message + Attachments
```

Removed: `Message` (moves to the first `SupportMessage`) and the `Attachments` navigation
(moves to `SupportMessage`).

### `SupportMessage` (new)

```
SupportMessage : IUserOwned
  Id             Guid
  UserId         Guid                 // the TICKET OWNER, for every message incl. Agent
  SupportTicketId Guid
  AuthorRole     SupportMessageAuthor // User | Agent
  Body           string
  CreatedAt      DateTime
  Attachments    ICollection<SupportTicketAttachment>
```

Three deliberate choices:

- **`AuthorRole` is an enum, not an agent user-id.** In a solo-operator beta, storing *which*
  agent is premature and would couple the message to the admin-identity model. `User | Agent`
  is enough now; an `AuthorUserId Guid?` can be added later without a reshape. Stored as int;
  ordinals are the contract.
- **`SupportMessage.UserId` is the ticket owner, even for `Agent` messages.** This is the
  load-bearing isolation decision. The message is `IUserOwned` and lives under the *owner's*
  RLS scope, so the user reads the whole thread including agent replies. The operator writes
  an agent message through the admin (BYPASSRLS) path but stamps it with the owner's id, so it
  sits inside the owner's tenant. `AuthorRole=Agent` — not ownership — is what marks it as the
  operator's.

  The isolation claim above is only true if two invariants hold; both reviews found the spec
  *asserted* them without pinning them, so they are now requirements (see § Security invariants):
  the owner id is taken from the **loaded ticket row, never the request**, and **both** FK hops
  are composite.

- **BOTH foreign keys are composite, not just the attachment hop.** `SupportMessage` sits
  *between* ticket and attachment, so both hops cross an `IUserOwned` boundary and both need the
  owner-carrying composite FK — the same pattern `TransactionAttachment` / `TransferAttachment`
  use (`AppDbContext.cs` ~541–553). The earlier draft specified only the attachment hop; a
  single-column `SupportMessage.SupportTicketId` FK would let an agent message reference a ticket
  owned by a different user, and the RI-trigger cascade (which RLS does not touch) would cross
  tenants — the identical hole 12.5 fixed one level down.

  | Hop | Foreign key | Principal key | On delete |
  |---|---|---|---|
  | message → ticket | `(SupportTicketId, UserId)` | `SupportTicket(Id, UserId)` | Cascade |
  | attachment → message | `(SupportMessageId, UserId)` | `SupportMessage(Id, UserId)` | Cascade |

  New alternate key: `SupportMessage.HasAlternateKey((Id, UserId))`. `SupportTicket` already has
  `(Id, UserId)` (keep it). Because `SupportMessage.UserId` is copied straight from the ticket,
  the `(…, UserId)` pair always matches — so the cross-owner reference is structurally
  unrepresentable. Required negative test (against the real DB, as 12.5 did): insert a
  `SupportMessage` referencing a ticket owned by a different user → the composite FK rejects it;
  delete the owner's ticket → no other tenant's row is touched by the cascade.

### `SupportTicketAttachment` (modified)

`SupportTicketId` → `SupportMessageId`; the `SupportTicket` navigation → `SupportMessage`.
The composite FK re-targets `SupportMessage`'s alternate key. Otherwise unchanged (still
`IUserOwned`, still magic-byte validated, still filesystem-stored).

### Status vocabulary

```
SupportTicketStatus
  Open       = 0   filed / ball with the operator
  Pending    = 1   waiting on the USER to reply
  OnHold     = 2   blocked on a third party or an in-progress fix
  Solved     = 3   operator believes resolved; a user reply reopens to Open
  Closed     = 4   final; no reopen (a follow-up is a new ticket)
```

The 12.5 enum was `Open=0, InProgress=1, Resolved=2, Closed=3`. The migration remaps values
(§ Section 4). Because the ordinals change meaning, the migration rewrites stored values; it
does not merely widen the enum.

---

## Section 2 — The status state machine

Status is a **stored column** on `SupportTicket`, written only by explicit transitions.
Nothing recomputes it on read. This does not violate the project's no-stored-derived-values
rule: that rule targets financial aggregates (a SUM of transactions) that drift out of sync
with their source. A state machine's *current state* is legitimately stored — that is what a
state machine is. The transitions are the only writers, so it cannot drift.

### Transition table

**User actions derive status (the user never picks one):**

| User does | From | Status becomes |
|---|---|---|
| Files a ticket | — | Open |
| Posts a reply | Pending, OnHold | Open |
| Posts a reply | Solved | Open (the reopen) |
| Posts a reply | Open | Open (unchanged) |
| Posts a reply | Closed | **blocked** — refused server-side; user files a follow-up |

**Operator actions carry an explicit status the operator chooses:**

- Every operator reply is posted *with* a status from `{Open, Pending, OnHold, Solved, Closed}`,
  defaulting to `Pending` but freely changeable in the same action.
- The operator can also change status with **no reply** — a silent OnHold while investigating,
  or Closing a stale ticket.
- `Closed` is reachable directly from any non-Closed state (spam, duplicate,
  resolved-without-confirmation).

**`Closed` is terminal** from either side. No transition leaves it. A follow-up is a new
ticket with `PrecedingTicketId` set.

### Structure

`SupportTicketStateMachine` is a **`static class` with static methods** — no interface, no
instance, no DI registration, no `DbContext`, no request awareness, no fields. The architect
review corrected the earlier draft here: it proposed a DI-singleton instance class and cited
`BudgetPeriod` as precedent, but `BudgetPeriod` (`ProjectCeres/Services/BudgetPeriod.cs:9`) is a
`public static class` — the opposite shape. A static class *is* the "nothing to fake, tests call
it directly, no second implementation" design the rules argue for, and it needs no
constructor-injection into the two services and no "singleton is safe because it is stateless"
justification (that justification only existed because the draft picked the instance form). If a
future reason to inject ever appears, promote it then; a `[Theory]` calls a static method just as
directly.

```csharp
public static class SupportTicketStateMachine
{
    public static TransitionResult ResolveUserReply(SupportTicketStatus current);
    public static TransitionResult ResolveOperatorAction(SupportTicketStatus current, SupportTicketStatus chosen);
}

public readonly record struct TransitionResult(
    bool Allowed,
    SupportTicketStatus NewStatus,  // meaningful only when Allowed
    string? Reason);                // the rejection message when not
```

The **rules live in the machine; the database writes live in the services.** A service resolves
the transition, throws `InvalidOperationException(result.Reason)` if not allowed, else writes
the message and `ticket.Status = result.NewStatus`. Both services import the identical verdict;
neither re-encodes a rule. Changing a transition is a one-method change every caller inherits.

**Two enforcement facts** (the security review will look for these):

- The user-posts-to-Closed block is enforced **server-side** in the reply endpoint, not merely
  by hiding the composer.
- An illegal operator transition is rejected by the machine, never silently written. Every
  status change goes through the one gate.

---

## Section 3 — Endpoints, services, notifications

### User surface — `SupportApiController` (`[Authorize]`, extends 12.5)

| Endpoint | Change |
|---|---|
| `POST /api/support/tickets` | Creates the ticket **and its first `SupportMessage`** (Subject + Body + optional attachments) in one transaction. Status → Open. `201` + `{id}` + `Location`. |
| `GET /api/support/tickets` | List. Gains `messageCount` and `lastMessageAt` (a thread wants "3 messages, last reply 4h ago"). Replaces the `attachmentCount` idea from the 12.5 follow-up — attachment count belongs on the message now. |
| `GET /api/support/tickets/{id}` | Returns the **full ordered thread**: every message with `authorRole`, `body`, `createdAt`, and that message's attachments. This is what the sheet reads. IDOR → 404. |
| `POST /api/support/tickets/{id}/messages` | **New.** The user reply. Goes through the state machine; refused server-side on a Closed ticket (`422`). Optional attachments ride the message. |
| `POST /api/support/tickets/{id}/close` | Kept — the user closing their own ticket. `204` / `404` unreachable / `422 TICKET_ALREADY_CLOSED`. |
| `GET /api/attachments/support/{id}` | Download. Unchanged shape; the owner-scope check now walks message → ticket → owner. |

Attachment upload: the plan settles whether attachments ride the message-create multipart
payload or a `POST .../messages/{messageId}/attachments` sub-route, against the actual
`FileAttachmentService` shape. Either way the file attaches to a **message**.

### Operator surface — new, under the `ProjectCeres/Admin/` **namespace**, `[RequireAdmin]`

| Endpoint | Behaviour |
|---|---|
| `POST /api/admin/support/tickets/{id}/messages` | The operator reply **and/or** status set, in one call (see § Empty body below). Writes a `SupportMessage` with `AuthorRole=Agent`, stamped with the ticket owner's `UserId` **taken from the loaded ticket row**, and applies the chosen transition through the state machine. |

That is the entire operator surface this stage. No inbox, no list, no assignment. The operator
reaches a ticket by the id in their notification email until the § 12.5.2 admin list ships.

**The endpoint's mechanics are non-obvious and both reviews flagged them — the plan MUST spell
these out, because there is no existing cross-user-write template under `Admin/` to copy (this is
the first one):**

1. **Location.** The controller lives under the `ProjectCeres/Admin/` **namespace**, not merely
   an `api/admin/*` route. The cross-tenant-write allow-list (ADR-0065,
   `ArchitectureTests.Admin_namespace_is_allow_listed_for_IgnoreQueryFilters`) keys on the
   namespace; a file in `Controllers/Api/` with an `api/admin` route would trip that test.
2. **Read via `AdminDbContext` + `IgnoreQueryFilters()`.** Load the ticket by `{id}`; the global
   query filter would otherwise scope the read to the *admin's* own id and return zero rows.
   **404 if not found** (matching the user surface and the IDOR 404-not-403 rule). This is the
   *not* the `AdminUsersApiController` pattern — that one uses `UserManager` against Identity
   tables and never writes an `IUserOwned` row into a tenant. The right precedent is
   `AdminDbContext` + `[RequiresAdminContext]`, pinned by `AdminContextDisciplineTests`.
3. **Stamp `message.UserId = ticket.UserId` EXPLICITLY** before `SaveChanges`. This is the
   load-bearing line. `UserOwnershipInterceptor` (`UserOwnershipInterceptor.cs:38–41`) auto-stamps
   the *current* user on any Added `IUserOwned` whose `UserId` is still `default` — and on an
   operator request the current user is the **admin**. Leave it unset and the message lands in the
   admin's tenant, invisible to the user and unattachable to the owner's ticket via the composite
   FK. The owner id comes from the ticket row (step 2), **never from the request** — a
   request-supplied owner is exactly the cross-tenant write the composite FK exists to stop, and
   under BYPASSRLS the `WITH CHECK` policy will not catch it.
4. **Write via `AdminDbContext` (BYPASSRLS).** On the normal RLS connection the `WITH CHECK`
   pins the inserted `UserId` to the admin's GUC and rejects the owner-stamped insert (42501).
5. **Validate the transition** through `SupportTicketStateMachine.ResolveOperatorAction` before
   writing; reject an illegal transition (Closed is terminal).
6. **Audit.** An operator reply is an admin mutation of another user's data. It writes an
   `AdminAuditLog` row (actor = admin, target = owner; append-only, no delete endpoint — this is
   the record that survives a Stage-13 erasure of the user) AND adds an owner-scoped
   `AuditLogAction.SupportMessageByAgent` so the owner's own trail shows it. The new
   `AuditLogAction` value lands in the same commit as
   `AuditLogAction_enum_values_match_documented_set`, or that test goes red.

**Empty body.** The endpoint conflates "reply" and "status-only change". Resolve in the plan
(both reviews, M2): a status-only change (silent OnHold, stale-Close) creates **no message row** —
it is a status transition with no `SupportMessage`. A reply requires a non-empty `Body`. This
avoids an empty-`Body` row fighting the `[Required, MinLength(1)]` shape `SupportTicket.Message`
carries today.

### Notifications — `SupportNotificationService` (extracted from the controller)

Now invoked from three call sites (create, user-reply, operator-reply), so the logic moves out
of the controller into its own service — same "one purpose, one place" reasoning as the state
machine.

| Event | Emailed | Recipient factory |
|---|---|---|
| User files ticket | operator | `EmailRecipient.ForConfiguredSupportAddress` (exists) |
| User replies | operator | `ForConfiguredSupportAddress` |
| Operator replies | **user** | `EmailRecipient.FromVerifiedUser` (exists; server-resolves the owner's email) |
| Operator sets Solved | user | `FromVerifiedUser` |
| Operator sets OnHold | nobody | — |

**Recipient-lock: sound, but two separate rules apply.** The *recipient* mechanics are fine —
`FromVerifiedUser` is the sanctioned factory for the user's own verified address, and adding it as
a second call site correctly requires extending the whole-tree recipient-lock architecture test's
allow-list and the `security-model.md` factory table. That covers the Layer-2 *recipient* lock.

The **content** rule is separate and the security review (H2) flagged the spec was silent on it.
The `SupportReplyToUser` email **includes the operator's reply text** (user decision, 2026-08-27:
a Zendesk/Intercom-style reply email, not notification-only). Because agent-authored free text now
renders into an email delivered to the user, it MUST be sanitised/escaped into the template the
same way user content is (security-model.md § Email Security Layer 2, "sanitize all
user-controlled content"). Required negative test: an agent body containing HTML / template-
breaking characters asserts escaped output. The reply email also carries a link to
`/support/<id>` so the user can open the thread.

Two new `EmailTemplateKey` values — `SupportReplyToUser`, `SupportTicketSolved` — each with the
EN/ES resx triple and the `EmailComposer` switch arm.

**Carried forward from 12.5:** the rate limit and the per-user storage quota. `POST .../messages`
(user reply) sends mail, so it carries `[EnableRateLimiting(EmailByUser)]` + `[ApplyEmailIpRateLimit]`.
Both reviews (security H3, architect) flag that
`ArchitectureTests.Email_triggering_endpoints_carry_a_rate_limit` hard-codes only
`(SupportApiController, "Create")` in its `mailSendingActions` array — the plan MUST add the new
user-reply and operator-reply actions to that array, or the test passes while not covering the new
mail-senders (the "green and lying" failure this spec warns about elsewhere).

---

## Section 4 — Migration, testing, carry-forward

### Migration — one forward migration, clean cutover (no dual-read)

The feature is pre-launch beta; the dev DB holds test tickets but no real uploaded files
(user-confirmed), so the attachment repoint is low-risk.

Order matters — run the whole migration in **one transaction** so a mid-migration failure cannot
strand attachments pointing at a dropped column.

1. Create the `SupportMessages` table **with its `(Id, UserId)` alternate key** and its
   `user_isolation` RLS policy in the same migration (the entity+policy pairing rule;
   `RlsParityStartupCheck` / `ParityTests` fail until it lands).
2. Add `ExternalRef` (nullable) to `SupportTickets`.
3. **Remap status as an explicit value map, not a name map — this is a data-corruption trap.**
   The statuses are stored *ints* and the ordinals changed meaning (old `Closed=3`; new `Solved=3`,
   `Closed=4`). A `SET status = status + 1` or a name-only rewrite collides old `Closed=3` into the
   new `Solved=3` slot. The correct rewrite is a single `CASE`:

   | Old (name = int) | New (name = int) |
   |---|---|
   | `Open = 0` | `Open = 0` |
   | `InProgress = 1` | `Open = 0` |
   | `Resolved = 2` | `Solved = 3` |
   | `Closed = 3` | `Closed = 4` |

   With a pre-flight guard that aborts readably if an unexpected stored value is present.
4. **Data move:** for each existing ticket, insert one `SupportMessage`
   (`AuthorRole=User`, `Body`=old `Message`, `UserId`=ticket owner,
   `CreatedAt`=ticket's `CreatedAt`).
5. **Repoint attachments:** `SupportTicketId` → the new first message's `SupportMessageId`;
   swap the attachment composite FK to `(SupportMessageId, UserId)` → `SupportMessage(Id, UserId)`,
   and add the message→ticket composite FK `(SupportTicketId, UserId)` → `SupportTicket(Id, UserId)`.
   Because each message's `UserId` is copied from its ticket (step 4), the `(…, UserId)` pairs are
   guaranteed to match, so the repoint cannot create a cross-owner row.
6. Drop `SupportTicket.Message`.

Steps 3–5 are raw SQL inside the migration with a pre-flight guard (the shape used by
`ScopeOlderAttachmentFksToOwner`: abort with a readable message rather than a bare constraint
violation if the data is already in an unexpected shape). **Any DB-destructive step is run only
after explicit user confirmation** — a standing project rule independent of this spec.

### IUserOwned registry discipline for `SupportMessage`

The five-registry checklist the `verify-stage-completeness` gate enforces: `DbSet`,
`OnModelCreating` relationship config, model-derived `UserOwnedModel.RlsTables` membership
(automatic via the interface), the RLS migration (step 1). No DI (it is an entity). The
attachment re-pointing keeps its composite-FK protection.

### Testing — four layers

**Unit (no DB):** the full state-machine transition table as a `[Theory]` — every user edge,
every operator edge, Closed-is-terminal. Milliseconds, no fixture.

**Integration (real DB):**
- create-with-first-message; the message is `AuthorRole=User`, owner-stamped, status Open.
- user reply moves status per the table; user-reply-to-Solved reopens.
- **user-reply-to-Closed is refused server-side** (not merely UI-hidden).
- operator reply carries the chosen status, stamps the owner's id, `AuthorRole=Agent`.
- the full thread reads back in order through the owner's RLS scope, **including agent
  messages** (proves the owner-stamped-agent-message decision).
- IDOR: user B cannot read or post to user A's ticket or messages; cannot download A's
  attachment.
- both notification directions fire with the correct recipient factory.
- rate limit and quota still bite on the message endpoint.

**Frontend (Vitest):** the `/support` page — list, sheet opening from a URL, composer, status
badges, the follow-up affordance on a Closed ticket, empty / loading / error states, 375px
mobile.

**E2E (Playwright):** the browser-only cross-boundary flows —
- file a ticket → it appears in the list → open it in the sheet → the first message reads back.
- **deep-link:** navigate straight to `/support/<id>` → the sheet opens on that thread on load
  (the URL-reflected behaviour; verifiable only in a browser).
- reply round-trip: post a user reply → it appears → the status badge updates.
- Solved shows a composer and posting reopens; Closed hides the composer and shows the
  follow-up affordance instead.
- 375px mobile: the sheet is full-width, tap targets ≥ 44×44px.

  *Caveat, stated honestly:* the operator side is `[RequireAdmin]` and API-driven (no UI this
  stage), so the agent-reply leg of an E2E is **API-seeded** — the test posts an agent message
  through the admin endpoint, then asserts the user's browser renders it. The full loop is not
  browser-driven because half of it has no browser surface yet.

### Test reconciliation — inventory before touching anything

The 12.5 support suites (`SupportTicketServiceTests`, `SupportApiTests`,
`SupportNotificationTests`, and the reviewer-forced additions: the follow-up chain, the quota,
the close-IDOR oracle, the audit row) assert the **old model** — single `Message`, four
statuses, attachments-on-ticket. After the cutover many will not compile or will assert deleted
behaviour.

The spec's implementation plan MUST open with a **test-reconciliation inventory**: every 12.5
support test classified, with a reason, as one of —

- **migrate** — still valid, passes as-is;
- **rewrite** — the contract intentionally changed; name the change (per `docs/testing.md`
  § Rules) and update;
- **retire** — the behaviour is genuinely deleted.

Nothing is touched until it is classified. This protects the hard-won reviewer coverage from
being silently dropped, and prevents a "green and lying" suite where a stale assertion passes
by coincidence against a field that still exists. Bulk-deleting the old files and rewriting
fresh is explicitly rejected: it loses the edge cases unless each is deliberately re-derived,
which is the inventory anyway, just less visible and less reviewable.

---

## Carried forward from Stage 12.5 (relocated, not redone)

- **Composite-FK attachment protection** — re-pointed from ticket to message.
- **`EmailRecipient` recipient-lock + `SupportRecipientResolver`** — unchanged; the notification
  service now uses a second existing factory (`FromVerifiedUser`).
- **Rate limit** — extended to the new message-post endpoint.
- **Per-user storage quota** — unchanged.
- **Follow-up chain (`PrecedingTicketId`)** — unchanged; now orthogonal to conversations.

## ExternalRef — a correlation column, not a seam

The architect review (Q5) corrected the earlier framing. `ExternalRef` is a nullable
correlation-id column, added now because a nullable column is cheap and avoids a schema
migration on a populated table later. It is **not** an architectural seam that makes a future
Zendesk integration "an adapter rather than a rewrite" — that integration still needs an
outbound sync, an inbound sync (Zendesk replies → `SupportMessage` rows), a bidirectional status
map, idempotency/conflict handling, and an adapter interface with a real second implementation.
`ExternalRef` participates in exactly one of those (the correlation id) and reshapes no control
flow. Adding the column now is the right YAGNI call; claiming it de-risks the integration is not,
because a later spec that reasons "we already have the seam" would skip the real design. Keep the
column, drop the seam claim.

## Out of scope (YAGNI — keeps the stage shippable)

- Admin ticket-list / inbox UI (roadmap § 12.5.2).
- Assignment, triage, agent identity beyond the `User | Agent` role.
- The actual Zendesk adapter (see § ExternalRef — only the correlation column ships).
- Inbound email parsing (operator-replies-by-email).
- Digest / batched notifications (no background-job scheduler exists).
- Rich-text or Markdown message bodies — plain text this stage.

## Deferred to Stage 13 (receiving-stage checkboxes required)

Both reviews found two policy questions this stage *creates* but the next stage (GDPR baseline,
`roadmap-phase-three.md` Stage 13) must answer. Per the deferral rules, each gets a `[ ]` under
Stage 13, not a silent gap:

- **Agent-message erasure/purge policy.** Owner-stamped agent messages are `IUserOwned`, so
  `UserOwnedCleanup` auto-includes `SupportMessage` and a natural-churn purge (legal.md day-180
  hard-delete) would destroy the operator's replies with the user's. `legal.md` has no carve-out
  classifying support correspondence. Stage 13 must decide: purge / anonymise-and-retain /
  retain-separately. The `AdminAuditLog` row (from the operator-endpoint audit) is the
  accountability record that survives erasure regardless.
- **Support-attachment file cleanup on delete.** The composite-FK `ON DELETE CASCADE` removes
  the attachment *row* but leaves the *file* on disk (a `models.md` known gap). This stage
  multiplies the delete surface (per-message attachments, cascade-on-message), so tie the fix to
  the Stage-13 erasure work (`FileAttachmentService` already flags GDPR file handling there).

## Cross-references

- Supersedes: `2026-06-30-stage-12-sessions-support-spa-design.md` § Commit 4.
- Roadmap: `docs/roadmap-phase-three.md` → new Stage 12.6; two `[ ]` receiving items under Stage 13.
- `docs/models.md` § SupportTicket / SupportTicketAttachment (updated by the cutover).
- `docs/security-model.md` § Email Security Rules (recipient-lock factory table gains a row).
- `docs/api-contract.md` SupportTickets row (gains the message endpoints).

## Review revisions (2026-08-27)

Dispatched `ceres-architect` and `ceres-security-reviewer` against the approved design before
writing the plan. Neither rejected the shape; both found invariants the spec asserted without
pinning. Each finding was verified against the real code before folding in.

| Finding | Source | Change |
|---|---|---|
| Owner id must come from the loaded ticket row, never the request (C1) | security | § Operator surface, step 3 — explicit, with the `UserOwnershipInterceptor` trap spelled out |
| Both FK hops must be composite, not just attachment→message (C2) | security | § Section 1 — message→ticket FK added; table + negative test |
| State machine is a `static class`, not a DI-singleton; `BudgetPeriod` is static | architect | § Structure — rewritten |
| Status remap is a value-rewrite with a collision trap (`Closed=3`→`4`) | architect | § Migration step 3 — explicit CASE map |
| Operator reply needs an audit action; it's the GDPR-surviving record | both | § Operator surface, step 6 |
| Agent reply body emailed to user must be sanitised; include-text chosen | security | § Notifications — sanitise + negative test |
| Rate-limit `mailSendingActions` array must gain the two new endpoints | both | § Notifications carry-forward |
| Empty-body operator action → no message row (status-only change) | both | § Operator surface, Empty body |
| `ExternalRef` is a correlation column, not a seam — downgrade language | architect | § ExternalRef |
| Two Stage-13 policy questions this stage creates | both | § Deferred to Stage 13 |

The owner-stamping isolation model, status-as-stored-column, and the clean-cutover migration were
all confirmed sound.
