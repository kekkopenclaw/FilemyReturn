# Secure AI Document Digitisation & Archive Management Platform

A commercial-quality, open-source MVP for digitising, OCR-ing, indexing and archiving documents — built for small businesses, archive companies and public-sector organisations.

**Stack:** .NET 10 LTS (ASP.NET Core Web API, EF Core, Identity) + Angular (latest stable) + PostgreSQL / SQLite + Docker Compose + PaddleOCR (self-hosted, Phase 3)

**Budget:** £0 — free, open-source software only. No paid cloud services, no paid APIs, no proprietary AI. Documents are never sent to external providers for OCR.

## Status — Phase 1 in progress

Phase 1: Foundation — architecture, repository structure, Docker, backend, Angular, database, authentication.

This initial commit proves the GitHub push works. Full Phase 1 code, tests and docs are being built now, and will be pushed next once Phase 1 builds and its tests pass.

See `docs/ARCHITECTURE.md` for the Phase 1 plan, and `TODO.md` for what remains.

## How to run (Phase 1 target)

```bash
docker compose up --build
```

API: http://localhost:5000/swagger
Web: http://localhost:4200

Local dev without Docker is documented in `SETUP.md` once Phase 1 lands.

## Security

Workspace-isolated, role-based access, no secrets in source code. See `SECURITY.md` once Phase 1 lands.

## Licence

Free and open-source dependencies only — see `DEPENDENCIES.md` once Phase 1 lands.
