# DocuArchive E2E tests (Playwright)

End-to-end tests for DocuArchive. Free and open-source (Playwright + Chromium).
They run the **real app locally** — never the live site.

## Setup (once)

```bash
cd e2e
npm install
npx playwright install chromium
```

## Run

```bash
cd e2e
npm test
```

What happens: `start-app.sh` builds the Angular frontend, serves it from the
API's `wwwroot` (the production layout) and starts the API on
http://localhost:5000 with a throwaway SQLite database and storage dir under
`/tmp/docuarchive-e2e` (deleted at the start of every run). Playwright waits
for `/health`, then runs the specs.

## Specs

- `landing.spec.ts` — public landing page, the "Watch me scan" demo, real
  Log in / Get started links. Style-agnostic: it checks the demo works, not
  which colours it uses.
- `auth.spec.ts` — register then login reaches the dashboard; a wrong password
  shows an error and stays on login.
- `documents.spec.ts` — create a workspace, upload the demo receipt
  (`fixtures/demo-receipt.png`, fictional THE CORNER CAFE), wait for it to
  reach Ready, search finds it by OCR text ("flat white"), an unrelated term
  ("plumber") finds nothing.

## Notes

- CI does not run these yet: `.github/workflows/ci.yml` has no Playwright
  job. Adding one is a workflow-file change for the parent agent, not this
  folder — these tests are run locally.
- The fixture receipt is the same fictional receipt the backend tests use.
