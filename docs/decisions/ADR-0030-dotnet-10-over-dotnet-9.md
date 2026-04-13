# ADR-0030 — .NET 10 over .NET 9

**Status:** Accepted
**Date:** 2026-04-11

## Context

The project was initially scaffolded targeting .NET 9 (the SDK available on the development machine). .NET 9 is a Standard Term Support (STS) release with an end-of-life date of May 2026 — the same month as the start of active development. Building on a runtime with one month of remaining support is not viable.

## Decision

Target .NET 10 (LTS). The .NET 10 SDK was installed via Homebrew (`brew install dotnet@10`) and both `ProjectCeres` and `ProjectCeres.Tests` were updated to `net10.0`. All NuGet packages were upgraded to their .NET 10-compatible versions (Npgsql.EntityFrameworkCore.PostgreSQL 10.0.1, Microsoft.EntityFrameworkCore.Design 10.0.5).

## Consequences

- .NET 10 is supported until November 2028, covering the full planned development lifecycle.
- Latest package versions (Npgsql 10.x, EF Core 10.x) are compatible without version pinning workarounds.
- The PATH entry in `~/.zshrc` points to `/opt/homebrew/opt/dotnet/bin` (the unversioned Homebrew formula for .NET 10).
