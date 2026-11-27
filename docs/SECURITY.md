# Security

How DocuArchive protects accounts, data and uploads. This document describes
what is implemented in the code today (27 November 2026); anything listed
here is verifiable in the repository, not aspirational.

## Authentication

- **ASP.NET Core Identity** for users, with **JWT Bearer** authentication.
- Access tokens last **15 minutes**; refresh tokens last **7 days**
  (`Jwt:AccessTokenMinutes` / `Jwt:RefreshTokenDays` in `appsettings.json`).
- Passwords are hashed by Identity (PBKDF2). No password is ever stored or
  logged in plain text.

## Secrets — environment only, never in the repo

- The seed admin account is created from the `ADMIN_PASSWORD` environment
  variable only (`backend/src/Infrastructure/Data/SeedData.cs`). There is
  **no hardcoded seed password anywhere in the codebase**. Seeding throws
  and refuses to start if `ADMIN_PASSWORD` is missing or shorter than
  12 characters, and it only runs when the database has no users yet.
- The seed admin must change its password on first login
  (`MustChangePassword`, enforced with HTTP 403 until changed).
- The JWT signing key comes from the `Jwt__Key` environment variable.
  `appsettings.json` contains only a `DEV_ONLY_PLACEHOLDER_KEY_…` value for
  local development. `Program.cs` **refuses to start** in any environment
  other than Development if the key is missing, is the placeholder, or is
  shorter than 32 characters.
- The same rule covers every other credential: `ConnectionStrings__Default`,
  `Database__Provider`, `Frontend__Origin`, `Storage__RootPath`,
  `ADMIN_PASSWORD`. Production values live in the hosting environment
  (Render dashboard / env vars), never in a committed file.
- Before every push, the source is scanned for hardcoded passwords, keys,
  tokens and API keys. The only key-like string in the repo is the dev
  placeholder above, which Production rejects at startup.

## Authorisation and workspace isolation

- Every document, workspace and archive operation goes through
  `IWorkspaceAccessService`: a user can only see or change workspaces they
  belong to, and only in the role they hold there.
- Roles: workspace Owner / Admin / Member, plus a system admin flag.
  Changing a member's role is an audited, authorised operation.
- Isolation is covered by automated tests that must pass before any push:
  `backend/tests/DocuArchive.Tests/IsolationTests.cs`,
  `AuthorizationTests.cs`, `SecurityAuditTests.cs`.

## HTTP security headers

`backend/src/WebApi/Middleware/SecurityHeadersMiddleware.cs` sets on every
response:

- `Content-Security-Policy: default-src 'self'; script-src 'self';
  style-src 'self' 'unsafe-inline'; img-src 'self' data:;
  font-src 'self' data:; connect-src 'self'; frame-ancestors 'none';
  base-uri 'self'; form-action 'self'`
- `X-Content-Type-Options: nosniff`, `X-Frame-Options: DENY`,
  `Referrer-Policy: no-referrer` (plus the remaining headers in that file).

## Uploads

- Maximum upload size **25 MB** (`Storage:MaxFileSizeBytes`, default
  25 × 1024 × 1024).
- The file extension must match its declared MIME type
  (`FileValidation.ExtensionMatchesMime`); mismatches are rejected.
  Uploads are streamed with a size cap — an over-limit body is rejected
  before it is stored.
- Files are stored on local disk under `Storage:RootPath` (default
  `storage/`), never executed, and served back only through authenticated
  endpoints. Previews (`GET …/documents/{id}/preview`) use inline
  disposition for PDF/PNG/JPEG/GIF/WebP only; TIFF/BMP are download-only.

## OCR and data residency

- OCR is **self-hosted Tesseract** (CPU-only, in-process worker). Document
  text never leaves the server for a third-party OCR or AI service.
- Default database is local **SQLite**; PostgreSQL is supported via
  `Database:Provider`. On the Render Free tier the SQLite file resets on
  redeploy — that is a known platform limit, not a data leak.

## Audit logging

Reads and writes that matter are audited, including document views
(`Document.Viewed`), uploads, category confirmations and role changes.
Audit entries record who did what, when, and in which workspace.

## Reporting a vulnerability

This is a solo-founder project with no bug-bounty programme. If you find a
security issue, do not open a public issue — contact the repository owner
directly so it can be fixed before it is disclosed.
