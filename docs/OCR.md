# DocuArchive OCR — Phase 3 receipt flow

## The story (what the promo video shows)

Someone scans a receipt. The software does everything:

1. **Upload** — photo/PDF of a receipt goes in (drag-drop or the Documents page).
2. **OCR reads it** — text appears, with a confidence score.
3. **Fields come out** — vendor, date, total (+ currency) are extracted automatically.
4. **Categorised** — suggested category "Receipt" (shown as a suggestion, never a fact).
5. **Archived + searchable** — the document is Ready in its workspace; the extracted text is stored and the dashboard counts it.

Demo receipt used for all of this: `docs/demo-receipt/demo-receipt.png` (+ `.pdf`) —
a fictional receipt for **THE CORNER CAFE, 24 Nov 2026, TOTAL £18.40**. No real personal data.
Tesseract reads it perfectly: all lines correct, mean word confidence **95.7 / 100**.

## Engine decision: Tesseract, not PaddleOCR (for now)

| | Tesseract 5.3 | PaddleOCR (self-hosted) |
|---|---|---|
| Licence / cost | Apache 2.0, free | Apache 2.0, free |
| Runs here | Yes — `apt-get install tesseract-ocr` (~30 MB), verified on the demo receipt | No — needs a Python sidecar + PaddlePaddle + model downloads, ~1 GB+ RAM |
| Fits Render Free (512 MB, one container) | Yes | No — sidecar + models do not fit |
| GitHub ubuntu runners | Preinstalled | Not installed; install would dominate the CI run |
| Receipt accuracy (clean print) | 95.7 mean confidence on the demo receipt | Better on handwriting / skewed photos, but unneeded for the demo |

**Decision:** `TesseractOcrEngine` is the registered `IOcrEngine`. `PaddleOcrEngine` stays an
explicit stub that throws with the reason — it is the future engine for handwriting/skewed
photos, behind a Python worker service, only if a bigger host is ever paid for. Nothing is
sent to any external service: OCR runs in a subprocess on the same machine.

## How the flow works

```
Upload (DocumentUploadService)
  → magic-byte validation, 25 MiB limit, SHA-256 duplicate → 409
  → Document row created with Status=Uploaded, OcrStatus=Queued
  → (optional) IOcrJobQueue enqueue — fast wake-up only; the DB status is the durable trigger

OcrWorker (BackgroundService)
  → drains the in-memory queue AND polls the DB for OcrStatus=Queued (every 10s, batch 10)
  → claims each document atomically (TryClaimForOcrAsync: only a still-Queued row flips to
    Processing, so two workers can never process the same document twice)
  → OcrProcessingService.ProcessAsync:
      IOcrEngine.ExtractTextAsync(storageKey)
      → success + text → ReceiptFieldExtractor → Ready / OcrStatus Completed
      → no text / engine error → Failed / OcrStatus Failed (never invented text)
      → audit log entry either way (Document.OcrCompleted / Document.OcrFailed)

Reprocess: POST /api/workspaces/{id}/documents/{docId}/reprocess (StandardUser+)
  → resets a Ready/Failed document to Queued, clears the old extraction, worker picks it up.
  → 400 when already Queued/Processing, 404 when the stored file is gone.
```

`DocumentDto` now carries the extraction: `ocrText`, `ocrConfidence`, `extractedVendor`,
`extractedDate`, `extractedTotal`, `extractedCurrency`, `suggestedCategory` (all null until OCR runs).

## OCR engine details (`TesseractOcrEngine`)

- Images: one `tesseract <file> stdout --psm 6 -l eng tsv` call; words are rebuilt into lines
  from the TSV and the mean word confidence (0–100) is reported.
- PDFs: `pdftotext -layout` first — embedded text is exact, so confidence is null by design.
  When the PDF has no embedded text (a scan), pages are rendered with `pdftoppm -png -r 300`
  and each page is OCR'd.
- Missing binary / timeout (120 s) / unreadable file → `Success=false`, no text, no exception
  into the worker. Arguments are passed as a list, never through a shell.
- Config (`Ocr` section in appsettings): `TesseractPath`, `PdftotextPath`, `PdftoppmPath`,
  `Languages` (default `eng`), `PageSegmentationMode` (default 6), `TimeoutSeconds` (120).

## Receipt extraction (`ReceiptFieldExtractor`, pure, no I/O)

- Vendor: first line that looks like a business name (letters, not a date/total/phone line).
- Date: `24 Nov 2026`, `12/11/2026`, `2026-11-24`.
- Total: amount on the `TOTAL` line (largest if several); otherwise the largest amount on the page.
  Currency is the symbol next to it (£, $, €).
- Category: `"Receipt"` is suggested only when a vendor AND a total were found AND the text
  contains a receipt keyword (total, VAT, paid, change, till, receipt). Anything else → null.
  Fields the text does not support stay null — never guessed.

## Setup the deploy needs (DevOps — Dockerfile)

~~The Docker image must install the OCR tools~~ — **done 2026-11-27 (Phase 3 builder)**:
the runtime stage installs `tesseract-ocr poppler-utils` (see Dockerfile). The CI
`docker` job builds the same image, so a missing binary is caught in CI, not on Render.
GitHub ubuntu runners already have both tools, so CI tests run real OCR.

## WebApi integration (backend fixer — 2 lines in `Program.cs`)

**Done 2026-11-27 (Phase 3 builder):** the worker runs inside the WebApi process — `Program.cs`
registers `IOcrJobQueue`/`ChannelOcrJobQueue` and `AddHostedService<OcrWorker>()` (plus a
`ProjectReference` to the Worker project), so uploads are processed in-process within ~10
seconds on the single-container Render deploy. The standalone Worker host is for separate
scaling only. The `reprocess` endpoint works either way (it resets to Queued; the worker —
whichever host runs it — picks the document up).

## Verified live (smoke test, 2026-11-27 00:43, local)

Real WebApi over HTTP + standalone worker host, same SQLite DB: register → login → create
workspace → upload `docs/demo-receipt/demo-receipt.png` → Queued → worker ran → document
`Ready` / `Completed`, confidence 95.71, vendor THE CORNER CAFE, date 2026-11-24, total £18.40,
suggestedCategory Receipt. Full suite: **37/37 tests pass** (26 pre-existing + 11 new receipt tests).

Worker robustness fix found during the smoke test: a claim that affects 0 rows (e.g. a row the
worker cannot match) used to count as "work", so the loop never slept and spun at ~1,600
DB commands/sec. `ProcessOneAsync` now returns whether it actually claimed the document, and
the poll interval is only skipped when real work happened. Note for anyone seeding test data
by hand: EF Core on SQLite stores Guids as 16-byte BLOBs in .NET byte order — rows inserted
as TEXT uuid strings will never match the claim's Guid parameters. Seed through EF or the API.

## What is still a stub / not done

- **PaddleOCR**: unwired stub (see decision above). No handwriting OCR yet.
- **Classification beyond receipts**: **done 2026-11-27** — `DocumentClassifier` (Application, pure,
  no external calls) scores Receipt, Invoice, Contract, BankStatement, Letter and Other from
  the OCR text with weighted keyword/regex signals and a 0–100 confidence. Below 60, or when
  nothing matches, the document is flagged `NeedsReview` and appears in the dashboard review
  queue; a human confirms or corrects the category via
  `POST /api/workspaces/{id}/documents/{documentId}/confirm-category`. New columns:
  `ClassificationConfidence`, `ClassificationReason`, `NeedsReview`, `ConfirmedCategory`
  (migration `20261127012706_AddClassification`). Tests: `ClassificationTests` (10 tests).
- **Full-text search over `OcrText`**: **done 2026-11-27** — `GET /api/workspaces/{id}/documents?search=...`
  matches file name, extracted vendor and the full OCR text (LIKE, workspace-scoped), and the
  Documents page has a search box wired to it. Test: `ReceiptOcrTests.Search_FindsDocumentByOcrTextAndFileName_NotByUnrelatedTerm`.
- **OCR fields in the Angular UI**: **done 2026-11-27** — the Documents page shows an expandable
  Details panel per document (vendor, date, total, suggested category, OCR confidence, extracted
  text), a "Retry OCR" button for failed documents, and auto-refreshes while a document is
  being processed so the row flips to Ready on its own.
- **`dotnet test` in this VM**: `vstest` cannot connect to the test host here (known VM issue);
  CI (GitHub runners) is the proof. Run tests with the direct test binary there.

## Promo video — shot list (free tools only)

Use the demo receipt in `docs/demo-receipt/` (PNG for the phone-scan shot, PDF for the upload shot).

1. Phone camera over the printed demo receipt (or the PNG fullscreen) — "Scan a receipt".
2. Upload in the app (Documents page → upload control) — the file lands as Queued.
3. The document row flips to Ready — show the API response / dashboard counts changing.
4. Close-up of the extracted fields: **THE CORNER CAFE · 24 Nov 2026 · £18.40 · Receipt**.
5. Search/archived line: "Archived. Searchable. Nothing typed by hand."
6. End card: "Scan it. Done." + the live link (once deployed).

Do NOT show: handwriting recognition, multi-page invoices, or a live OCR screen in the UI —
none of those exist yet.
