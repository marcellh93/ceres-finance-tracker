# ADR-0081 — Docker containerization: multi-stage build + wave-1 hardening

> **Diataxis type:** Explanation — architectural decision record.

**Status:** Accepted — 2026-09-11 (Stage 12.14). A three-stage `Dockerfile` + `.dockerignore` at the repo root produce a hardened, minimal runtime image; wave-2 hardening and deploy wiring are deferred to Stage 16.

**Phase:** Phase 3 (Hosted Beta) — accelerated out of Stage 16, tracked as §12.14. Sequenced CI (§12.13) → Docker (§12.14) → BDD (§12.15).

**Supersedes:** None.

**Related decisions:** ADR-0068 (three-role DB separation `ceres_app` / `ceres_admin` / `ceres_migrator`) — the migration-strategy decision below preserves it. ADR-0079 (pnpm 10.33.2 pin) — the build reuses that version.

**Design doc:** `docs/superpowers/specs/2026-09-11-stage-12-14-docker-containerization-design.md`.

## Context

Stage 16 (Hosting + ops) will deploy Project Ceres to a real server. That needs a container image that runs the ASP.NET Core app with its React SPA and minified Tailwind CSS baked in, hardened enough not to be a liability, and carrying no build tooling or secrets. This ADR records the image's shape. It is deliberately decoupled from CI (no image build/push job) and from a local `docker compose` stack — those are Stage 16.11 concerns.

Two properties of the codebase shape the design:

1. **The app serves two sets of pre-built static assets.** The SPA (`ProjectCeres.Client`, Vite → `wwwroot/dist`) and Tailwind CSS (`ProjectCeres`, `build:css` → `wwwroot/css/site.css`) are two separate pnpm projects. In a normal Release build, MSBuild targets (`BuildSpaClient`, `BuildTailwind`) shell out to pnpm to produce them; both can be suppressed via `SkipSpaBuild` / `SkipTailwind` (the pattern CI uses to build without Node).

2. **The app does not self-migrate.** `Program.cs` has no `Database.Migrate()` call (verified). Schema is applied out-of-band by the deploy operator using the `ceres_migrator` role, per ADR-0068 and roadmap 16.15.

## Decision

### A three-stage build, Node isolated from .NET

1. **`spa` (`node:22-alpine`)** builds both pnpm projects — the SPA bundle and the minified CSS — with pnpm enabled via `corepack` pinned to **10.33.2** (matching both `packageManager` fields; corepack reads the pin directly, avoiding the `pnpm/action-setup` sha512 misparse CI hit). `--frozen-lockfile` fails on lockfile drift.
2. **`build` (`dotnet/sdk:10.0-alpine`)** copies the SPA/CSS artifacts from `spa`, then `dotnet publish -c Release` with `SkipSpaBuild=true SkipTailwind=true` so the pnpm-driven MSBuild targets stay dormant (this stage has no Node). Same pnpm-less path CI proves.
3. **`runtime` (`dotnet/aspnet:10.0-alpine`)** copies only the publish output. No SDK, no Node, no pnpm.

**Why a dedicated Node stage over a combined SDK+Node stage:** the combined stage (install Node into the SDK image, let MSBuild drive pnpm) needs zero csproj changes but bloats the build stage, couples the toolchains, and caches worse — and the runtime stage discards it either way. Separation keeps each stage's surface minimal and each layer independently cacheable.

**Why alpine over Chainguard distroless:** `security-model.md` lists both as acceptable. Alpine images are Microsoft-official (no third-party registry dependency), tiny, and widely used; Chainguard's smaller CVE surface is not worth the external-registry coupling at this stage. Revisit if CVE pressure warrants it.

### Wave-1 runtime hardening in the Dockerfile

Per `security-model.md § Container / Runtime Hardening`, the controls a solo dev adopts immediately:

- **Non-root** — uid 1000 `appuser`, `USER appuser` before entrypoint.
- **Port 8080** — `EXPOSE 8080` + `ASPNETCORE_HTTP_PORTS=8080`; the aspnet:10 base defaults to 8080 so a non-root user can bind it. TLS terminates upstream at the reverse proxy (16.2/16.4); the container serves plain HTTP.
- **Minimal surface** — alpine runtime, single `ENTRYPOINT ["dotnet","ProjectCeres.dll"]`, no extra tools.
- **No secrets in the image** — all Production config is runtime-injected via env vars (`ConnectionStrings__ApplicationConnection`, `Email__Resend__ApiKey`, `Email__SupportAddress`, `Email__PublicBaseUrl`, `ASPNETCORE_ENVIRONMENT=Production`). The base `appsettings.json` ships only `configure-via-user-secrets` placeholders; `.dockerignore` excludes the `appsettings.*.json` overlays.

### Deferred, and where

- **EF migrations** — operator runs them out-of-band with `ceres_migrator` before rollout; the runtime image is NOT given migrator privileges and does NOT migrate at entrypoint (keeps it least-privilege per ADR-0068). Wired at 16.10/16.15.
- **Read-only root filesystem + tmpfs/volumes** — needs the writable-path inventory (`/tmp`, the `FileAttachments` upload root, the DataProtection keyring); done after deployment is stable, Stage 16.13. The design doc carries the inventory.
- **Image digest pinning, `--cap-drop=ALL`, resource limits** — orchestration flags, 16.13.
- **`docker compose`, CI image build/push** — out of scope by decision; 16.11.

## Consequences

**Positive.** A deployable, hardened, ~small image exists now, ahead of Stage 16, so hosting work starts from a known-good artifact. The runtime image carries no build toolchain and no secrets. Node and .NET toolchains are decoupled, so each build stage is minimal and independently cacheable. The migration decision is recorded, so a future deployer will not add an entrypoint-migrate that leaks migrator credentials into the runtime image.

**Negative / accepted.** The image is not yet run under a read-only filesystem, digest-pinned bases, or dropped capabilities — those are the explicit wave-2 items. Building both pnpm projects plus a .NET publish makes the build slower than a code-only image (mitigated by layer caching; only `spa` re-runs on a frontend change). The container assumes an external, already-migrated Postgres — it will fail to boot against an unmigrated database, which is the intended least-privilege behavior, not a bug.

**Verification.** `docker build` succeeds across all three stages; a boot smoke test confirms the app starts non-root on :8080 and `GET /` returns the SPA shell (200 text/html); the final image contains `dotnet` but not `node`/`pnpm`/the SDK.
