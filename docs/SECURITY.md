# Security

How DocuArchive protects accounts, data and uploads. This document describes
what is implemented in the code today (27 November 2026); anything listed
here is verifiable in the repository, not aspirational. The "Not yet built"
section at the end lists the items that are deliberately open, with the
reason — nothing here is claimed that the code does not do.

## Authentication

- **ASP.NET Core Identity** for users, with **JWT Bearer** authentication.
- Access tokens last **15 minutes**; refresh tokens last **7 days**
  (`Jwt:AccessTokenMinutes` / `Jwt:RefreshTokenDays` in `appsettings.json`).
- Passwords are hashed by Identity (PBKDF2). No password is ever stored or
  logged in plain text.
- **Refresh tokens live in an HttpOnly cookie, never in localStorage.**
  Login and refresh set a `docuarchive.refresh` cookie
  (`HttpOnly`, `SameSite=Lax`, `Secure` outside Development,
  `Path=/api/auth`, 7-day `MaxAge`); the refresh token is **never** in a
  response body, and the frontend stores nothing (a legacy localStorage key
  is purged on startup). Refresh and logout read the cookie first, with a
  body fallback only for non-browser clients. Tokens are base64url, so the
  cookie value is never percent-encoded and always matches the stored hash.
- **Refresh-token rotation with reuse detection.** Every refresh rotates
  the token. Presenting an already-rotated (revoked) token is treated as
  theft: all refresh tokens for the account are revoked at once and the
  attempt is audited (`Auth.RefreshTokenReuse`).
- **Changing a password revokes all refresh tokens** for the account, so a
  token stolen before the change cannot survive it. (The 15-minute access
  tokens already issued expire on their own and cannot be minted anew.)
- **Brute-force protection.** Auth endpoints are rate-limited to **30
  requests/minute** per IP + account (see below), and Identity locks an
  account after **5 failed logins** (HTTP 423).
- **Errors do not enumerate accounts.** Login, registration, password
  reset and first-login password change all return the same generic error
  whether the account exists or not, so the API cannot be used to probe
  which emails have accounts.

## Rate limiting

`backend/src/WebApi/Program.cs` (fixed-window, no queue, HTTP 429 on
rejection):

- Default policy: **120 requests/minute per client IP**.
- Auth policy (login / register / password change): **30
  requests/minute**, partitioned per IP and per account email where the
  request carries one — the brute-force control for credential-taking
  endpoints.
- Refresh policy: **120 requests/minute per client IP**. Refresh used to
  share the auth policy, but a refresh request carries no email, so every
  refresh from one IP fell in a single IP-only partition: 30 refreshes in
  a minute (one household, one office NAT, or a test suite that reloads
  the page a lot — every reload silently refreshes, because the access
  token lives in memory only) answered 429, which the frontend reads as
  "session dead" and logs the user out. Refresh takes no credentials — it
  presents an already-issued rotating token — so the controls there are
  token entropy, rotation, reuse detection and family revocation, and the
  bucket is the general limit. Regression-tested
  (`RefreshRateLimitTests`): 40 unauthenticated refreshes are all 401,
  never 429, while a 31st login in a minute is still 429.

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
- Roles: `ReadOnly` / `StandardUser` / `ArchiveManager` /
  `WorkspaceAdmin` / `WorkspaceOwner`, plus a `SystemAdmin` flag on the
  user. The flag does **not** grant workspace access — a system admin
  still needs an explicit membership in each workspace. Changing a
  member's role and removing a member require `WorkspaceAdmin` or above,
  and only the owner can remove or re-role the owner; the
  `WorkspaceOwner` role can never be assigned through a role change.
- Workspace deletion calls `DeleteAsync` on the workspace (owner only):
  the workspace row, its documents, archives, tags, rules and memberships
  are removed and the stored files deleted — but audit entries **survive**
  (foreign key `SetNull`, not `Cascade`), so deleting a workspace does not
  erase its audit trail, including the `Workspace.Deleted` record itself.
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
- **Duplicate uploads are rejected with HTTP 409.** Every file is hashed
  (SHA-256) and an exact duplicate in the same workspace is refused, even
  under a race (the unique index wins and the second upload gets 409).
- Files are stored on local disk under `Storage:RootPath` (default
  `storage/`), never executed, and served back only through authenticated
  endpoints. Previews (`GET …/documents/{id}/preview`) use inline
  disposition for PDF/PNG/JPEG/GIF/WebP only; TIFF/BMP are download-only.

## OCR and data residency

- OCR is **self-hosted Tesseract** (CPU-only, in-process worker). Document
  text never leaves the server for a third-party OCR or AI service.

## Database — SQLite is demo-only

- The default database is local **SQLite**. It is fine for local
  development and for the live demo, but it is **not a production
  database**: on the Render Free tier the SQLite file lives on ephemeral
  disk and **resets on every redeploy** — uploads and accounts created on
  the live site do not survive a deploy.
- A production deployment must use **PostgreSQL**. Locally, Postgres is
  behind the `postgres` profile in `docker-compose.yml` — it never starts
  unless asked for (`docker compose --profile postgres up`, with
  `DATABASE_PROVIDER=Postgres` and a matching `CONNECTION_STRING` in
  `.env`); the default `docker compose up` run is SQLite-only and needs no
  Postgres password.
- The runtime image runs as a **non-root user** (`USER app` in the
  `Dockerfile`).

## Backups

- A system administrator can download a complete backup — a consistent
  database snapshot plus every stored file, with a manifest — from the
  **Backup** page (`GET /api/admin/backup/download`). Every download is
  audited as `Backup.Downloaded` (no workspace: a backup spans all of
  them). The ZIP is built in a temp directory that is deleted after the
  download, and the database snapshot is taken with SQLite `VACUUM INTO`,
  which does not disturb the live database.
- There is deliberately **no in-app restore endpoint**: uploading a
  database file to overwrite production data is too big a security
  surface. Restore is a manual, admin-only procedure — see
  `docs/BACKUP.md`.
- On the Render Free tier the database and the uploaded files live on
  ephemeral disk and are wiped on every redeploy, so a backup only helps
  if it is downloaded **off the server** — before every deploy, at
  least. A backup holds every workspace's documents: treat the ZIP like a
  password (encrypted storage, never emailed or shared).
- In-app backup is SQLite-only. On PostgreSQL the download answers 501
  and the database must be backed up with `pg_dump` or the provider's own
  backup feature — also `docs/BACKUP.md`.

## Audit logging

Reads and writes that matter are audited — who did what, when, and in
which workspace. The audited actions include:

- Auth: `Auth.Registered`, `Auth.Login`, `Auth.LoginFailed`,
  `Auth.Refresh`, `Auth.RefreshTokenReuse`, `Auth.PasswordChanged`.
- Documents: `Document.Uploaded`, `Document.Downloaded`,
  `Document.DownloadFailed` (stored file missing — the row exists but the
  bytes are gone), `Document.Viewed`, `Document.ViewFailed`,
  `Document.MetadataUpdated` (only when something actually changes),
  `Document.CategoryConfirmed`, `Document.Tagged`, `Document.Untagged`,
  `Document.RuleFiled`, `Document.RuleEvaluationFailed`,
  `Document.OcrCompleted`, `Document.OcrFailed`,
  `Document.OcrReprocessRequested`.
- Organisation: `Tag.Created`, `Tag.Deleted`, `FilingRule.Created`,
  `FilingRule.Updated`, `FilingRule.Deleted`.
- Workspaces: `Workspace.Created`, `Workspace.Deleted`,
  `Workspace.MemberAdded`, `Workspace.MemberRemoved`,
  `Workspace.MemberRoleChanged`, `Workspace.AccessDenied`.
- Archives: `Archive.Created`.
- Admin: `Backup.Downloaded`.

Audit entries survive workspace deletion (see above) and are read-only —
there is no edit or delete endpoint for the audit log.

## Not yet built (deliberately open)

- **Email verification on registration.** Not built: it needs a real
  email provider account (only a log-only `LogEmailService` exists for
  development). Until one is connected, registration does not send mail
  and accounts are usable immediately.
- **CAPTCHA on registration.** Not built: it needs hCaptcha/Turnstile
  keys, which only the repository owner can provide. Registration is
  protected meanwhile by the auth rate limit and Identity lockout above.
- Both items are blocked on accounts/keys only the owner can supply — see
  `TODO.md`. Nothing else on the security list is open.

## Reporting a vulnerability

This is a solo-founder project with no bug-bounty programme. If you find a
security issue, do not open a public issue — contact the repository owner
directly so it can be fixed before it is disclosed.
