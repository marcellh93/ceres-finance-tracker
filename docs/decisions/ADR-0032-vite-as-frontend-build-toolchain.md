# ADR 0032: Vite as the Frontend Build Toolchain and Vitest for Frontend Testing

## Status: Accepted

## Context

Phase 2 introduces React components embedded into Razor pages (hybrid model per ADR-0014).
This requires a JavaScript build toolchain that did not exist in Phase 1. The toolchain must:

- Compile and bundle React (JSX/TSX) components
- Integrate with the existing ASP.NET Core MVC project without requiring a full SPA rewrite
- Support a fast developer feedback loop alongside `dotnet run`
- Be consistent with the test tooling chosen for React components

Options considered for the build toolchain:

- **Vite** — native ESM dev server, near-instant HMR, Rollup-based production build, de-facto standard for new React projects, integrates with .NET via `Vite.AspNetCore` NuGet package
- **webpack (Create React App)** — CRA is deprecated; webpack alone is complex to configure and slow on large projects
- **Parcel** — zero-config but limited ecosystem and plugin support relative to Vite
- **esbuild standalone** — extremely fast but lower-level; no HMR, requires manual wiring

Options considered for frontend testing:

- **Vitest** — same config file and transform pipeline as Vite, Jest-compatible API, no separate setup required
- **Jest** — requires separate babel/transform config when using Vite; redundant toolchain
- **Playwright component testing** — suitable for E2E-style component tests but heavier than unit/component tests; deferred to Phase 3

## Decision

**Build toolchain: Vite.**

Vite is the correct choice for this stack:
- Fastest dev server available (native ESM — no bundling during development)
- Production builds via Rollup are well-optimized and well-documented
- `Vite.AspNetCore` NuGet package handles the .NET integration: dev proxy routes asset requests to the Vite dev server; production builds output to `wwwroot` as a standard static asset
- Standard for Vite-based React in the current ecosystem

**Frontend testing: Vitest + React Testing Library.**

Vitest is the natural pair for Vite:
- Shares the Vite config file — no separate Jest config or babel transform needed
- Jest-compatible API — same `describe`, `it`, `expect` surface; no relearning required
- `@testing-library/react` goes on top for component-level tests (render, query, interact)
- `jsdom` provides the DOM environment for headless component tests

Playwright component testing is deferred to Phase 3 alongside E2E tests.

## Consequences

**Positive:**
- Single config file (`vite.config.ts`) governs both the build and the test runner
- No separate Babel setup or transform pipeline for tests
- `Vite.AspNetCore` makes the .NET + Vite integration a solved problem — no custom middleware
- Vitest runs in watch mode alongside `dotnet watch run` without conflict

**Negative:**
- Introduces a `package.json` and `node_modules` tree into the .NET project — a new dependency class to maintain
- Developers must run both `dotnet run` and `vite dev` (or use `Vite.AspNetCore`'s auto-start) during Phase 2 development
- Full SPA transition in Phase 3 will likely move the Vite project to its own root — the Phase 2 embedded setup is intentionally temporary
