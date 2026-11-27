using DocuArchive.Domain;
using DocuArchive.Infrastructure;
using DocuArchive.Infrastructure.Ocr;
using Microsoft.Extensions.Hosting;

namespace DocuArchive.Worker;

// Standalone OCR worker host (Phase 3). Wires the same Infrastructure the API uses
// (database, storage, OCR engine) so it can process queued documents independently.
// NOTE: this host must point at the SAME database as the API (ConnectionStrings__Default);
// in local dev both default to the SQLite file, in Production set the same env vars.
// The Render single-container deploy runs OCR inside the WebApi process instead (the
// WebApi host registers the same OcrWorker as a hosted service — see docs/OCR.md).
//
// The entry point is an explicit namespaced Program class (not top-level statements):
// top-level statements would generate a global `Program` type that collides with the
// WebApi's `Program` once WebApi references this project (CS0433 in the test project).
internal static class Program
{
    public static void Main(string[] args)
    {
        var builder = Host.CreateApplicationBuilder(args);
        builder.Services.Configure<OcrWorkerOptions>(builder.Configuration.GetSection("Worker"));
        builder.Services.AddSingleton<IOcrJobQueue, ChannelOcrJobQueue>();
        builder.Services.AddInfrastructure(builder.Configuration);
        builder.Services.AddHostedService<OcrWorker>();

        var host = builder.Build();
        host.Run();
    }
}
