using DocuArchive.Domain;
using DocuArchive.Infrastructure;
using DocuArchive.Infrastructure.Ocr;
using DocuArchive.Worker;

// Standalone OCR worker host (Phase 3). Wires the same Infrastructure the API uses
// (database, storage, OCR engine) so it can process queued documents independently.
// NOTE: this host must point at the SAME database as the API (ConnectionStrings__Default);
// in local dev both default to the SQLite file, in Production set the same env vars.
// The Render single-container deploy runs OCR inside the WebApi process instead (the
// WebApi host registers the same OcrWorker as a hosted service — see docs/OCR.md).
var builder = Host.CreateApplicationBuilder(args);
builder.Services.Configure<OcrWorkerOptions>(builder.Configuration.GetSection("Worker"));
builder.Services.AddSingleton<IOcrJobQueue, ChannelOcrJobQueue>();
builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddHostedService<OcrWorker>();

var host = builder.Build();
host.Run();
