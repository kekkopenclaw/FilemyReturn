# DocuArchive — Setup

How to run DocuArchive locally and how the live deploy is configured.
Stack: .NET 10 LTS (ASP.NET Core Web API) + Angular 22 + SQLite/PostgreSQL +
Docker. OCR is Tesseract (free, CPU-only, self-hosted). Everything here is
free and open-source; there are no paid services, keys or accounts to set up.

See also: `README.md` (status), `docs/ARCHITECTURE.md` (design),
`docs/OCR.md` (OCR engine decision), `backend/README.md` (backend detail),
`WORKING-RULES.md` (rules every change must follow before it is pushed).

## Prerequisites

- **Recommended path:** Docker + Docker Compose only.
- **Local dev without Docker:** .NET 10 SDK, Node 24, Angular CLI 22.3.0
  (`npm i -g @angular/cli` is not needed — `npx ng` works from `frontend/`).

## Quick start (Docker Compose)

```bash
cp .env.example .env
# Edit .env and set REAL values (every value in .env.example is a placeholder):
#   JWT_KEY         — random string, at least 32 characters (required)
#   ADMIN_PASSWORD  — at least 12 characters (seed admin login, see below)
#   POSTGRES_PASSWORD — random (only needed with the postgres profile, below)
docker compose up --build
```

Open **http://localhost:5000** — the API serves the Angular production build
from `wwwroot`, so one URL is the whole app. Swagger UI is at
`/swagger` (Development only). Health: `GET /health`.

Compose details (`docker-compose.yml`): the `api` service builds the repo
`Dockerfile` and maps `5000:8080`; by default it is the only service that
starts, on SQLite. The `db` service is `postgres:16-alpine`
(user `docuarchive`, database `docuarchive`, port `5432`) and sits behind
the `postgres` profile — it never starts unless you ask for it, so the
default run needs no Postgres password. To test the production provider:

```bash
docker compose --profile postgres up --build
# with DATABASE_PROVIDER=Postgres and CONNECTION_STRING set in .env, e.g.
# CONNECTION_STRING=Host=db;Database=docuarchive;Username=docuarchive;Password=<same as POSTGRES_PASSWORD>
```

Data lives in the `docuarchive-data` volume
(`/app/data`: SQLite file + uploaded files).

## Local dev without Docker

Backend (from `backend/`):

```bash
dotnet run --project src/WebApi -c Release
# API: http://localhost:5000, Swagger at /swagger (Development)
```

The backend README has the full run/test commands and the API contract.
Frontend (from `frontend/`):

```bash
npm ci
npm start          # ng serve — http://localhost:4200, /api proxied to localhost:5000
```

`proxy.conf.json` proxies `/api` to the backend, so the Angular dev server
talks to the API on `:5000` with no CORS setup. To build the production
bundle the Dockerfile ships: `npx ng build --configuration production`.

## Environment variables

| Variable | Required | Notes |
|---|---|---|
| `Jwt__Key` (`JWT_KEY` in `.env`) | Yes | JWT signing key, **32+ random characters**. The API refuses to start in Production with the dev placeholder key. |
| `ADMIN_PASSWORD` | Yes in Production | Seed admin password, **12+ characters**. In Development the API generates a random one if unset — set it so you know the login. |
| `Database__Provider` | No (`Sqlite`) | `Sqlite` or `Postgres`. |
| `ConnectionStrings__Default` | No | SQLite: `Data Source=/app/data/docuarchive.db`. Postgres: standard Npgsql connection string. |
| `Storage__RootPath` | No | Uploaded-file storage, default `/app/data/storage`. |
| `Frontend__Origin` | No | CORS origin for the Angular dev server (`http://localhost:4200` locally, `https://docuarchive.onrender.com` live). Same-origin in production, so the default is safe. |
| `ASPNETCORE_ENVIRONMENT` | No | `Development` locally, `Production` live. |
| `PORT` | No (Render sets it) | The API listens on `$PORT` when set, otherwise the ASP.NET Core default. Never hardcode a port. |

**Never commit `.env`.** It is git-ignored; only `.env.example` (placeholders)
belongs in the repo. No secret ever goes in source code, `appsettings.json`,
`render.yaml` or a Dockerfile.

## First login

The seed admin is `admin@docuarchive.local` with the `ADMIN_PASSWORD` from
the environment. First login forces a password change: until it is changed,
login returns **403 `MustChangePassword`**. Registering a normal account and
creating a workspace works without the admin login.

## Tests

```bash
cd backend && dotnet test tests/DocuArchive.Tests/DocuArchive.Tests.csproj -c Release
cd frontend && npx ng build --configuration production
# Playwright end-to-end tests live in e2e/
```

Nothing is pushed until `dotnet build` (0 errors), `dotnet test`,
`ng build` and a security check all pass — see `WORKING-RULES.md`.
CI (`.github/workflows/ci.yml`) runs the same checks on every push to `main`.

## Live deploy (Render Free)

Live: **https://docuarchive.onrender.com** — one Free web service
(`docuarchive`, Frankfurt, Docker runtime, health check `GET /health`,
auto-deploy on push to `main`), defined in `render.yaml`. The API serves
the Angular build from `wwwroot`; the database is SQLite on ephemeral disk.

- Secrets (`Jwt__Key`, `ADMIN_PASSWORD`) are generated by Render
  (`generateValue`) or set in the dashboard — never in the repo.
- `Database__Provider=Sqlite`, `ASPNETCORE_ENVIRONMENT=Production`,
  `Frontend__Origin=https://docuarchive.onrender.com`.
- Do **not** use the Render GitHub Marketplace install: it asks for a
  payment method to verify. The service was created directly via the
  Render API/dashboard.
- Free-plan limits to know: the service sleeps after 15 minutes idle (slow
  first load), and **SQLite data resets on every redeploy** — uploads and
  accounts created on the live site do not survive a deploy.

## Troubleshooting

- `docker compose` refuses to start: `JWT_KEY` is unset in `.env` — copy
  `.env.example` to `.env` and fill it in. (`POSTGRES_PASSWORD` is only
  required with the `postgres` profile, not on the default SQLite run.)
- API won't start in Production: the JWT key is the dev placeholder or
  shorter than 32 characters — set a real random `Jwt__Key`.
- Uploads stay `Queued` / `Failed`: OCR needs `tesseract-ocr` +
  `poppler-utils` in the image — they are installed in the `Dockerfile`
  runtime stage; a custom image without them cannot OCR.
- First load of the live site is slow: the free plan was asleep — wait for
  it to wake (up to ~a minute), then `/health` returns 200 `Healthy`.
- Live data (uploads, workspaces) disappeared: a redeploy happened — see
  the ephemeral-disk note above. This is expected on Render Free.
