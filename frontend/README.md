# DocDigit Frontend — Phase 1

Angular 22.3.0 (standalone components, reactive forms, signals) for the Secure
AI Document Digitisation & Archive Management Platform.

## Run locally

```bash
export TMPDIR=$HOME/.tmp   # /tmp is a small tmpfs; use a home-dir temp dir
npm install
ng serve                   # http://localhost:4200, /api proxied to localhost:5000
```

`proxy.conf.json` forwards `/api` to the backend at `http://localhost:5000`
(see `src/environments/environment.ts`). Production builds use the relative
`/api` path (`src/environments/environment.prod.ts`).

## Build & test

```bash
ng build        # production build -> dist/frontend
ng test         # Vitest, headless (jsdom) — no browser needed
```

`ng lint` is not configured (no ESLint in the scaffold); linting is a later
hardening item, not a Phase 1 blocker.

## Structure

- `src/app/core/` — `ApiService` (typed API calls), `AuthService` (access
  token in memory only; refresh token in localStorage), `auth.interceptor.ts`
  (Bearer + one 401 refresh retry), `auth.guard.ts`, `WorkspaceService`
  (selected workspace), `models.ts` (API contract types)
- `src/app/features/auth/` — login + register pages
- `src/app/features/dashboard/` — workspace stats + recent documents (real
  API data only; empty states when the API returns nothing)
- `src/app/features/workspaces/` — workspace list + create form
- `src/app/features/documents/` — document list with status badges
  (no upload button — uploads arrive in Phase 2)
- `src/app/shared/` — app shell (top nav + sidebar), loading spinner,
  empty state, workspace selector

## Rules (Francesco's hard rule)

100% free and open source only — no paid npm packages, no proprietary UI
kits, no licence keys. Every dependency is MIT, Apache-2.0 or 0BSD (see
`package.json`; full list with licences in the project `DEPENDENCIES.md`).
No mock or hardcoded document data in production code; no secrets in code.
