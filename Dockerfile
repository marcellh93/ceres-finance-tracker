# syntax=docker/dockerfile:1

# ---- Stage 1: build the two pnpm projects (SPA + Tailwind CSS) ----
FROM node:22-alpine AS spa
# corepack ships with node:22; it reads the pinned pnpm version from packageManager,
# sidestepping the pnpm/action-setup sha512 misparse CI hit. Pin explicitly to match.
RUN corepack enable && corepack prepare pnpm@10.33.2 --activate
ENV COREPACK_ENABLE_STRICT=0
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
