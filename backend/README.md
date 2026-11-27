# DocuArchive backend — Phases 1 + 2

Clean Architecture (.NET 10 LTS). All dependencies are free and open-source; no paid
packages, services or licence keys are used anywhere (see **Dependencies** below).

## Layout

| Project | Layer | References |
|---|---|---|
| `src/Domain/` — `DocuArchive.Domain` | Entities (`Workspace`, `WorkspaceMembership`, `Archive`, `Document`, `AuditLog`, `RefreshToken`), `WorkspaceRole`, plain `UserProfile`, interfaces (`IOcrEngine`, `IDocumentStorage`, `IAuditService`, `IWorkspaceAccessService`, `IJwtService`, `IUserIdentityService`, `IEmailService`, `IOcrJobQueue`) and repository interfaces | Nothing external (no EF, no ASP.NET) |
| `src/Application/` — `DocuArchive.Application` | DTOs (the exact REST contract), `JwtService`, `AuthService`, `WorkspaceService`, `ArchiveService`, `DocumentService`, `DocumentUploadService` (Phase 2), `FileValidation` (magic bytes), `StorageSettings`, `DashboardService`, `AuditQueryService`, validation + `AppException` error types | Domain only (project-wise; JWT is a NuGet package) |
| `src/Infrastructure/` — `DocuArchive.Infrastructure` | `AppDbContext` (EF Core), Identity `ApplicationUser` + `IdentityUserService`, EF repositories, `AuditService`, `WorkspaceAccessService`, `LocalFileSystemStorage`, `PaddleOcrEngine` stub (throws `NotImplementedException`, Phase 3), `LogEmailService`, `SeedData`, EF migrations (`Migrations/`) | Application + Domain |
| `src/WebApi/` — `DocuArchive.Api` | Thin controllers, `Program.cs` (Identity options, JWT bearer, CORS, rate limiting, Swagger, `/health`, exception middleware), `appsettings.json` | Application + Infrastructure |
| `src/Worker/` — `DocuArchive.Worker` | Placeholder only: `ChannelOcrJobQueue` + `OcrWorker` BackgroundService registration. No processing in Phase 1 (Phase 3). | Domain |
| `tests/DocuArchive.Tests/` — `DocuArchive.Tests` | xUnit v3 + `WebApplicationFactory`, SQLite in-memory | WebApi, Infrastructure, Domain |

Dependency rule: WebApi → Application → Domain; Infrastructure → Application/Domain;
Domain depends on nothing external.

**Identity exception (documented):** ASP.NET Core Identity's user type
(`ApplicationUser : IdentityUser`) lives in Infrastructure, because Identity is an
infrastructure concern and Domain must not reference ASP.NET Core / EF Core.
Application services only ever see the plain Domain `UserProfile`.

## API contract (Phases 1–2, frozen for the frontend)

- `POST /api/auth/register`, `POST /api/auth/login`, `POST /api/auth/refresh`,
  `POST /api/auth/logout`, `POST /api/auth/change-password`, `GET /api/auth/me`
  — access token 15 min, refresh token 7 days (SHA-256 hashed in DB, rotated on use).
  Lockout after 5 failed logins for 15 min (Identity LockoutOptions). Accounts flagged
  `MustChangePassword` get login **403 with code `MustChangePassword`** until changed.
- `GET /api/workspaces`, `POST /api/workspaces`, `GET /api/workspaces/{id}`,
  `GET /api/workspaces/{id}/members`, `POST /api/workspaces/{id}/members` (Owner/Admin),
  `DELETE /api/workspaces/{id}` (Owner). Creator becomes WorkspaceOwner.
- `GET/POST /api/workspaces/{workspaceId}/archives`, `GET .../archives/{archiveId}`
  (create: StandardUser+; ReadOnly cannot create).
- `GET /api/workspaces/{workspaceId}/documents`, `GET .../documents/{documentId}`
- **Phase 2:** `POST /api/workspaces/{workspaceId}/documents` (multipart field `file`,
  optional `?archiveId=`, StandardUser+; magic-byte validation PDF/PNG/JPEG/GIF/TIFF/BMP/
  WebP, 25 MiB limit → 413, per-workspace SHA-256 duplicate → 409),
  `GET .../documents/{documentId}/download` (members only, original filename),
  `GET /api/workspaces/{workspaceId}/uploads` (upload history, newest first, with uploader).
- `GET /api/workspaces/{workspaceId}/dashboard` — real DB counts, 0 when empty.
- `GET /api/workspaces/{workspaceId}/audit-logs` — ArchiveManager+ only, read-only
  (no edit/delete endpoint exists).
- `GET /health`; Swagger UI in Development at `/swagger`.

Workspace isolation is enforced explicitly in services (`IWorkspaceAccessService`) on
every endpoint — no global query filters. Non-members get 404 (existence not leaked);
insufficient role gets 403.

## Run locally

```bash
export DOTNET_ROOT=$HOME/.dotnet PATH=$HOME/.dotnet:$PATH DOTNET_CLI_TELEMETRY_OPTOUT=1 TMPDIR=$HOME/.tmp
cd backend
dotnet run --project src/WebApi -c Release
# API: http://localhost:5000/swagger (Development), health: /health
```

## Deploy readiness (single deploy — API + Angular from one process)

The parent agent owns `Dockerfile` / `docker-compose.yml` / `render.yaml` / CI at the
repo root — this backend only exposes the hooks they need:

- **Frontend from wwwroot:** when `src/WebApi/wwwroot/` exists, `Program.cs` serves it
  (`UseDefaultFiles` + `UseStaticFiles`) and falls back unknown routes to `index.html`
  (`MapFallbackToFile`), so one process serves the Angular SPA and the API. When
  `wwwroot` is absent (local API-only dev), both are no-ops and the API works as before.
  At deploy time the Angular production build output is copied into `src/WebApi/wwwroot/`.
- **Port:** the API listens on the `$PORT` env var when set (Render injects it),
  validated 1–65535 in `Program.cs` (`builder.WebHost.UseUrls($"http://*:{port}")`).
  When unset, ASP.NET Core defaults apply. No port is hardcoded.
- **Health:** `GET /health` (Render `healthCheckPath`).

## Tests

```bash
dotnet test tests/DocuArchive.Tests/DocuArchive.Tests.csproj -c Release
```

26 tests: Phase 1 auth (register/login/refresh rotation/lockout after 5 failures/
change-password), authorisation (401 unauthenticated, ReadOnly cannot create
archive/member/audit), workspace isolation (non-member 404s, no list leakage, document
isolation), dashboard empty state, audit logging (login + workspace create, no
edit/delete route), Phase 2 uploads (valid PDF 201 + list/history, byte-identical
download, invalid type 400, duplicate 409, oversize 413, ReadOnly 403, non-member 404,
unauthenticated 401).

> **Test-runner note (this VM only):** `dotnet test` here aborts because the .NET 10
> SDK's `vstest.console` (18.7.1) never completes its socket handshake with the
> `testhost` process — the testhost starts but never opens a TCP connection, even for
> a stock `dotnet new xunit` project (loopback TCP itself was verified working).
> The tests therefore run via Microsoft.Testing.Platform in-process execution, which
> bypasses vstest entirely and is the supported way to run xUnit v3 tests directly:
>
> ```bash
> dotnet tests/DocuArchive.Tests/bin/Release/net10.0/DocuArchive.Tests.dll
> # === TEST EXECUTION SUMMARY ===  Total: 26, Errors: 0, Failed: 0
> ```
>
> On CI / GitHub Actions `dotnet test` is expected to work normally. The project was
> migrated to **xUnit v3 (3.2.0, Apache-2.0)** + **Microsoft.NET.Test.Sdk 18.0.1**
> with `EnableMicrosoftTestingPlatform=true` so this direct execution path exists.

Environment (no secrets in source — placeholders only in `appsettings.json`):

| Setting | Default | Notes |
|---|---|---|
| `Database__Provider` | `Sqlite` | `Sqlite` (dev) or `Postgres` (prod) |
| `ConnectionStrings__Default` | `Data Source=docuarchive.db` | Postgres connection string in prod |
| `Jwt__Key` | dev placeholder | **Must be set via env var / user-secrets in production (min 32 chars)** — the API **refuses to start in Production** if it is missing, equals the `DEV_ONLY` placeholder, or is shorter than 32 chars (startup check in `Program.cs`) |
| `ADMIN_PASSWORD` | *(none)* | Seed admin password (Development seed only, min 12 chars). If unset in Development a random one is generated and printed once; if unset outside Development, seeding fails loudly. Never committed. |
| `PORT` | *(unset)* | Port to listen on (Render injects it); validated 1–65535 in `Program.cs`. No port is hardcoded. |
| `Jwt__Issuer` / `Jwt__Audience` | `DocuArchive` | |
| `Frontend__Origin` | `http://localhost:4200` | CORS allow-list |
| `Storage__RootPath` | `storage` | Local file storage root |

Development seed (Development only, never Production): workspace **"Demo Archive"** +
admin `admin@docuarchive.local`. There is **no hardcoded seed password**: set
`ADMIN_PASSWORD` (min 12 chars) in the environment; if unset in Development, a random
password is generated at runtime and printed **once** to the console/log (never written
to a file). The seeded admin is flagged `MustChangePassword` and login returns
**403 with code `MustChangePassword`** until the password is changed via
`POST /api/auth/change-password` (supply `email` + current password; no login needed
for that first change — the current password is still required).

## Migrations

Migrations live in `src/Infrastructure/Migrations/`: `InitialCreate` and
`AddMustChangePassword` (adds `AspNetUsers.MustChangePassword`). Generated with:

```bash
dotnet ef migrations add InitialCreate \
  --project src/Infrastructure/DocuArchive.Infrastructure.csproj \
  --startup-project src/WebApi/DocuArchive.Api.csproj
```

Postgres equivalent (same migration code, provider-specific SQL is generated at
`dotnet ef database update` time against a Postgres connection string):

```bash
Database__Provider=Postgres \
ConnectionStrings__Default="Host=localhost;Database=docuarchive;Username=...;Password=..." \
dotnet ef database update --project src/Infrastructure --startup-project src/WebApi
```

Note: the EF Core migration is provider-agnostic C#; run `database update` once per
environment. A Postgres-specific migration, if ever needed, can be generated with the
design-time factory pointed at Npgsql — the factory currently defaults to SQLite.

## Dependencies (all free & open-source, commercially compatible licences)

| Package | Version | Licence | Used by |
|---|---|---|---|
| Microsoft.AspNetCore.Identity.EntityFrameworkCore | 10.0.0 | MIT | Infrastructure |
| Microsoft.EntityFrameworkCore.Sqlite | 10.0.0 | MIT | Infrastructure, Tests |
| Npgsql.EntityFrameworkCore.PostgreSQL | 10.0.0 | PostgreSQL Licence (free OSS) | Infrastructure |
| Microsoft.EntityFrameworkCore.Design | 10.0.0 | MIT | Infrastructure, WebApi (migrations tooling) |
| Microsoft.AspNetCore.Authentication.JwtBearer | 10.0.0 | MIT | WebApi |
| Microsoft.Extensions.Diagnostics.HealthChecks.EntityFrameworkCore | 10.0.0 | MIT | WebApi |
| Swashbuckle.AspNetCore | 10.0.0 | MIT | WebApi (Swagger) |
| System.IdentityModel.Tokens.Jwt | 8.14.0 | MIT | Application |
| Microsoft.Extensions.Options | 10.0.0 | MIT | Application |
| Microsoft.AspNetCore.Mvc.Testing | 10.0.0 | MIT | Tests |
| xunit.v3 | 3.2.0 | Apache-2.0 | Tests |
| Microsoft.NET.Test.Sdk | 18.0.1 | MIT | Tests |
| coverlet.collector | 6.0.4 | MIT | Tests |
| Microsoft.AspNetCore.OpenApi | 10.0.15 | MIT | WebApi (template leftover; Swagger is Swashbuckle) |
| SQLitePCLRaw.bundle_e_sqlite3 | 2.1.12 | MIT | Infrastructure, WebApi, Tests — **security pin** (see below) |
| System.Security.Cryptography.Pkcs | 10.0.13 | MIT | Infrastructure, WebApi, Tests — **security pin** (see below) |
| System.Security.Cryptography.Xml | 10.0.13 | MIT | Infrastructure, WebApi, Tests — **security pin** (see below) |

### Security pins (transitive vulnerability fixes, verified on nuget.org)

`dotnet list package --vulnerable` (2026-11-26) reported, before pinning:
`SQLitePCLRaw.lib.e_sqlite3` 2.1.11 High (GHSA-2m69-gcr7-jv3q / CVE-2025-6965) and
`System.Security.Cryptography.Pkcs` 9.0.0 High (GHSA-846f-f764-76mx / CVE-2026-84008)
+ Moderate (GHSA-2fmv-rv2c-59x7), plus `System.Security.Cryptography.Xml` 9.0.0
NU1903 warnings for the same October 2026 .NET DoS fixes. Fixed by explicit
direct references (nearest-wins over the transitive versions):
`SQLitePCLRaw.bundle_e_sqlite3` **2.1.12** (first patched 2.x build, bundles SQLite
3.53.3; do NOT jump to 3.x — it re-cut the packages) and `Pkcs`/`Xml` **10.0.13**
(first patched 10.0.x builds; 9.0.21 is the patched 9.0.x equivalent). After pinning,
`dotnet build` emits **0 NU1902/NU1903 warnings** and the audit reports no vulnerable
packages in any project. Re-check with `dotnet list DocuArchive.slnx package --vulnerable --include-transitive`.

No package above requires payment, a licence key, or a proprietary runtime. No
GitHub Copilot / Codespaces / paid Actions runners were used.

## TODOs left for Phase 3+

- Phase 2: DONE (uploads, validation, duplicates, download, upload history).
- Phase 3: PaddleOCR — implement `PaddleOcrEngine.ExtractTextAsync` and the `OcrWorker` processing loop (queue + registration already exist). Never return fake OCR text.
- Phase 3+: email — replace `LogEmailService` with a real provider behind `IEmailService` when needed.
- Phase 3+: S3-compatible storage behind `IDocumentStorage` (local filesystem is Phase 1/2).
