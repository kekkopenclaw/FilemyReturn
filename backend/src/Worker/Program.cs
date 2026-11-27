using DocuArchive.Domain;
using DocuArchive.Worker;

var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddSingleton<IOcrJobQueue, ChannelOcrJobQueue>();
builder.Services.AddHostedService<OcrWorker>();

var host = builder.Build();
host.Run();
