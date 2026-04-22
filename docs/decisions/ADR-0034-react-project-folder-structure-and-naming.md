# ADR 0034: React Project Folder Location and Naming Convention

## Status: Accepted

## Context

Phase 2 introduces React (per ADR-0014) and requires a decision on where React source files
live in the repository and how the folder is named.

The repository currently has two top-level project folders:

- `ProjectCeres/` — ASP.NET Core MVC backend
- `ProjectCeres.Tests/` — xUnit test project

React requires its own build system (Vite, per ADR-0032) separate from the .NET build
pipeline. The JS source files cannot live in `wwwroot/` (mixes source with output) and
should not be nested deeply inside the .NET project (couples two independent build systems).

Options considered for folder location:

- **`ProjectCeres/Scripts/`** — inside the .NET project folder; follows the existing Tailwind
  pattern but conflates the JS build system with the .NET project
- **`client/` at the repo root** — clean separation, matches some community conventions, but
  the name is generic and inconsistent with the existing dot-separated naming pattern
- **`ProjectCeres.Client/` at the repo root** — peer to `ProjectCeres/` and
  `ProjectCeres.Tests/`; follows Microsoft's `ClientApp/` convention adapted to the
  project's naming scheme

Options considered for naming:

- **`client/`** — short, common in JS communities, but breaks naming alignment
- **`ClientApp/`** — Microsoft's legacy default from the create-react-app template era;
  not a deliberate convention, now considered outdated
- **`ProjectCeres.Client/`** — consistent with the dot-separated pattern; descriptive and
  immediately recognisable as the frontend peer to the backend project

Renaming the backend to `ProjectCeres.Server/` was also considered for full symmetry.
Rejected: the rename would touch namespaces, the solution file, all docs, and all scripts
with no functional benefit. If a Phase 3 full restructure occurs, the rename can happen
then when everything is already being touched.

## Decision

The React project lives in **`ProjectCeres.Client/`** at the repo root.

The backend project stays as **`ProjectCeres/`** — no rename.

The repository top-level structure is:

```
ProjectCeres/           ← ASP.NET Core MVC backend
ProjectCeres.Client/    ← React + Vite frontend
ProjectCeres.Tests/     ← xUnit test project
```

`ProjectCeres.Client/` is a standard Vite project with its own `package.json`. The Vite
dev server proxy forwards API requests to the .NET backend during development. Production
builds output to `ProjectCeres.Client/dist/`, served by ASP.NET Core via the
`Vite.AspNetCore` NuGet package (per ADR-0032).

## Consequences

**Positive:**
- Naming is immediately recognisable and consistent with the rest of the repo
- The JS build system is fully isolated — `dotnet build` and `vite build` are independent
- Follows the Microsoft `ClientApp/` convention, so community documentation and tooling
  apply without translation
- No namespace or solution-file changes required

**Negative:**
- Slight asymmetry: `ProjectCeres/` has no `.Server` suffix while `ProjectCeres.Client/`
  has `.Client`. Accepted as a pragmatic tradeoff — the rename cost outweighs the symmetry
  benefit at this stage
