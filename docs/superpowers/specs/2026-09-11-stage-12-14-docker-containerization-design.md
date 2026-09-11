# Stage 12.14 — Docker containerization: design

**Status:** approved (2026-09-11), pending implementation.
**Decision record:** [ADR-0081](../../decisions/ADR-0081-docker-containerization.md).
**Scope choice (user, 2026-09-11):** Dockerfile + ADR + `.dockerignore` only. `docker compose`, CI image build/push, and read-only-filesystem wiring are explicitly out of scope; EF-migration-on-deploy and wave-2 hardening are *documented* here and deferred to Stage 16.

## Goal

Produce a hardened, minimal container image that runs the Project Ceres ASP.NET Core app (with its React SPA baked in), so the app can be deployed to a real host in Stage 16. Decoupled from CI.

## Non-goals (deferred, with their receiving stage)

- **`docker compose` / local Postgres container** — not built. A one-command local stack is a convenience, not a deployment need.
- **CI image build + registry push** — not built; the brainstorm deliberately keeps Docker decoupled from CI. Belongs to Stage 16.11 (CD pipeline).
- **Read-only root filesystem + `tmpfs` mounts** — documented as the wave-2 writable-path inventory below; wired at Stage 16.13, "after the deployment is otherwise stable" (per `security-model.md § Container / Runtime Hardening`).
- **Image digest pinning, capability drop, resource limits** — orchestration-layer flags, Stage 16.13.
- **EF migrations** — the app does **not** self-migrate (verified: no `Database.Migrate()` in `Program.cs`). Strategy is documented below and wired at Stage 16.10/16.15.

## Architecture — three-stage build

One multi-stage `Dockerfile` at the repo root. Only the final stage's contents ship.

### Stage 1 — `spa` (`node:22-alpine`)

Builds the two pnpm projects the app serves as static assets:

- **`ProjectCeres.Client`** — the React SPA. `pnpm install --frozen-lockfile` then `pnpm build` (`tsc -b && vite build && pnpm check-size`) → `ProjectCeres.Client/dist/`.
- **`ProjectCeres`** — Tailwind CSS. `pnpm install --frozen-lockfile` then `pnpm run build:css` (`tailwindcss -i ./Styles/app.css -o ./wwwroot/css/site.css --minify`) → `ProjectCeres/wwwroot/css/site.css`.

pnpm is enabled via `corepack enable` and pinned to **10.33.2** (matching the `packageManager` field in both `package.json` files). This sidesteps the `pnpm/action-setup` sha512-hash misparse gotcha CI hit — corepack reads the pinned version directly. `--frozen-lockfile` fails the build on lockfile drift.

**Two pnpm roots**, not one: Tailwind's `build:css` lives in `ProjectCeres/package.json`; the SPA's `build` lives in `ProjectCeres.Client/package.json`. The stage installs and builds each in its own working directory.

### Stage 2 — `build` (`mcr.microsoft.com/dotnet/sdk:10.0-alpine`)

`dotnet publish ProjectCeres/ProjectCeres.csproj -c Release -o /app/publish` with **`SkipTailwind=true SkipSpaBuild=true`** — so MSBuild's `BuildTailwind` / `BuildSpaClient` targets do NOT shell out to pnpm (this stage has no Node). This is the exact pnpm-less pattern CI already proves works (`ci.yml` sets `SkipTailwind` job-wide and stages the SPA separately).

Before publishing, the SPA artifacts are copied in from the `spa` stage:
- `--from=spa /src/ProjectCeres.Client/dist` → `ProjectCeres/wwwroot/dist`
- `--from=spa /src/ProjectCeres/wwwroot/css/site.css` → `ProjectCeres/wwwroot/css/site.css`

so `dotnet publish` carries them into the publish output as static web assets. The build fails loud if the SPA bundle is absent (a missing `wwwroot/dist/app.html` would otherwise only surface as a runtime 404 on `GET /`, the assertion `DashboardApiTests.GetDashboardRoot_ServesSpaShell` protects).

### Stage 3 — `runtime` (`mcr.microsoft.com/dotnet/aspnet:10.0-alpine`)

Copies only `/app/publish` from the `build` stage. No SDK, no Node, no pnpm.

- **Non-root:** create uid 1000 `appuser`; `USER appuser` before entrypoint (`security-model.md` mandate).
- **Port:** `EXPOSE 8080`, `ASPNETCORE_HTTP_PORTS=8080`. The aspnet:10 images default to 8080 precisely so a non-root user can bind it (they dropped the old privileged-80 default). TLS terminates at the reverse proxy (Stage 16.2/16.4); the container serves plain HTTP.
- **Entrypoint:** `ENTRYPOINT ["dotnet", "ProjectCeres.dll"]`.

## Configuration — runtime-injected, never baked

The image contains **no secrets**. The base `appsettings.json` ships with `configure-via-user-secrets` placeholders (verified: no real values), and Production reads these env vars, which override appsettings:

| Env var | Why (fail-to-boot guard in `Program.cs`) |
|---|---|
| `ConnectionStrings__ApplicationConnection` | app DB connection; boot throws if unset |
| `ConnectionStrings__AdminConnection` | admin (BYPASSRLS) connection |
| `Email__Resend__ApiKey` | required in Production |
| `Email__SupportAddress` | required in Production |
| `Email__PublicBaseUrl` | required in Production (outgoing-email links) |
| `ASPNETCORE_ENVIRONMENT=Production` | selects the Production config + cookie-secure policy |

`.dockerignore` excludes `appsettings.*.json` overlays (Development/E2E and any local secrets) so only the placeholder base file enters the image.

## `.dockerignore`

Keeps the build context small and prevents cruft/secrets in any layer:
`**/bin`, `**/obj`, `**/node_modules`, `ProjectCeres.Client/dist`, `ProjectCeres/wwwroot/dist`, `ProjectCeres/wwwroot/css`, `.git`, `.github`, `.claude`, `.superpowers`, `docs`, `tools`, `**/*.user`, `**/appsettings.*.json`, `**/.env*`, `**/TestResults`, `ProjectCeres.Client/e2e/.artifacts`, `**/*.md`.

(The rebuilt-in-image artifacts — `dist`, `wwwroot/dist`, `wwwroot/css`, `node_modules` — are excluded so the local working tree's copies never shadow the fresh in-image build.)

## EF migrations (documented, deferred to Stage 16)

The app does not self-migrate. The chosen strategy, to be wired at Stage 16.10/16.15:

- The **deploy operator** runs `dotnet ef database update` (or applies reviewed SQL) using the `ceres_migrator` role **out-of-band**, before rolling the new image, per `roadmap-phase-three.md` 16.15.
- The runtime container is deliberately **not** given migrator privileges and does **not** run migrations at entrypoint — this keeps the running image least-privilege (`ceres_app` / `ceres_admin` only), consistent with ADR-0068 role separation.
- This is recorded so a future deployer does not add an entrypoint-migrate that would require shipping migrator credentials into the runtime image.

## Wave-2 hardening — writable-path inventory (deferred to Stage 16.13)

`--read-only` root filesystem needs `tmpfs`/volume mounts for every path the runtime writes. Inventory:

- `/tmp` — framework + upload staging.
- **FileAttachments upload root** (`FileAttachments:RootPath`) — user file uploads; a persistent volume, not tmpfs (data must survive restart).
- **DataProtection keyring** — default `~/.aspnet/DataProtection-Keys`; a persistent volume or KMS (Stage 16.14). Ephemeral keys would invalidate all cookies on restart.

Also wave-2: image digest pinning, `--cap-drop=ALL`, `--memory`/`--cpus` limits.

## Testing / verification (the stage's proof)

1. `docker build -t ceres:local .` — all three stages succeed.
2. Boot smoke test: `docker run` with the required env vars pointed at a reachable Postgres; confirm the app starts, binds `:8080` as non-root, and `GET /` returns the SPA shell (200 `text/html`).
3. Image-surface check: final image has `dotnet` but not `node`/`pnpm`/the SDK.

If Docker is unavailable in the build environment, that is stated plainly and the exact commands are handed off — no faked pass.

## One thing to confirm at build time

The `-alpine` tag variants (`mcr.microsoft.com/dotnet/sdk:10.0-alpine`, `.../aspnet:10.0-alpine`, `node:22-alpine`) are assumed to exist. If a tag is not published for the pinned .NET 10 GA channel, fall back to the non-alpine `10.0` tag (Debian-based, larger but present) and note it — do not silently switch base families beyond that. Verified at `docker build`, not before.

## Files

- Create: `Dockerfile` (repo root)
- Create: `.dockerignore` (repo root)
- Create: `docs/decisions/ADR-0081-docker-containerization.md`
- Update: `docs/roadmap-phase-three.md` — add §12.14 (Done on implementation), cross-reference 16.13/16.15.
- Update: `docs/security-model.md § Container / Runtime Hardening` — note the wave-1 controls now realised in the Dockerfile, wave-2 still pending.
