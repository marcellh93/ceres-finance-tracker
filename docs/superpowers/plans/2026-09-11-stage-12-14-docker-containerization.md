# Stage 12.14 — Docker containerization Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Produce a hardened, minimal container image that runs the Project Ceres ASP.NET Core app with its React SPA and Tailwind CSS baked in, plus the `.dockerignore` and doc updates that go with it.

**Architecture:** One multi-stage `Dockerfile` at the repo root — a `node:22-alpine` stage builds both pnpm projects (SPA + Tailwind), a `dotnet/sdk:10.0-alpine` stage runs `dotnet publish -c Release` with the pnpm-driven MSBuild targets suppressed (`SkipSpaBuild`/`SkipTailwind`), and a `dotnet/aspnet:10.0-alpine` runtime stage copies only the publish output and runs it non-root on port 8080.

**Tech Stack:** Docker multi-stage build; .NET 10 (`sdk`/`aspnet` alpine images); Node 22 + pnpm 10.33.2 (via corepack); the app's existing `SkipSpaBuild`/`SkipTailwind` MSBuild opt-outs.

**Spec:** `docs/superpowers/specs/2026-09-11-stage-12-14-docker-containerization-design.md`
**ADR:** `docs/decisions/ADR-0081-docker-containerization.md`

## Global Constraints

- Scope is Dockerfile + `.dockerignore` + doc updates ONLY. No `docker compose`, no CI image build/push, no read-only-filesystem wiring — those are documented-and-deferred to Stage 16.
- pnpm is pinned to **10.33.2** via corepack (matches both `package.json` `packageManager` fields); use `--frozen-lockfile`.
- The build must NOT let MSBuild shell out to pnpm in the .NET stage — pass `SkipSpaBuild=true SkipTailwind=true` to `dotnet publish`.
- Two pnpm projects: Tailwind's `build:css` is in `ProjectCeres/package.json`; the SPA's `build` is in `ProjectCeres.Client/package.json`. Build each in its own working directory.
- No secrets baked into the image. Production config is runtime-injected via env vars. Only the placeholder base `appsettings.json` may enter the image.
- Non-root runtime (uid 1000), `EXPOSE 8080`, `ASPNETCORE_HTTP_PORTS=8080`, `ENTRYPOINT ["dotnet","ProjectCeres.dll"]`.
- Base-image tags: use the `-alpine` variants; if a `10.0-alpine` tag is not published for .NET 10 GA, fall back to the non-alpine `10.0` tag and note it — do not switch base families further. Confirmed at `docker build`, not before.
- gitleaks Stop-hook active — no committed secret literals. Stay on `main`, no branches. No `Co-Authored-By` trailer.

---

### Task 1: `.dockerignore`

**Files:**
- Create: `.dockerignore` (repo root)

Small, independent, and it must exist before `docker build` so the context is correct — do it first.

- [ ] **Step 1: Write `.dockerignore`**

```
# Build outputs (rebuilt inside the image; never shadow the fresh build)
**/bin
**/obj
**/node_modules
ProjectCeres.Client/dist
ProjectCeres/wwwroot/dist
ProjectCeres/wwwroot/css

# VCS + tooling + docs (not needed at build/runtime)
.git
.github
.claude
.superpowers
docs
tools
**/*.user
**/TestResults
ProjectCeres.Client/e2e/.artifacts
**/*.md

# Config overlays + local secrets — only the placeholder base appsettings.json enters the image
**/appsettings.*.json
**/.env*
scripts/Untitled-1.json
```

- [ ] **Step 2: Sanity-check the patterns**

Run: `git check-ignore -v --no-index ProjectCeres/bin ProjectCeres.Client/node_modules ProjectCeres/appsettings.Development.json ProjectCeres/appsettings.json 2>/dev/null; echo "note: .dockerignore is not git's — this only sanity-reads glob shapes"`
Expected: the intent is that `appsettings.Development.json` is excluded while `appsettings.json` is kept. (`.dockerignore` globs are validated for real by the build in Task 3; this step is just a human read of the patterns.)

- [ ] **Step 3: Commit**

```bash
git add .dockerignore
git commit -m "build(12.14): .dockerignore — minimal Docker build context"
```

---

### Task 2: The multi-stage `Dockerfile`

**Files:**
- Create: `Dockerfile` (repo root)

**Interfaces:**
- Consumes: the app's `SkipSpaBuild` / `SkipTailwind` MSBuild opt-outs (`ProjectCeres/ProjectCeres.csproj`); the two `package.json` `packageManager: pnpm@10.33.2` pins; the app entrypoint assembly `ProjectCeres.dll`.
- Produces: a buildable image tagged `ceres:local`, serving on `:8080` as a non-root user.

- [ ] **Step 1: Write the `Dockerfile`**

```dockerfile
# syntax=docker/dockerfile:1

# ---- Stage 1: build the two pnpm projects (SPA + Tailwind CSS) ----
FROM node:22-alpine AS spa
# corepack ships with node:22; it reads the pinned pnpm version from packageManager,
# sidestepping the pnpm/action-setup sha512 misparse CI hit. Pin explicitly to match.
RUN corepack enable && corepack prepare pnpm@10.33.2 --activate
WORKDIR /src

# SPA (ProjectCeres.Client): tsc -b && vite build && check-size -> dist/
COPY ProjectCeres.Client/package.json ProjectCeres.Client/pnpm-lock.yaml ProjectCeres.Client/
RUN cd ProjectCeres.Client && pnpm install --frozen-lockfile
COPY ProjectCeres.Client/ ProjectCeres.Client/
RUN cd ProjectCeres.Client && pnpm build

# Tailwind (ProjectCeres): build:css -> wwwroot/css/site.css. Needs the Styles/
# input and the views Tailwind scans (its content globs), so copy the project.
COPY ProjectCeres/package.json ProjectCeres/pnpm-lock.yaml ProjectCeres/
RUN cd ProjectCeres && pnpm install --frozen-lockfile
COPY ProjectCeres/ ProjectCeres/
RUN cd ProjectCeres && pnpm run build:css

# ---- Stage 2: publish the .NET app (no Node/pnpm here) ----
FROM mcr.microsoft.com/dotnet/sdk:10.0-alpine AS build
WORKDIR /src
# Restore first for layer caching: copy csproj/sln, restore, then the rest.
COPY *.sln ./
COPY ProjectCeres/*.csproj ProjectCeres/
COPY ProjectCeres.Analyzers/*.csproj ProjectCeres.Analyzers/
COPY ProjectCeres.Analyzers.Annotations/*.csproj ProjectCeres.Analyzers.Annotations/
RUN dotnet restore ProjectCeres/ProjectCeres.csproj
COPY ProjectCeres/ ProjectCeres/
COPY ProjectCeres.Analyzers/ ProjectCeres.Analyzers/
COPY ProjectCeres.Analyzers.Annotations/ ProjectCeres.Analyzers.Annotations/

# Bring in the pre-built static assets from the spa stage, then publish with the
# pnpm-driven MSBuild targets suppressed (this stage has no Node).
COPY --from=spa /src/ProjectCeres.Client/dist/ ProjectCeres/wwwroot/dist/
COPY --from=spa /src/ProjectCeres/wwwroot/css/site.css ProjectCeres/wwwroot/css/site.css
# Fail loud if the SPA shell is missing (otherwise a runtime 404 on GET /).
RUN test -f ProjectCeres/wwwroot/dist/app.html
RUN dotnet publish ProjectCeres/ProjectCeres.csproj \
      -c Release -o /app/publish \
      -p:SkipSpaBuild=true -p:SkipTailwind=true

# ---- Stage 3: runtime (minimal, non-root) ----
FROM mcr.microsoft.com/dotnet/aspnet:10.0-alpine AS runtime
WORKDIR /app
# Non-root uid 1000. adduser -D creates a system user with no password.
RUN addgroup -g 1000 appuser && adduser -D -u 1000 -G appuser appuser
COPY --from=build /app/publish .
USER appuser
ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080
ENTRYPOINT ["dotnet", "ProjectCeres.dll"]
```

- [ ] **Step 2: Confirm the SPA-shell filename**

Run: `ls ProjectCeres/wwwroot/dist/*.html 2>/dev/null || grep -rn "MapFallbackToFile" ProjectCeres/Program.cs`
Expected: confirm the fallback file the app serves. The spec/Program.cs use `dist/app.html`; if the actual filename differs, fix the `test -f` guard in the Dockerfile to match it. (Adjust, don't assume.)

- [ ] **Step 3: Verify the base-image tags resolve, else fall back**

Run: `docker pull mcr.microsoft.com/dotnet/aspnet:10.0-alpine && docker pull mcr.microsoft.com/dotnet/sdk:10.0-alpine && docker pull node:22-alpine`
Expected: all three pull. If a `10.0-alpine` tag is NOT found, change that image's tag to `10.0` (non-alpine) in the Dockerfile, keep `addgroup`/`adduser` as `groupadd`/`useradd` if the base switches to Debian (`RUN groupadd -g 1000 appuser && useradd -u 1000 -g appuser -m appuser`), and note the fallback in the commit message.

- [ ] **Step 4: Build the image**

Run: `docker build -t ceres:local .`
Expected: all three stages succeed; final line `naming to docker.io/library/ceres:local`. If Docker is unavailable in this environment, STOP and report that plainly with the exact command for the user to run — do not mark this step done.

- [ ] **Step 5: Commit**

```bash
git add Dockerfile
git commit -m "build(12.14): multi-stage Dockerfile — node SPA build, pnpm-less .NET publish, non-root alpine runtime"
```

---

### Task 3: Boot + image-surface verification

**Files:** none (verification only). Folded into its own task because it is the stage's ship-gate and a reviewer could reject it independently of the Dockerfile text.

**Interfaces:**
- Consumes: the `ceres:local` image from Task 2; a reachable, migrated Postgres with the `ceres_app`/`ceres_admin` roles (the local dev DB `project_ceres` provisioned by `scripts/setup-postgres-roles.sql`).

- [ ] **Step 1: Boot smoke test against a reachable Postgres**

Run (single line; `host.docker.internal` reaches the host DB from the container on Docker Desktop):
```bash
docker run --rm -p 8080:8080 \
  -e ASPNETCORE_ENVIRONMENT=Production \
  -e ConnectionStrings__ApplicationConnection="Host=host.docker.internal;Database=project_ceres;Username=ceres_app;Password=ceres_app_dev_password" \
  -e ConnectionStrings__AdminConnection="Host=host.docker.internal;Database=project_ceres;Username=ceres_admin;Password=ceres_admin_dev_password" \
  -e Email__Resend__ApiKey="re_test_not_a_real_key" \
  -e Email__SupportAddress="support@example.test" \
  -e Email__PublicBaseUrl="https://ceres.example.test" \
  --name ceres-smoke ceres:local
```
Expected: the app starts and logs `Now listening on: http://[::]:8080` (or `http://0.0.0.0:8080`) with no unhandled startup exception. (Ctrl-C or `docker stop ceres-smoke` from another shell to stop.)

- [ ] **Step 2: Confirm the SPA shell is served + runs non-root**

Run (from another shell while the container is up):
```bash
curl -sS -o /dev/null -w "%{http_code} %{content_type}\n" http://localhost:8080/
docker exec ceres-smoke id
```
Expected: `200 text/html` for `GET /` (the SPA shell), and `id` shows `uid=1000(appuser)` — not root.

- [ ] **Step 3: Confirm the runtime image has no build toolchain**

Run:
```bash
docker run --rm --entrypoint sh ceres:local -c 'command -v dotnet && ! command -v node && ! command -v pnpm && echo OK-minimal'
```
Expected: prints the `dotnet` path then `OK-minimal` (node and pnpm absent).

- [ ] **Step 4: Record the evidence**

Capture the three command outputs (startup log line, curl status, `id`, and the minimal-surface check) into the stage's build-matrix evidence bundle. If Docker is unavailable, state that plainly, mark Task 3 blocked, and hand the exact commands to the user — never fabricate a pass.

- [ ] **Step 5: No commit** (verification only; no files changed).

---

### Task 4: Roadmap + security-model doc updates (stage close)

**Files:**
- Modify: `docs/roadmap-phase-three.md` — add a `## Stage 12.14` section marked Done with the verification checklist; cross-reference 16.13 (wave-2 hardening) and 16.15 (operator migrations).
- Modify: `docs/security-model.md` § Container / Runtime Hardening — note the wave-1 controls now realised in the Dockerfile; leave wave-2 items pending.

- [ ] **Step 1: Add the §12.14 roadmap section**

Add after the §12.13 section (or in stage order), mirroring the §12.18 Done format: status line, one-paragraph what/why, and a `[x]` checklist covering Dockerfile (3 stages), .dockerignore, non-root/8080/no-secrets, and the boot+surface verification. Add a `[→]` deferral line for wave-2 hardening pointing at 16.13 and one for CI image push pointing at 16.11 — each with a receiving `[ ]`/row in Stage 16 (16.13 exists; add a 16-section line for "container image build/push (CD)" if none exists).

- [ ] **Step 2: Update security-model § Container / Runtime Hardening**

Add a short note that the wave-1 controls (non-root uid 1000, minimal alpine base, no baked secrets, port 8080) are realised in the repo `Dockerfile` as of Stage 12.14, and that the wave-2 controls (read-only fs + tmpfs, digest pinning, cap-drop, resource limits) remain pending for Stage 16.13. Do not change the security requirements themselves.

- [ ] **Step 3: Run the roadmap-consistency check**

Run: `node -e "const fs=require('fs');const {scan}=require('./.claude/hooks/roadmap-consistency-check.js');const g=scan(fs.readFileSync('docs/roadmap-phase-three.md','utf8'));if(g.length){g.forEach(x=>console.error(x));process.exit(1)}else{console.log('consistent')}"`
Expected: `consistent`.

- [ ] **Step 4: Commit**

```bash
git add docs/roadmap-phase-three.md docs/security-model.md
git commit -m "docs(12.14): mark Docker stage Done; note wave-1 hardening realised, wave-2 deferred"
```

---

## Notes for the executor

- **Docker availability is not guaranteed in this environment.** If `docker` is not installed / the daemon is not running, Tasks 2 Step 4 and all of Task 3 cannot be verified locally. In that case: write the Dockerfile + .dockerignore (Tasks 1–2 Steps 1–2), commit them, mark the docker-run steps blocked with the exact commands, and say so plainly. Do NOT fabricate build/run output. The docs (Task 4) can still be completed, but the roadmap checklist item for the boot smoke test stays `[ ]` (blocked) rather than `[x]` until a real run confirms it.
- **Stage close (Phase E)** after Task 4: run `sync-docs` + `changelog-sync`, confirm zero unchecked `[ ]` under §12.14 (except any genuinely-blocked-on-Docker item, which stays `[ ]` with a note), then this plan's SDD workspace is deleted per subagent-driven-development.
