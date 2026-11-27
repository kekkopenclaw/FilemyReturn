using System.Threading.Channels;
using DocuArchive.Domain;

namespace DocuArchive.Worker;

/// <summary>
/// Channel-based OCR job queue (Phase 3). Phase 1: registration + queue only — no OCR
/// processing happens here. The worker dequeues jobs and logs that OCR is not
/// implemented yet; it never fabricates results.
/// </summary>
public class ChannelOcrJobQueue : IOcrJobQueue
{
    private readonly Channel<OcrJob> _channel = Channel.CreateUnbounded<OcrJob>();

    public ValueTask EnqueueAsync(OcrJob job, CancellationToken cancellationToken = default)
        => _channel.Writer.WriteAsync(job, cancellationToken);

    public async Task<OcrJob?> DequeueAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            return await _channel.Reader.ReadAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            return null;
        }
    }
}

/// <summary>Placeholder OCR worker (Phase 3). Registered as a BackgroundService; does no processing in Phase 1.</summary>
public class OcrWorker : BackgroundService
{
    private readonly IOcrJobQueue _queue;
    private readonly ILogger<OcrWorker> _logger;

    public OcrWorker(IOcrJobQueue queue, ILogger<OcrWorker> logger)
    {
        _queue = queue;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("OcrWorker started. OCR processing is Phase 3 and is not implemented yet.");
        while (!stoppingToken.IsCancellationRequested)
        {
            var job = await _queue.DequeueAsync(stoppingToken);
            if (job is null) break;
            // TODO (Phase 3): resolve IOcrEngine (PaddleOCR) and process job.DocumentId.
            // Phase 1 deliberately does nothing with the job and never invents OCR text.
            _logger.LogWarning("OCR job received for document {DocumentId} but OCR is not implemented (Phase 3). Job ignored.", job.DocumentId);
        }
    }
}
