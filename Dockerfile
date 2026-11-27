# DocuArchive — single-image Dockerfile (Render Free / local Docker)
# One deploy: ASP.NET Core 10 API serves the Angular production build from wwwroot.
# No secrets in this file. All secrets come from environment variables at runtime:
#   Jwt__Key, ADMIN_PASSWORD, ConnectionStrings__Default, Database__Provider.
# Build with:  docker build -t docuarchive .
# Run locally: docker run --rm -p 5000:8080 -e Jwt__Key=<32+ chars> -e ADMIN_PASSWORD=<12+ chars> docuarchive
# Render sets $PORT; Program.cs reads it (UseUrls http://*:$PORT). Health: GET /health.

# ---- Stage 1: build Angular frontend (Node 24, Angular CLI 22.3.0) ----
FROM node:24-alpine AS web-build
WORKDIR /src/frontend
COPY frontend/package.json frontend/package-lock.json ./
RUN npm ci --no-audit --no-fund
COPY frontend/ ./
RUN npx ng build --configuration production

# ---- Stage 2: build + publish .NET 10 API ----
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS api-build
WORKDIR /src
COPY backend/DocuArchive.slnx backend/
COPY backend/src/ backend/src/
COPY backend/tests/ backend/tests/
# Copy the Angular production output into wwwroot so dotnet publish ships it.
# Angular 22 application builder (no outputPath in angular.json) emits
# dist/frontend/browser/ — index.html MUST be there. Fail loudly if it is not:
# a silent fallback would ship wwwroot/browser/index.html and the SPA would 404.
RUN mkdir -p backend/src/WebApi/wwwroot \
    && cp -r /src/frontend/dist/frontend/browser/. backend/src/WebApi/wwwroot/ \
    && test -f backend/src/WebApi/wwwroot/index.html
WORKDIR /src/backend
RUN dotnet publish src/WebApi/DocuArchive.Api.csproj -c Release -o /app/publish --nologo

# ---- Stage 3: runtime (ASP.NET Core 10, no SDK) ----
FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
RUN apt-get update && apt-get install -y --no-install-recommends curl \
    && rm -rf /var/lib/apt/lists/*
COPY --from=api-build /app/publish ./
# Writable data dir for the SQLite DB + uploaded files (Render free disk is ephemeral — see DEPLOY.md).
RUN mkdir -p /app/data/storage
# Listen on 8080 by default (docker compose maps 5000:8080 and healthchecks :8080).
# Program.cs calls UseUrls($PORT) when $PORT is set, which takes precedence over
# ASPNETCORE_URLS — so on Render (which injects $PORT) the app still listens on $PORT,
# and locally / in compose (no $PORT) it listens on 8080 instead of the ASP.NET
# Core default localhost:5000, which nothing was mapped to.
ENV ASPNETCORE_URLS=http://*:8080 \
    ASPNETCORE_ENVIRONMENT=Production \
    Database__Provider=Sqlite \
    ConnectionStrings__Default="Data Source=/app/data/docuarchive.db" \
    Storage__RootPath=/app/data/storage
EXPOSE 8080
HEALTHCHECK --interval=30s --timeout=5s --start-period=20s --retries=3 \
    CMD curl -fsS "http://localhost:${PORT:-8080}/health" || exit 1
ENTRYPOINT ["dotnet", "DocuArchive.Api.dll"]
