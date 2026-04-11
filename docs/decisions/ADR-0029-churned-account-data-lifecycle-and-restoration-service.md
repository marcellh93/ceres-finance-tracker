# ADR-0029 — Churned Account Data Lifecycle and Paid Restoration Service (Phase 3+)

**Status:** Accepted

**Context:**

When a user leaves the platform, two distinct scenarios exist with different legal and product consequences:

1. **Natural churn** — the user stops using the service or cancels their subscription without explicitly requesting data deletion. No erasure obligation is triggered. The platform may retain data under a disclosed retention policy.
2. **Explicit GDPR erasure request** (Art. 17) — the user formally requests deletion of their personal data. The platform must comply. Financial records subject to legal retention (Código de Comercio, 6 years) are retained in anonymised form; all personal identifiers are removed. No archive for restoration is possible after this path.

These two scenarios must be tracked separately at account closure time. Mixing them — for example, keeping a restoration archive for a user who requested erasure — is a GDPR violation.

A third scenario also needs handling: a user who churned naturally and later wants to return. If their data was archived, restoring it from archive involves manual retrieval effort. This creates a legitimate cost basis for a paid restoration service.

## Decision

### Data Lifecycle for Natural Churn

```
Account closed (natural churn)
        │
        ▼
0–30 days: Grace period
  - Account marked IsActive = false
  - Data remains fully intact in the live database
  - Reactivation is free and instant — no data loss
        │
        ▼
30 days: Active data removed, archive created
  - All user data is exported into a sealed archive file (compressed, on filesystem)
  - Live database records are purged (except legally retained financial records,
    which are anonymised in place per the GDPR erasure model)
  - CustomerArchive row is written (see schema below)
        │
        ▼
30–180 days: Archive period
  - Data exists only as the sealed archive file
  - Restoration is available but has a cost (see Restoration Service below)
        │
        ▼
180 days: Permanent deletion
  - Archive file is deleted
  - CustomerArchive row is updated (ArchivedFileDeletedAt stamped)
  - No restoration possible after this point
```

These windows (30 days grace, 180 days archive) must be disclosed in the Privacy Policy before Phase 3 launch. They may be adjusted by policy without a schema change.

### Data Lifecycle for Explicit GDPR Erasure Request

```
Erasure request received
        │
        ▼
Personal identifiers anonymised immediately
  - Name, email, and any other direct identifiers replaced with anonymous tokens
  - Financial records retained in anonymised form for legal period (6 years)
  - All non-financial personal data purged
        │
        ▼
No archive created — restoration is not possible
  - ClosureType = GdprErasure is recorded on CustomerArchive (no file path)
  - If this user returns, they start from zero
```

### CustomerArchive Schema

```
CustomerArchive
  Id                        (UUID)
  UserId                    (UUID — references the now-inactive user record)
  ClosureType               (enum: NaturalChurn | GdprErasure)
  ClosedAt                  (UTC timestamp)
  GracePeriodEndsAt         (UTC — 30 days after ClosedAt; null for GdprErasure)
  PermanentDeletionScheduledAt (UTC — 180 days after ClosedAt; null for GdprErasure)
  ArchiveFilePath           (filesystem path; null for GdprErasure or after deletion)
  ArchivedFileDeletedAt     (UTC — stamped when the file is permanently deleted)
  RestorationFee            (decimal, nullable — set at archive creation time by policy)
  RestoredAt                (UTC — stamped if the user returns and restoration completes)
```

### Restoration Service (Phase 5)

When a churned user returns and their archive still exists, restoring their data is a paid service. The cost covers the retrieval and re-import effort. This is distinct from the free GDPR data portability export — the restoration service re-activates a full working account, not just a data file.

- The fee is set at archive creation time (`RestorationFee`) based on the policy active at that moment
- Payment is collected before restoration begins
- Restoration is performed by re-importing the archive into a new account record
- Once restored, the archive file is retained until `PermanentDeletionScheduledAt` then deleted on schedule
- If restoration is requested after `PermanentDeletionScheduledAt`, it is not possible — the user is informed at sign-up

The restoration service is a Phase 5 feature. It must not be built before Phase 5.

### What Is Not Permitted

- Keeping a restoration archive for a user who submitted a GDPR erasure request
- Charging for a standard GDPR Right of Access or Right to Portability export — those are legally free
- Restoring data past the `PermanentDeletionScheduledAt` date

## Consequences

**Positive:**
- The two closure paths are legally distinct and enforced at the schema level via `ClosureType` — no accidental GDPR violation from treating erasure requests as natural churn
- Grace period prevents accidental permanent loss from impulsive cancellations
- Restoration service creates a legitimate revenue opportunity without compromising compliance
- Archive lifecycle is fully deterministic — scheduled deletion is a background job keyed on `PermanentDeletionScheduledAt`

**Negative:**
- Archive files on the filesystem require a reliable background job and monitoring — a failed deletion job leaves data retained past the disclosed window, which is itself a compliance issue
- The restoration service (Phase 5) requires import tooling that mirrors the original data model at the time of export — schema migrations between archive time and restoration time must be handled

**Related decisions:**
- ADR-0023: soft delete (deactivation) applies during the grace period
- ADR-0028: GDPR erasure flow is triggered via the admin dashboard under audit
- `docs/legal.md`: Data Retention Policy section governs the windows used here; Privacy Policy must disclose them before Phase 3
