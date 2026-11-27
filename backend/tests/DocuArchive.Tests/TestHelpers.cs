using System.Net.Http.Headers;
using System.Net.Http.Json;
using DocuArchive.Domain;
using DocuArchive.Infrastructure.Data;
using Microsoft.Extensions.DependencyInjection;

namespace DocuArchive.Tests;

/// <summary>Shared HTTP + DB helpers for the API tests. Each test uses unique emails so tests sharing one factory DB stay independent.</summary>
public static class TestHelpers
{
    public const string Password = "Password123!@#";

    public static string UniqueEmail(string prefix) => $"{prefix}-{Guid.NewGuid():N}@example.com";

    public record AuthSession(string Email, string UserId, string AccessToken, string RefreshToken);

    public static async Task<AuthSession> RegisterAndLoginAsync(HttpClient client, string email, string? password = null)
    {
        var register = await client.PostAsJsonAsync("/api/auth/register",
            new { Email = email, Password = password ?? Password, DisplayName = "Test User" });
        register.EnsureSuccessStatusCode();
        var login = await client.PostAsJsonAsync("/api/auth/login",
            new { Email = email, Password = password ?? Password });
        login.EnsureSuccessStatusCode();
        var body = await login.Content.ReadFromJsonAsync<AuthBody>() ?? throw new InvalidOperationException("Empty login response.");
        return new AuthSession(email, body.User.Id, body.AccessToken, body.RefreshToken);
    }

    public static HttpClient Authed(HttpClient client, string token)
    {
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    public static HttpClient ClientFor(CustomWebApplicationFactory factory, string? token = null)
    {
        var client = factory.CreateClient();
        if (token is not null) Authed(client, token);
        return client;
    }

    public static async Task<Guid> CreateWorkspaceAsync(HttpClient client, string name = "Workspace")
    {
        var response = await client.PostAsJsonAsync("/api/workspaces", new { Name = name, Description = "test" });
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<WorkspaceBody>() ?? throw new InvalidOperationException("Empty workspace response.");
        return body.Id;
    }

    public static async Task AddMemberAsync(HttpClient client, Guid workspaceId, string email, string role)
    {
        var response = await client.PostAsJsonAsync($"/api/workspaces/{workspaceId}/members", new { Email = email, Role = role });
        response.EnsureSuccessStatusCode();
    }

    public static int CountAudit(CustomWebApplicationFactory factory, string userId, string action)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return db.AuditLogs.Count(a => a.UserId == userId && a.Action == action);
    }

    public static async Task<Guid> SeedDocumentAsync(CustomWebApplicationFactory factory, Guid workspaceId, string userId)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var doc = new Document
        {
            WorkspaceId = workspaceId,
            OriginalFileName = "test.pdf",
            StorageKey = $"{Guid.NewGuid():N}.pdf",
            Sha256Hash = Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N")[..8],
            FileSizeBytes = 1234,
            MimeType = "application/pdf",
            CreatedById = userId
        };
        db.Documents.Add(doc);
        await db.SaveChangesAsync();
        return doc.Id;
    }

    private record AuthBody(string AccessToken, string RefreshToken, UserBody User);
    private record UserBody(string Id, string Email);
    private record WorkspaceBody(Guid Id);
}
