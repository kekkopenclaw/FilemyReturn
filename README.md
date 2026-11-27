# DocuArchive — Secure AI Document Digitisation & Archive Management Platform

A commercial-quality, open-source platform for digitising, OCR-ing, indexing and
archiving documents — built for small businesses, archive companies and
public-sector organisations.

**Live:** https://docuarchive.onrender.com — `/health` returns 200 `Healthy`.
Render Free (Frankfurt, Docker, SQLite). The free plan sleeps after 15 minutes
idle, so the first load can be slow.

**Stack:** .NET 10 LTS (ASP.NET Core Web API, EF Core, Identity + JWT) +
Angular 22 + PostgreSQL / SQLite + Docker. OCR is **Tesseract** (free,
CPU-only, self-hosted, runs in-process) — see `docs/OCR.md` for the engine
decision. PaddleOCR stays an unwired stub: it needs a Python sidecar and
~1 GB RAM, which does not fit Render Free.

**Budget:** £0 — free, open-source software only. No paid cloud services, no
paid APIs, no proprietary AI. Documents are never sent to external providers
for OCR.

## Status — live

- **Phase 1 — Foundation:** done. Clean Architecture backend, Angular app
  (landing first, then login/register, dashboard shell, auth guard), EF Core
  migrations (PostgreSQL + SQLite), Docker + Docker Compose, Swagger/OpenAPI,
  auth/authorisation/workspace-isolation tests.
- **Phase 2 — Uploads:** done. Multipart upload with magic-byte validation
  (PDF/PNG/JPEG/GIF/TIFF/BMP/WebP, 25 MiB limit), per-workspace SHA-256
  duplicate detection, secure download, upload history.
- **Phase 3 — OCR + classification:** done. Upload → Queued → worker →
  Ready/Failed, receipt field extraction (vendor/date/total/currency),
  reprocess endpoint, full-text search over OCR text (search box on the
  Documents page), rule+regex document classifier
  (Receipt/Invoice/Contract/BankStatement/Letter/Other) with confidence and a
  human-review queue.
- **Design:** the live site uses the cream base (light warm off-white
  `#fcfcf9` + teal green `#0e7c66`) from the DocuArchive preview. A futuristic
  3D cinematic concept lives as a separate `/concept` route in the Angular app
  and is a preview only until approved.

See `TODO.md` for what remains (Phase 4 organisation, Phase 5 search,
Phase 6 viewer, Phase 7 physical archive, Phase 8 security hardening,
Phase 9 deployment) and `docs/ARCHITECTURE.md` for the plan.

## How to run

```bash
docker compose up --build
```

API: http://localhost:5000/swagger (Development) · Web: http://localhost:4200

Local dev without Docker is documented in `backend/README.md`.

## Tests

Unit + integration tests (xUnit v3, run via Microsoft.Testing.Platform — see
the test-runner note in `backend/README.md`), plus Playwright end-to-end
tests in `e2e/`. Nothing is pushed until `dotnet build` (0 errors),
`dotnet test`, `ng build` and a security check all pass — see
`WORKING-RULES.md`.

## Security

Workspace-isolated, role-based access, no secrets in source code.
`ADMIN_PASSWORD` and the JWT key come from environment variables only; the
API refuses to start in Production with the dev placeholder key. Every
dependency is free and open-source — the full list with licences is in
`backend/README.md`.

## Licence

Free and open-source dependencies only.
