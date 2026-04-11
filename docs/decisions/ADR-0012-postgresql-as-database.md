# ADR 0012: PostgreSQL as the Database

## Status: Accepted

## Context

The project initially planned to use Microsoft SQL Server. Before any code or migrations were
written, a review of Phase 3 hosting options revealed that SQL Server creates unnecessary
constraints:

- No managed SQL Server hosting outside Azure — self-hosting requires running SQL Server in
  Docker on a VPS with at least 4 GB RAM dedicated to the database engine alone
- The official SQL Server Docker image does not support Apple Silicon; a workaround image
  (Azure SQL Edge) is required for M-series Macs, adding friction to local development
- Effectively locks Phase 3 hosting to Azure or an oversized VPS
- Docker is not needed at all for local development if the database can be installed natively

PostgreSQL runs natively on macOS (Homebrew or Postgres.app), has a broad ecosystem of
cheap managed hosting options for Phase 3 (Supabase, Railway, Render, Neon, DigitalOcean),
and is fully supported by EF Core via the Npgsql provider.

## Decision

Use PostgreSQL as the database with the `Npgsql.EntityFrameworkCore.PostgreSQL` EF Core provider.

**Local development:** Install PostgreSQL via Homebrew (`brew install postgresql@16`) or
Postgres.app. No Docker required.

**Phase 3 hosting:** Use a managed PostgreSQL service. Provider TBD (see Open Questions in
planning.md) — options include Supabase, Railway, Render, Neon, and DigitalOcean Managed
Databases.

This decision was made before any code, migrations, or schema were written — migration cost
is zero.

## Consequences

**Positive:**
- No Docker requirement for local development — simpler setup on macOS
- Works natively on Apple Silicon with no workaround images
- Broad managed hosting options for Phase 3 at lower cost than Azure SQL
- Open source and free at all tiers
- Lighter footprint than SQL Server (~400 MB Docker image vs ~1.5 GB, if Docker is ever used)
- `date`, `decimal`/`numeric`, and other SQL types used in the schema have direct equivalents
  in PostgreSQL — no migration complexity

**Negative:**
- Switches NuGet provider from `Microsoft.EntityFrameworkCore.SqlServer` to
  `Npgsql.EntityFrameworkCore.PostgreSQL` — trivial at this stage since no code exists yet
- Any T-SQL-specific syntax would need to be replaced with standard SQL or PostgreSQL syntax
  — not applicable here as no queries are written yet
- EF Core via Npgsql maps `decimal` to `numeric` with no default precision — requires
  explicit `decimal(18,2)` annotation on all financial amount columns (see ADR-0006)
