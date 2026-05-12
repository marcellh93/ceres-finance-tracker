# ADR-0070 — CI and CD on GitHub Actions

**Status:** Accepted (Phase 3, pre-launch infrastructure)

**Date:** 2026-05-13

**Supersedes:**
- The "CI service" Open Question in `planning-phase3.md` § Open Questions (line "CI service — GitHub Actions is the leading candidate; confirm before Phase 3 launch").
- The "CD strategy" Open Question in `planning-phase3.md` § Open Questions (line "CD strategy — no pipeline designed; must define trigger, staging environment, migration step, and rollback plan"). This ADR locks the **runner**; the deploy target itself remains scoped to the still-pending hosting-platform decision.

## Context

Phase 3 ships the app to a hosted environment. Two automation surfaces need a defined runner:

1. **CI** — every PR runs `dotnet build`, `dotnet test`, `pnpm build`, and `pnpm test` against a Postgres service. Failing CI must block merge to `main`.
2. **CD** — once a deployable target is chosen (hosting Open Question), a workflow must run the production migrations and deploy the build artefact on merge to `main` (or on tag, TBD with the hosting decision).

The choice of runner is independent of the choice of host. GitHub Actions, CircleCI, GitLab CI, Buildkite, and self-hosted (Jenkins, Drone) all integrate with most modern hosts. The decision criteria here are:

- **Repository proximity.** The repo lives on GitHub. Actions ships with zero extra tooling and integrates with PR status checks, branch protection, environments, OIDC, and secrets without a second account.
- **Existing skill / template availability.** `actions/checkout`, `actions/setup-dotnet`, `pnpm/action-setup`, `services.postgres` are all first-party or well-maintained and the .NET 10 + pnpm matrix is documented.
- **Cost at beta scale.** Free tier covers the expected PR volume for the beta window. Self-hosted runners can be added later for cost control without changing the workflow files.
- **Operator footprint.** No second account, no second secret store, no second control plane.

Other candidates (CircleCI, GitLab CI mirror, self-hosted Drone, Buildkite) were considered. None offers an advantage that justifies adding a second control plane.

## Decision

**Both CI and CD run on GitHub Actions.**

The CI pipeline runs on every PR and on push to `main`:

- `actions/checkout@v4`
- `actions/setup-dotnet@v4` with .NET 10
- `pnpm/action-setup@v4` + Node 22
- `services.postgres` (Postgres 16) for the integration suite
- `dotnet restore` → `dotnet build` → `dotnet test`
- `pnpm --dir ProjectCeres.Client install` → `pnpm --dir ProjectCeres.Client tsc --noEmit` → `pnpm --dir ProjectCeres.Client build` → `pnpm --dir ProjectCeres.Client test`
- E2E suite (Playwright per ADR-0071) wired in once the hosting platform is decided.

The CD pipeline runs on push to `main` (until a tag-based release scheme is chosen). It uses **GitHub Actions environments** (with required reviewers on `production`) and **OIDC** to authenticate to the host (whatever it ends up being — Render, Fly, AWS, Azure, Hetzner-via-Coolify, etc.). The deploy steps themselves are scoped to the hosting ADR and not specified here.

Migration step: `dotnet ef database update` runs as part of the CD job, gated on a successful CI run for the same commit. Rollback plan is also scoped to the hosting ADR but the ADR-0070-mandated baseline is: every CD run leaves a database snapshot taken immediately before the migration step, retained for at least 7 days.

## Consequences

**Positive:**

- One control plane (GitHub) for code, PRs, status checks, secrets, environments, and CD. No second account, no second secret rotation, no second SSO integration.
- Branch protection on `main` can be wired directly to the CI job's status check.
- `repository_dispatch` and `workflow_dispatch` available for manual triggers without an extra tool.
- OIDC short-lived tokens for cloud auth instead of long-lived secrets.

**Negative:**

- Self-hosted runner setup (if cost or job-length pressure forces it later) is GitHub-specific work; not portable to another CI without rewriting workflow YAML.
- GitHub Actions minute pricing applies beyond the free tier. The integration suite (currently ~3 minutes for 963 tests) sets the lower bound; if the suite grows past the free tier ceiling, either trim the matrix, parallelise, or move the integration suite to a self-hosted runner.
- Coupling to GitHub. If the project ever moves repository hosts, the workflows do not port without rewrite. Acceptable lock-in given the operator-footprint gain.

## Implementation gates

This ADR is locked; the workflow files are not yet written. The first CI workflow lands as part of Phase 3 pre-launch infrastructure work (no specific stage assignment yet — runs alongside whichever stage first touches deploy automation). The CD workflow lands once the hosting platform is decided.

Pre-launch must-have:

- [ ] `.github/workflows/ci.yml` runs `dotnet test` + `pnpm test` against Postgres 16, gates PR merge.
- [ ] Branch protection on `main`: required reviews + required CI green.
- [ ] `secrets.PG_PASSWORD` etc. stored in GitHub Actions secrets, not in repo.
- [ ] CD workflow defined (deploy target scoped to hosting ADR).
- [ ] OIDC trust relationship with the chosen host's identity service.
- [ ] Pre-migration database snapshot step.

## Cross-references

- `planning-phase3.md` § Open Questions — "CI service" and "CD strategy" are resolved by this ADR (Stage-launch hosting still open).
- `planning-resolved.md` — entry added.
- `testing.md` § CI/CD scope — workflow filenames named here become normative.
