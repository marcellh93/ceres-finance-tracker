# ADR 0042: TransferAttachment Entity — In Scope for Phase 2

## Status: Accepted

## Context

Phase 1 introduced `TransactionAttachment` — file attachments on transactions (receipts,
invoices). Transfers (internal movements between accounts) have no equivalent. Users may
legitimately want to attach documents to transfers (e.g. wire transfer confirmations, bank
letters).

The question was whether `TransferAttachment` belongs in Phase 2 or is deferred.

A secondary question was whether transfer reconciliation (matching imported transfers
against manually entered ones) is needed. Transfers are internal movements between the
user's own accounts — they are not bank-originated entries and do not appear in bank CSV
exports. CSV import reconciliation (ADR-0039) therefore does not apply to transfers.

## Decision

**`TransferAttachment` is in scope for Phase 2.** It mirrors `TransactionAttachment`
exactly.

### Schema

```csharp
public class TransferAttachment
{
    public Guid Id { get; set; }
    public Guid TransferId { get; set; }
    public string FileName { get; set; } = string.Empty;
    public string StoredPath { get; set; } = string.Empty;
    public string ContentType { get; set; } = string.Empty;
    public long FileSizeBytes { get; set; }
    public DateTime UploadedAt { get; set; }

    public Transfer Transfer { get; set; } = null!;
}
```

### Deletion rule

Hard delete with confirmation prompt — same rule as `TransactionAttachment` and `Transfer`
(per ADR-0023). No soft delete, no `DeletedAt` timestamp.

### File storage

Same convention as `TransactionAttachment`: local filesystem under
`uploads/{transferId}/{guid}{extension}`. Files served via controller action, not `wwwroot`.
Original filename stored in `FileName`; system-generated path stored in `StoredPath`.

### Reconciliation

No reconciliation flow for transfers. Transfers are internal movements between the user's
own accounts — they are not present in bank CSV exports and therefore have no import
counterpart to reconcile against. Transfer reconciliation is deferred; it will be revisited
if Phase 2 daily use reveals a concrete need.

## Consequences

**Positive:**
- Closes an obvious gap — wire transfer confirmations and bank letters can be attached
- Implementation is a near-copy of `TransactionAttachment` — low effort, low risk
- Deletion rule and storage convention are consistent across all attachment types

**Negative:**
- One additional entity, migration, and service method to maintain
- Transfer reconciliation remains undesigned — if a future use case requires it, it must
  be designed from scratch
