# Phase 4 — Live browser iteration (interactive design session, UNVERIFIED)

The user wants to **point at an element on screen** and ask for variants rather than describing a change in prose.

Run `/impeccable live`. The user browses **`https://localhost:7081`** (the Kestrel/dotnet port — same origin they use every day). Despite the React client living in `ProjectCeres.Client/`, the dev setup uses `Vite.AspNetCore`'s `UseViteDevelopmentServer` middleware (see `ProjectCeres/Program.cs` ~L582) to run Vite *inside* the dotnet pipeline. Vite's HMR WebSocket is explicitly terminated at the dotnet port via `clientPort: 7081` in `ProjectCeres.Client/vite.config.ts`. So HMR works, but it reaches the browser through Kestrel's reverse proxy rather than from Vite's standalone port 5173.

**This phase is unverified.** Impeccable docs describe `/impeccable live` as working on "Vite, Next.js (including monorepos), SvelteKit, Astro, Nuxt" but say nothing about Vite hosted behind a dotnet middleware. There are two ways it could discover the dev server — by inspecting the Vite process (works fine for us) or by probing port 5173 directly (would miss us, since clients hit 7081). The first time this phase runs, **treat it as an experiment**: try it, watch for "no Vite process found" or "HMR not connected" errors, and report back. If it works, leave Phase 4 in place. If it fails, fall back to Phase 1 or 2 and add a one-line note here documenting the failure mode so it isn't retried indefinitely.

**Prerequisites before invoking:**
- `dotnet run --project ProjectCeres` running on `https://localhost:7081`.
- The browser session is on the React-served path (`/app/...`), not a pure Razor path. The Razor layer (Tailwind v3, server-rendered) has no HMR and is out of scope for live mode regardless.

**Caveats:**
- After accepting a variant, **the working-rule consistency clause applies**: a change to a shared primitive must be propagated everywhere it's used in the same pass (CLAUDE.md rule + `docs/design-system.md` § Working rules #5). Do this manually after the live session ends.
- The Razor layer (`ProjectCeres/Views/`, Tailwind v3) cannot be live-iterated. For Razor view changes, fall back to Phase 1 or 2.
