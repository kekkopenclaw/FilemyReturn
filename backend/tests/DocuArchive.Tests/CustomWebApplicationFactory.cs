using DocuArchive.Infrastructure.Data;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace DocuArchive.Tests;

/// <summary>Test web app: Testing environment (no seed data), SQLite in-memory DB shared per factory instance.</summary>
public class CustomWebApplicationFactory : WebApplicationFactory<Program>
{
    private readonly SqliteConnection _connection = new("Data Source=:memory:");
    private readonly string _storagePath = Path.Combine(Path.GetTempPath(), "docuarchive-tests", Guid.NewGuid().ToString("N"));

    public CustomWebApplicationFactory()
    {
        _connection.Open();
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        // Test overrides: tiny upload limit (1 KiB) so the oversize test stays cheap,
        // and an isolated temp storage root so upload tests never touch the repo.
        builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(
            new Dictionary<string, string?>
            {
                ["Storage:MaxFileSizeBytes"] = "1024",
                ["Storage:RootPath"] = _storagePath,
                // Test-only JWT key: Program.cs refuses to start outside Development with
                // the DEV_ONLY placeholder, so tests must supply a real (test-only) key.
                ["Jwt:Key"] = "TestOnlyJwtKeyForIntegrationTests0123456789abcdef",
                // The in-process OCR worker polls every 10s in production; in tests the
                // poll is effectively disabled so tests drive processing explicitly via
                // OcrProcessingService (otherwise the worker could claim a seeded document
                // before the test's direct ProcessAsync call and make its result flaky).
                // That the worker is wired in at all is proven by Worker_IsRegistered below.
                ["Worker:PollIntervalSeconds"] = "3600"
            }));
        builder.ConfigureServices(services =>
        {
            services.RemoveAll(typeof(DbContextOptions<AppDbContext>));
            services.RemoveAll(typeof(AppDbContext));
            services.AddDbContext<AppDbContext>(options => options.UseSqlite(_connection));
        });
    }

    /// <summary>Creates the schema in the shared in-memory database. Call once per test class setup.</summary>
    public void EnsureCreated()
    {
        using var scope = Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.EnsureCreated();
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing) _connection.Dispose();
    }
}
