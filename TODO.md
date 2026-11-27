# TODO

## Phase 1 — in progress
- [ ] Backend API (.NET 10, EF Core, Identity, JWT, roles, workspace isolation)
- [ ] Angular app (login, register, dashboard shell, auth guard)
- [ ] Database migrations (PostgreSQL + SQLite)
- [ ] Docker + Docker Compose
- [ ] Swagger/OpenAPI
- [ ] Tests: auth, authorisation, workspace isolation — must pass before Phase 2
- [ ] README / SETUP / SECURITY / DEPENDENCIES docs

## Later phases
- [ ] Phase 2: uploads
- [x] Phase 3 (receipt flow only, 2026-11-27): local Tesseract OCR — upload → Queued → worker → Ready/Failed, receipt field extraction (vendor/date/total/currency, "Receipt" suggestion), reprocess endpoint. Engine decision + shot list in docs/OCR.md. PaddleOCR stays an unwired stub (needs a Python sidecar + ~1 GB RAM; does not fit Render Free). Worker wired inside the WebApi process (2026-11-27, was missing — uploads sat at Queued on Render), tesseract-ocr + poppler-utils in the Dockerfile runtime image, full-text search over OcrText (`?search=`, file name/vendor/OCR text) + OCR details panel, search box and Retry OCR in the Angular Documents page. Classification beyond receipts done 2026-11-27: rule+regex classifier (Receipt/Invoice/Contract/BankStatement/Letter/Other) with confidence + human-review queue (NeedsReview, confirm-category endpoint, dashboard "Needs review" card).
- [ ] Phase 4: organisation
- [ ] Phase 5: search
- [ ] Phase 6: viewer
- [ ] Phase 7: physical archive
- [ ] Phase 8: security hardening
- [ ] Phase 9: deployment
