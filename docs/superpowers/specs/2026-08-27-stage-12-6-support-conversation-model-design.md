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
  ExternalRef   string?               // NEW — the provider-agnostic seam, null today
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
- **Attachments move to `SupportMessageId`.** The 12.5 composite-FK protection carries forward,
  re-pointed: `(SupportMessageId, UserId)` against a new `SupportMessage` alternate key
  `(Id, UserId)`, same `ON DELETE CASCADE`, same reason (the Postgres RI trigger bypasses RLS,
  so a single-column FK would allow a cross-tenant destructive write).

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

`SupportTicketStateMachine` is a single **stateless `sealed` class** — no interface, no
inheritance, no `DbContext`, no request awareness, no fields. It is registered as a DI
singleton purely so it can be constructor-injected into the two services like everything else
(it holds no state, so singleton is safe). No interface, because there is nothing to fake
(it is pure — tests call it directly with real enums) and there will never be a second
implementation; the rules *are* the product decision. This follows the project's precedent for
pure domain helpers (`BudgetPeriod` is a plain class, no interface).

```csharp
public sealed class SupportTicketStateMachine
{
    public TransitionResult ResolveUserReply(SupportTicketStatus current);
    public TransitionResult ResolveOperatorAction(SupportTicketStatus current, SupportTicketStatus chosen);
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

### Operator surface — new, under `Admin/`, `[RequireAdmin]`, admin BYPASSRLS context

| Endpoint | Behaviour |
|---|---|
| `POST /api/admin/support/tickets/{id}/messages` | The operator reply **and** status set, in one call. Body carries the message text (may be empty) + the chosen status. Empty text + a status = the silent OnHold / stale-Close case. Writes a `SupportMessage` with `AuthorRole=Agent`, stamped with the **ticket owner's** `UserId`, and applies the chosen transition through the state machine. |

That is the entire operator surface this stage. No inbox, no list, no assignment. The operator
reaches a ticket by the id in their notification email until the § 12.5.2 admin list ships.

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

**Recipient-lock consequence** (flagged so the security review is not surprised): notifying the
user means composing an email `To:` the user's own verified address, which uses
`FromVerifiedUser` — the existing, sanctioned factory for exactly that. No new hole, but the
notification service now uses **both** recipient factories. The whole-tree recipient-lock
architecture test gains one call site, and the `security-model.md` factory table stays accurate.

Two new `EmailTemplateKey` values — `SupportReplyToUser`, `SupportTicketSolved` — each with the
EN/ES resx triple and the `EmailComposer` switch arm.

**Carried forward from 12.5:** the rate limit (now also on `POST .../messages`, since it sends
mail), and the per-user storage quota (unchanged — it already sums across attachment families).

---

## Section 4 — Migration, testing, carry-forward

### Migration — one forward migration, clean cutover (no dual-read)

The feature is pre-launch beta; the dev DB holds test tickets but no real uploaded files
(user-confirmed), so the attachment repoint is low-risk.

1. Create the `SupportMessages` table **and** its `user_isolation` RLS policy in the same
   migration (the entity+policy pairing rule; `RlsParityStartupCheck` / `ParityTests` fail
   until it lands).
2. Add `ExternalRef` (nullable) to `SupportTickets`. Remap status values:
   `Open→Open`, `InProgress→Open`, `Resolved→Solved`, `Closed→Closed`.
3. **Data move:** for each existing ticket, insert one `SupportMessage`
   (`AuthorRole=User`, `Body`=old `Message`, `UserId`=ticket owner,
   `CreatedAt`=ticket's `CreatedAt`).
4. **Repoint attachments:** `SupportTicketId` → the new first message's `SupportMessageId`;
   swap the composite FK to `(SupportMessageId, UserId)` against `SupportMessage`'s alternate
   key `(Id, UserId)`.
5. Drop `SupportTicket.Message`.

Steps 3–4 are raw SQL inside the migration with a pre-flight guard (the shape used by
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

## Out of scope (YAGNI — keeps the stage shippable)

- Admin ticket-list / inbox UI (roadmap § 12.5.2).
- Assignment, triage, agent identity beyond the `User | Agent` role.
- The actual Zendesk adapter — only the `ExternalRef` seam and status-mapping shape ship.
- Inbound email parsing (operator-replies-by-email).
- Digest / batched notifications (no background-job scheduler exists).
- Rich-text or Markdown message bodies — plain text this stage.

## Cross-references

- Supersedes: `2026-06-30-stage-12-sessions-support-spa-design.md` § Commit 4.
- Roadmap: `docs/roadmap-phase-three.md` → new Stage 12.6.
- `docs/models.md` § SupportTicket / SupportTicketAttachment (updated by the cutover).
- `docs/security-model.md` § Email Security Rules (recipient-lock factory table gains a row).
- `docs/api-contract.md` SupportTickets row (gains the message endpoints).
