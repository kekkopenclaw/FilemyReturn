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

The Docker image must install the OCR tools (the API currently fails OCR with `Success=false`
when they are absent — honest, but the demo does nothing). Add to the Dockerfile:

```dockerfile
RUN apt-get update && apt-get install -y --no-install-recommends tesseract-ocr poppler-utils \
    && rm -rf /var/lib/apt/lists/*
```

The CI `docker` job builds the same image, so a missing binary is caught in CI, not on Render.
GitHub ubuntu runners already have both tools, so CI tests run real OCR.

## WebApi integration (backend fixer — 2 lines in `Program.cs`)

The worker must also run inside the WebApi process, because the Render deploy is one container
(the standalone Worker host is for separate scaling only). In `WebApi/Program.cs`, after
`builder.Services.AddInfrastructure(...)`:

```csharp
builder.Services.AddSingleton<IOcrJobQueue, ChannelOcrJobQueue>();
builder.Services.AddHostedService<OcrWorker>();
// + usings: DocuArchive.Domain; DocuArchive.Worker;
```

Without this, uploads sit at Queued until the standalone worker runs; with it, uploads are
processed in-process within ~10 seconds. The `reprocess` endpoint works either way (it resets
to Queued; the worker — whichever host runs it — picks the document up).

## What is still a stub / not done

- **PaddleOCR**: unwired stub (see decision above). No handwriting OCR yet.
- **Full-text search over `OcrText`**: the text is stored, but no search endpoint queries it yet (Phase 4).
- **Classification beyond receipts**: only the "Receipt" suggestion exists; Invoice/Contract/etc.
  classification with confidence + human review is still to build (Phase 3 remainder).
- **No OCR in the Angular UI yet**: the API returns the fields; the frontend does not display
  them yet (the frontend fixer finished before this flow landed). The promo video should show
  the API / dashboard data, not a finished OCR screen — see shot list.
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
