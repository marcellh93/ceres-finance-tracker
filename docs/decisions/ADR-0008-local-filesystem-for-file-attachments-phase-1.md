# ADR 0008: Local Filesystem for File Attachments in Phase 1 and 2

## Status: Accepted

## Context
Transactions can have one or more file attachments (receipts, invoices). The files need to
be stored somewhere. The options are:

1. **Database BLOBs:** Store file bytes in SQL Server. Simple, atomic with the record, but
   bloats the database, slows backups, and degrades query performance on unrelated columns.

2. **Local filesystem:** Store files on disk, store only the path in the database. Fast,
   cheap, no database bloat. Works well for a single-machine local app.

3. **Cloud object storage (Azure Blob, S3):** Store files in a managed service. Scales
   across multiple servers, survives server replacement. Required for a properly hosted
   multi-user app. Adds cost and an external dependency.

In Phase 1 and 2 the app runs locally on one machine. Cloud storage adds cost and
complexity with no benefit at this scale. BLOBs are rejected on principle (see above).

This decision must be made before the attachment feature is built — `StoredPath` will
contain filesystem paths in Phase 1/2. If Phase 3 switches to cloud storage, every
existing `StoredPath` value will need a data migration. Deferring this decision would
guarantee a painful migration later.

## Decision
Phase 1 and 2 use the local filesystem. Files are stored under `uploads/` in the application
directory using the path convention `uploads/{transactionId}/{guid}{extension}`:
- The GUID prevents filename collisions
- The transactionId subdirectory groups a transaction's files together
- The original filename is preserved in `TransactionAttachment.FileName` for display only

Files must not be placed inside `wwwroot` and must not be served as static files. They
must only be accessible via an authenticated controller action that reads the file and
streams it back to the client.

Phase 3 will require a migration to cloud object storage. The cloud provider is TBD (see
Open Questions in planning.md). The `StoredPath` column will need to be repopulated with
cloud keys or URLs during that migration, and the upload/serve logic will be replaced.

## Consequences

**Positive:**
- Zero cost and zero external dependencies in Phase 1/2
- Simple to implement and debug locally
- Clear path convention makes files findable without querying the database

**Negative:**
- `StoredPath` values will be filesystem paths in Phase 1/2 and cloud references in Phase 3
  — a data migration is required at the Phase 3 transition
- Local files are not backed up automatically — the user is responsible for backing up the
  uploads directory alongside the database
- Files do not survive a machine replacement without a manual copy
- The serve-via-controller pattern must be implemented correctly — a mistake here (e.g.
  serving from wwwroot or not checking auth) is a security vulnerability
