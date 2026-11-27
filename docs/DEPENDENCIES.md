# Dependencies

Every dependency DocuArchive uses, where it comes from, and what it costs.
Rule: **free and open-source only** — no paid services, no paid GitHub
features, no licence fees. Versions below are what is pinned in the repo
on 27 November 2026; check the `.csproj` / `package.json` files for the
current pins.

## Backend — .NET 10 (`backend/src/*/*.csproj`)

All NuGet packages are from nuget.org. Microsoft packages are MIT-licensed
(or .NET Foundation); Npgsql is PostgreSQL-licensed; xUnit is Apache 2.0.

| Package | Version | Used by | Licence / notes |
| --- | --- | --- | --- |
| Microsoft.AspNetCore.Identity.EntityFrameworkCore | 10.0.0 | Infrastructure | Users, roles, password hashing |
| Microsoft.AspNetCore.Authentication.JwtBearer | 10.0.0 | WebApi | JWT bearer auth |
| Microsoft.AspNetCore.OpenApi | 10.0.15 | WebApi | OpenAPI document generation |
| Swashbuckle.AspNetCore | 10.0.0 | WebApi | Swagger UI |
| Microsoft.EntityFrameworkCore.Sqlite | 10.0.0 | Infrastructure | Default database (free tier) |
| Npgsql.EntityFrameworkCore.PostgreSQL | 10.0.0 | Infrastructure | Optional PostgreSQL provider |
| Microsoft.EntityFrameworkCore.Design | 10.0.0 | Infrastructure, WebApi | Migrations tooling |
| Microsoft.Extensions.Diagnostics.HealthChecks.EntityFrameworkCore | 10.0.0 | WebApi | `/health/ready` DB check |
| Microsoft.Extensions.Options | 10.0.0 | Application | Options binding |
| Microsoft.Extensions.Hosting | 10.0.15 | Worker | Background worker host |
| System.IdentityModel.Tokens.Jwt | 8.14.0 | Application | Token creation/validation |
| System.Security.Cryptography.Pkcs / .Xml | 10.0.13 | Infrastructure, WebApi, Tests | CMS/PDF signature support |
| SQLitePCLRaw.bundle_e_sqlite3 | 2.1.12 | Infrastructure, WebApi, Tests | Native SQLite for EF Core |
| Microsoft.AspNetCore.Mvc.Testing | 10.0.0 | Tests | Integration tests |
| Microsoft.NET.Test.Sdk | 18.0.1 | Tests | Test runner |
| xunit.v3 | 3.2.0 | Tests | Test framework |
| coverlet.collector | 6.0.4 | Tests | Coverage collection |
| QRCoder | 1.6.0 | Infrastructure | QR codes for physical boxes (SVG only, no System.Drawing at runtime); MIT licence, verified on nuget.org 2026-11-27 |

No other NuGet packages are referenced. There is no paid telemetry,
no paid logging service and no cloud OCR SDK.

## Frontend — Angular 22 (`frontend/package.json`)

All npm packages are from npmjs.com. Angular is MIT; Bootstrap is MIT;
RxJS is Apache 2.0; Vitest is MIT.

| Package | Version | Notes |
| --- | --- | --- |
| @angular/common, compiler, core, forms, platform-browser, router | ^22.3.0 | Framework |
| bootstrap | ^5.3.9 | Responsive layout only; no paid theme |
| rxjs | ~7.8.0 | Async streams |
| tslib | ^2.3.0 | TypeScript helpers |

Dev-only:

| Package | Version | Notes |
| --- | --- | --- |
| @angular/build, cli, compiler-cli | ^22.3.0 | Build tooling |
| typescript | ~6.0.2 | Language |
| vitest | ^5.0.0 | Unit tests |
| jsdom | ^31.0.0 | DOM for Vitest |
| prettier | ^3.8.1 | Formatting |

## End-to-end tests (`e2e/package.json`)

| Package | Version | Notes |
| --- | --- | --- |
| @playwright/test | ^1.49.1 | Browser E2E tests (Apache 2.0) |
| typescript | ~5.9.0 | Test language |

## System tools (not packages — installed in the Docker image)

| Tool | Purpose | Licence / cost |
| --- | --- | --- |
| Tesseract OCR | Local OCR engine | Apache 2.0, free |
| poppler-utils (`pdftotext`, `pdftoppm`) | PDF text/image extraction for OCR | GPL, free |

PaddleOCR is present only as an **unwired stub** (`PaddleOcrEngine.cs`):
it needs a Python sidecar and ~1 GB RAM, which does not fit the Render
Free tier, so it is not a dependency of the running system. See
`docs/OCR.md` for the engine decision.

## Hosting and services — all free tier

| Service | Tier | Cost |
| --- | --- | --- |
| Render web service (Docker, Frankfurt) | Free | £0 — sleeps after 15 min idle, SQLite resets on redeploy |
| GitHub Actions (CI) | Free for public repos | £0 |
| GitHub repository (public) | Free | £0 |

No paid service is required to run, build, test or deploy this project.
If a dependency ever needs a paid tier, that is a design change to be
agreed first — it is not something to add quietly in a commit.
