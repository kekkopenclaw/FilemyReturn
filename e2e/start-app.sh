#!/usr/bin/env bash
# Start the real DocuArchive app for Playwright, the way production runs it:
# build the Angular frontend, serve it from the API's wwwroot, run the API.
# Everything is local and throwaway: a fresh SQLite file + storage dir under
# /tmp/docuarchive-e2e, deleted at the start of every run. Never the live site.
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
RUN_DIR="/tmp/docuarchive-e2e"
DOTNET="${HOME}/.dotnet/dotnet"
[ -x "$DOTNET" ] || DOTNET="dotnet"

rm -rf "$RUN_DIR" "$ROOT/backend/src/WebApi/wwwroot"
mkdir -p "$RUN_DIR/storage"

# 1. Frontend production build (fails loudly if the app does not compile).
cd "$ROOT/frontend"
npx ng build --configuration production

# 2. Serve the build from the API, exactly like the Docker image does.
mkdir -p "$ROOT/backend/src/WebApi/wwwroot"
cp -r "$ROOT/frontend/dist/frontend/browser/." "$ROOT/backend/src/WebApi/wwwroot/"

# 3. Run the API on http://localhost:5000 with a throwaway database.
cd "$ROOT/backend/src/WebApi"
export ASPNETCORE_ENVIRONMENT=Development
export ASPNETCORE_URLS=http://localhost:5000
export DOTNET_CLI_TELEMETRY_OPTOUT=1
export ConnectionStrings__Default="Data Source=$RUN_DIR/e2e.db"
export Storage__RootPath="$RUN_DIR/storage"
# Development allows the dev JWT placeholder; the seeded admin password is
# generated per run and never used by these tests (they register their own users).
exec "$DOTNET" run --project . -c Release --no-restore --no-launch-profile
