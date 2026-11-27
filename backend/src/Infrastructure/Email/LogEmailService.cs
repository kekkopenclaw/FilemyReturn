using DocuArchive.Domain;
using Microsoft.Extensions.Logging;

namespace DocuArchive.Infrastructure.Email;

/// <summary>
/// Log-only email provider (Phase 1). No SMTP account and no paid email service is
/// configured; messages are written to the application log. Swap for a real provider
/// (e.g. SMTP) behind the same <see cref="IEmailService"/> interface when needed.
/// </summary>
public class LogEmailService : IEmailService
{
    private readonly ILogger<LogEmailService> _logger;
    public LogEmailService(ILogger<LogEmailService> logger) => _logger = logger;

    /// <inheritdoc/>
    public Task SendAsync(string to, string subject, string body, CancellationToken ct = default)
    {
        _logger.LogInformation("Email (log provider, not sent): To={To} Subject={Subject}", to, subject);
        return Task.CompletedTask;
    }
}
