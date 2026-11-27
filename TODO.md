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
- [ ] Phase 4: organisation — tags done 2026-11-27 (workspace tags: create/list/delete, assign/unassign per document, tag filter on the list; backend 12/12 TagTests + Angular UI + Vitest 7 + Playwright E2E tag flow). Filing rules done 2026-11-27 (workspace rules: vendor/category/file-name conditions, assign tag / set category / move to archive actions, priority order, auto-run after OCR + manual apply endpoint; backend 21/21 FilingRuleTests + Angular UI + Vitest 8 + Playwright E2E rule flow). Metadata editing done 2026-11-27 (PUT …/documents/{id}/metadata: file name, vendor, date, total, category — full-form PUT, null clears vendor/date/total, known category counts as confirmed and clears NeedsReview, null category clears the confirmed category; validation on file name/date/total/category; StandardUser+; audited as Document.MetadataUpdated only when something changes; Angular edit form in the document details panel; backend 13/13 MetadataEditingTests + Vitest 3 + Playwright E2E metadata flow incl. 390px phone).
- [ ] Phase 5: search
- [ ] Phase 6: viewer — backend preview endpoint done 2026-11-27 (`GET …/documents/{id}/preview`, inline disposition, PDF/PNG/JPEG/GIF/WebP, audited as Document.Viewed; TIFF/BMP stay download-only); Angular in-app viewer UI done 2026-11-27 (DocumentViewer modal: PDF in iframe, images in `<img>`, 400 → "can't preview" + Download, Escape/backdrop/Close, preview fetched via authenticated HttpClient so the Bearer token travels; View button in the document list opens it instead of a new tab; 5 Vitest unit tests + Playwright E2E viewer test)
- [ ] Phase 7: physical archive
- [ ] Phase 8: security hardening
- [ ] Phase 9: deployment
