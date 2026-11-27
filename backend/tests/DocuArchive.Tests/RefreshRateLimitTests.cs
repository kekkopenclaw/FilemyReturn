using System.Net;
using System.Net.Http.Json;

namespace DocuArchive.Tests;

/// <summary>
/// Regression tests for the 2026-11-27 refresh rate-limit bug. Refresh used
/// to share the strict "auth" policy (30/min, partitioned by IP + account
/// email) — but a refresh request carries no email, so every refresh from
/// one IP fell in a single IP-only partition. Thirty refreshes in a minute
/// (one household, one office NAT, or a test suite that reloads the page a
/// lot — every reload silently refreshes, because the access token lives in
/// memory only) answered 429, which the frontend reads as "session dead"
/// and logs the user out. The full E2E suite failed the same two tests with
/// a login-page screenshot, and a direct probe of POST /api/auth/refresh
/// returned 30x 401 then 429.
/// Refresh now has its own "refresh" policy (120/min per IP): it takes no
/// credentials, it presents an already-issued rotating token, and the real
/// controls are token entropy, rotation, reuse detection and family
/// revocation. The credential-taking endpoints keep the strict bucket — the
/// second test proves login is still limited.
/// </summary>
public class RefreshRateLimitTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;

    public RefreshRateLimitTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Refresh_IsNotRateLimitedAtThirtyPerMinute()
    {
        var client = _factory.CreateClient();

        // 40 unauthenticated refresh attempts in one burst: without a token
        // every one is a 401. Before the fix the 31st onwards came back 429.
        for (var i = 0; i < 40; i++)
        {
            var res = await client.PostAsJsonAsync(
                "/api/auth/refresh", new { RefreshToken = "" });
            Assert.Equal(HttpStatusCode.Unauthorized, res.StatusCode);
        }
    }

    [Fact]
    public async Task Login_IsStillRateLimitedAtThirtyPerMinute()
    {
        var client = _factory.CreateClient();

        // 31 login attempts for the same (nonexistent) account in one burst.
        // The first 30 are 401s (unknown account); the 31st must be 429 —
        // the strict "auth" bucket still guards the credential endpoints.
        HttpStatusCode last = HttpStatusCode.OK;
        for (var i = 0; i < 31; i++)
        {
            var res = await client.PostAsJsonAsync("/api/auth/login", new
            {
                Email = "rate-limit-probe@example.com",
                Password = "WrongPassword123!@#",
            });
            last = res.StatusCode;
        }
        Assert.Equal((HttpStatusCode)429, last);
    }
}
