using System.Threading.Channels;
using DocuArchive.Application;
using DocuArchive.Domain;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DocuArchive.Worker;

/// <summary>
/// Channel-based OCR job queue (Phase 3). The queue is an in-process wake-up signal only;
/// the durable trigger is the document's <see cref="OcrStatus.Queued"/> status in the database,
/// so a restart (or a second process) never loses a job.
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

    /// <summary>Non-blocking drain attempt: null when the queue is empty right now.</summary>
    public bool TryDequeue(out OcrJob? job) => _channel.Reader.TryRead(out job);
}

/// <summary>Worker options, bound from the <c>Worker</c> configuration section.</summary>
public class OcrWorkerOptions
{
    /// <summary>How often the worker polls the database for Queued documents. Default 10 seconds.</summary>
    public int PollIntervalSeconds { get; set; } = 10;
    /// <summary>Max queued documents picked up per poll. Default 10.</summary>
    public int MaxBatchSize { get; set; } = 10;
}

/// <summary>
/// Real OCR worker (Phase 3). Two triggers, one claim path:
/// <list type="bullet">
/// <item>Drains the in-memory <see cref="IOcrJobQueue"/> (fast path when the API and worker share a process).</item>
/// <item>Polls the database for <see cref="OcrStatus.Queued"/> documents (cross-process path + recovery after a restart).</item>
/// </list>
/// Every document is claimed atomically (<see cref="IDocumentRepository.TryClaimForOcrAsync"/>)
/// before processing, so two workers — or the channel and the poller — can never process the
/// same document twice. The worker never crashes on a bad document: failures are recorded on
/// the document itself (Status Failed, OcrStatus Failed) and processing continues.
/// The worker runs as the system account for audit purposes; it never invents OCR text.
/// </summary>
public class OcrWorker : BackgroundService
{
    private readonly IOcrJobQueue _queue;
    private readonly IServiceScopeFactory _scopes;
    private readonly OcrWorkerOptions _options;
    private readonly ILogger<OcrWorker> _logger;

    public OcrWorker(
        IOcrJobQueue queue,
        IServiceScopeFactory scopes,
        IOptions<OcrWorkerOptions> options,
        ILogger<OcrWorker> logger)
    {
        _queue = queue;
        _scopes = scopes;
        _options = options.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("OcrWorker started (poll every {PollSeconds}s, batch {BatchSize}).",
            _options.PollIntervalSeconds, _options.MaxBatchSize);
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var didWork = false;
                // Fast path: drain whatever the in-process queue holds right now.
                while (_queue is ChannelOcrJobQueue channel && channel.TryDequeue(out var job) && job is not null)
                {
                    didWork |= await ProcessOneAsync(job.WorkspaceId, job.DocumentId, stoppingToken);
                }
                // Durable path: poll the database for Queued documents (cross-process + recovery).
                using (var scope = _scopes.CreateScope())
                {
                    var documents = scope.ServiceProvider.GetRequiredService<IDocumentRepository>();
                    foreach (var queued in await documents.ListQueuedForOcrAsync(_options.MaxBatchSize, stoppingToken))
                    {
                        didWork |= await ProcessOneAsync(queued.WorkspaceId, queued.Id, stoppingToken);
                    }
                }
                if (!didWork)
                    await Task.Delay(TimeSpan.FromSeconds(_options.PollIntervalSeconds), stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "OcrWorker poll cycle failed; retrying.");
                await Task.Delay(TimeSpan.FromSeconds(_options.PollIntervalSeconds), stoppingToken);
            }
        }
        _logger.LogInformation("OcrWorker stopped.");
    }

    /// <summary>Processes one document. Returns true only when this worker actually claimed
    /// and processed it — a failed claim (already claimed elsewhere, or a row this worker
    /// cannot match) is NOT work, so the caller still sleeps the poll interval instead of
    /// spinning on the same row.</summary>
    private async Task<bool> ProcessOneAsync(Guid workspaceId, Guid documentId, CancellationToken ct)
    {
        using var scope = _scopes.CreateScope();
        var processing = scope.ServiceProvider.GetRequiredService<OcrProcessingService>();
        // The worker is not a user: audit entries are attributed to the system account.
        var claimed = await processing.ProcessAsync(workspaceId, documentId, "system:ocr-worker", ct);
        if (!claimed)
            _logger.LogDebug("OCR job for document {DocumentId} was already claimed; skipping.", documentId);
        return claimed;
    }
}
