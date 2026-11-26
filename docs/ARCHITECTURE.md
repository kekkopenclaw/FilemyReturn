# Architecture — Phase 1

## Stack
- Backend: ASP.NET Core 10 LTS Web API, C#, EF Core, ASP.NET Core Identity + JWT
- Frontend: Angular latest stable, TypeScript, standalone components, reactive forms
- Database: PostgreSQL (production), SQLite (local dev)
- Infrastructure: Docker, Docker Compose, local file storage, S3-compatible abstraction
- OCR: PaddleOCR, self-hosted — Phase 3, via `IOcrEngine` abstraction

## Phase plan
1. Foundation — architecture, Docker, backend, Angular, database, auth
2. Document uploads — storage, validation, listing, secure download
3. OCR — PaddleOCR, background processing, results, retries
4. Organisation — categories, metadata, folders, tags, rules
5. Search — PostgreSQL full-text search, filters, snippets
6. Document viewer — PDF/image, metadata editing, OCR review
7. Archive management — physical location, QR codes, boxes
8. Security hardening — audit, tests, isolation, backup
9. Deployment — production config, HTTPS, backups, monitoring

## Rules
No fake functionality, no buttons that do nothing, no mock data in production, no paid APIs, no secrets in code, workspace isolation enforced server-side on every endpoint.
