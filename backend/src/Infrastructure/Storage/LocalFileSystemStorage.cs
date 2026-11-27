using DocuArchive.Domain;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace DocuArchive.Infrastructure.Storage;

/// <summary>
/// Local filesystem storage (Phase 1). Files are stored under the configured
/// <c>Storage__RootPath</c> under random GUID filenames — the user-supplied name is never
/// used as a path, so path traversal is impossible. TODO (Phase 2): S3-compatible
/// implementation behind the same <see cref="IDocumentStorage"/> interface.
/// </summary>
public class LocalFileSystemStorage : IDocumentStorage
{
    private readonly string _rootPath;
    private readonly ILogger<LocalFileSystemStorage> _logger;

    public LocalFileSystemStorage(IConfiguration configuration, ILogger<LocalFileSystemStorage> logger)
    {
        _rootPath = configuration["Storage:RootPath"]
            ?? Path.Combine(AppContext.BaseDirectory, "storage");
        _logger = logger;
        Directory.CreateDirectory(_rootPath);
    }

    /// <inheritdoc/>
    public async Task<string> SaveAsync(Stream content, string originalFileName, string contentType, CancellationToken ct = default)
    {
        // Random GUID filename + the original extension only (sanitised); never the raw user name.
        var ext = Path.GetExtension(originalFileName);
        if (ext.Length > 10 || ext.Contains('/') || ext.Contains('\\')) ext = string.Empty;
        var key = $"{Guid.NewGuid():N}{ext}";
        var fullPath = Path.Combine(_rootPath, key);
        await using var file = new FileStream(fullPath, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        await content.CopyToAsync(file, ct);
        _logger.LogInformation("Stored file {Key} ({ContentType})", key, contentType);
        return key;
    }

    /// <inheritdoc/>
    public Task<Stream?> OpenReadAsync(string storageKey, CancellationToken ct = default)
    {
        // Guard: keys are GUID filenames only; reject anything containing a directory separator.
        if (string.IsNullOrWhiteSpace(storageKey) || storageKey.Contains('/') || storageKey.Contains('\\') || storageKey.Contains(".."))
            return Task.FromResult<Stream?>(null);
        var fullPath = Path.Combine(_rootPath, storageKey);
        if (!File.Exists(fullPath)) return Task.FromResult<Stream?>(null);
        return Task.FromResult<Stream?>(new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.Read));
    }

    /// <inheritdoc/>
    public Task DeleteAsync(string storageKey, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(storageKey) || storageKey.Contains('/') || storageKey.Contains('\\') || storageKey.Contains(".."))
            return Task.CompletedTask;
        var fullPath = Path.Combine(_rootPath, storageKey);
        if (File.Exists(fullPath)) File.Delete(fullPath);
        return Task.CompletedTask;
    }
}
