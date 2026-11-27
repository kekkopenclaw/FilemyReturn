using System.Net;
using System.Net.Http.Json;

namespace DocuArchive.Tests;

public class AuthTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;
    public AuthTests(CustomWebApplicationFactory factory) { _factory = factory; _factory.EnsureCreated(); }

    [Fact]
    public async Task Register_ReturnsCreated_AndLoginSucceeds()
    {
        var client = _factory.CreateClient();
        var email = TestHelpers.UniqueEmail("reg");
        var register = await client.PostAsJsonAsync("/api/auth/register",
            new { Email = email, Password = TestHelpers.Password, DisplayName = "Reg User" });
        Assert.Equal(HttpStatusCode.Created, register.StatusCode);

        var login = await client.PostAsJsonAsync("/api/auth/login",
            new { Email = email, Password = TestHelpers.Password });
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        var body = await login.Content.ReadFromJsonAsync<AuthBody>();
        Assert.NotNull(body);
        Assert.False(string.IsNullOrWhiteSpace(body!.AccessToken));
        // The refresh token is delivered as an HttpOnly cookie, never in the
        // body — a script in the page must not be able to read it.
        Assert.True(string.IsNullOrWhiteSpace(body.RefreshToken));
        var cookie = TestHelpers.RefreshCookieValue(login);
        Assert.False(string.IsNullOrWhiteSpace(cookie));
        Assert.Contains(login.Headers.GetValues("Set-Cookie"),
            v => v.Contains("docuarchive.refresh=", StringComparison.OrdinalIgnoreCase)
                && v.Contains("httponly", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Register_WeakPassword_Returns400()
    {
        var client = _factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/auth/register",
            new { Email = TestHelpers.UniqueEmail("weak"), Password = "short", DisplayName = "Weak" });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Login_WrongPassword_Returns401()
    {
        var client = _factory.CreateClient();
        var session = await TestHelpers.RegisterAndLoginAsync(client, TestHelpers.UniqueEmail("badpass"));
        var bad = await client.PostAsJsonAsync("/api/auth/login",
            new { Email = session.Email, Password = "WrongPassword123!@#" });
        Assert.Equal(HttpStatusCode.Unauthorized, bad.StatusCode);
    }

    [Fact]
    public async Task Refresh_RotatesToken_OldTokenRejected()
    {
        var client = _factory.CreateClient();
        var session = await TestHelpers.RegisterAndLoginAsync(client, TestHelpers.UniqueEmail("refresh"));
        var refreshed = await client.PostAsJsonAsync("/api/auth/refresh",
            new { RefreshToken = session.RefreshToken });
        Assert.Equal(HttpStatusCode.OK, refreshed.StatusCode);
        var body = await refreshed.Content.ReadFromJsonAsync<AuthBody>();
        Assert.NotNull(body);
        // Rotation issues a NEW cookie; the body still carries no token.
        Assert.True(string.IsNullOrWhiteSpace(body!.RefreshToken));
        var newCookie = TestHelpers.RefreshCookieValue(refreshed);
        Assert.NotEqual(session.RefreshToken, newCookie);

        // Old refresh token must now be revoked.
        var reuse = await client.PostAsJsonAsync("/api/auth/refresh",
            new { RefreshToken = session.RefreshToken });
        Assert.Equal(HttpStatusCode.Unauthorized, reuse.StatusCode);
    }

    [Fact]
    public async Task Refresh_WithCookieOnly_Succeeds()
    {
        // The browser flow: no token in the body at all, the HttpOnly cookie
        // is the only credential. (HttpClient here does not persist cookies,
        // so the test sends the Cookie header itself.)
        var client = _factory.CreateClient();
        var session = await TestHelpers.RegisterAndLoginAsync(client, TestHelpers.UniqueEmail("cookie-refresh"));
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/refresh")
        {
            Content = JsonContent.Create(new { })
        };
        request.Headers.Add("Cookie", $"docuarchive.refresh={session.RefreshToken}");
        var refreshed = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, refreshed.StatusCode);
        var body = await refreshed.Content.ReadFromJsonAsync<AuthBody>();
        Assert.NotNull(body);
        Assert.False(string.IsNullOrWhiteSpace(body!.AccessToken));
        Assert.True(string.IsNullOrWhiteSpace(body.RefreshToken));
        Assert.NotEqual(session.RefreshToken, TestHelpers.RefreshCookieValue(refreshed));
    }

    [Fact]
    public async Task Refresh_WithoutCookieOrBody_Returns401()
    {
        var client = _factory.CreateClient();
        await TestHelpers.RegisterAndLoginAsync(client, TestHelpers.UniqueEmail("no-token"));
        var refreshed = await client.PostAsJsonAsync("/api/auth/refresh", new { });
        Assert.Equal(HttpStatusCode.Unauthorized, refreshed.StatusCode);
    }

    [Fact]
    public async Task Logout_ClearsCookie_AndRevokesToken()
    {
        var client = _factory.CreateClient();
        var session = await TestHelpers.RegisterAndLoginAsync(client, TestHelpers.UniqueEmail("logout"));
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/logout")
        {
            Content = JsonContent.Create(new { })
        };
        request.Headers.Add("Cookie", $"docuarchive.refresh={session.RefreshToken}");
        var logout = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.NoContent, logout.StatusCode);
        // The cookie is expired on logout.
        Assert.Contains(logout.Headers.GetValues("Set-Cookie"),
            v => v.Contains("docuarchive.refresh=", StringComparison.OrdinalIgnoreCase)
                && v.Contains("expires=Thu, 01 Jan 1970", StringComparison.OrdinalIgnoreCase));
        // The revoked token must no longer mint access tokens.
        var refresh = await client.PostAsJsonAsync("/api/auth/refresh",
            new { RefreshToken = session.RefreshToken });
        Assert.Equal(HttpStatusCode.Unauthorized, refresh.StatusCode);
    }

    [Fact]
    public async Task Login_AfterFiveFailures_AccountIsLockedOut()
    {
        var client = _factory.CreateClient();
        var email = TestHelpers.UniqueEmail("lockout");
        await TestHelpers.RegisterAndLoginAsync(client, email);
        // Identity locks the account ON the 5th failed attempt: the first 4 return 401,
        // the 5th already returns 423 (LockedOut).
        for (var i = 0; i < 4; i++)
        {
            var bad = await client.PostAsJsonAsync("/api/auth/login",
                new { Email = email, Password = "WrongPassword123!@#" });
            Assert.Equal(HttpStatusCode.Unauthorized, bad.StatusCode);
        }
        var fifth = await client.PostAsJsonAsync("/api/auth/login",
            new { Email = email, Password = "WrongPassword123!@#" });
        Assert.Equal((HttpStatusCode)423, fifth.StatusCode);
        // Correct password must still fail while locked out.
        var locked = await client.PostAsJsonAsync("/api/auth/login",
            new { Email = email, Password = TestHelpers.Password });
        Assert.Equal((HttpStatusCode)423, locked.StatusCode);
    }

    [Fact]
    public async Task ChangePassword_ThenLoginWithNewPasswordSucceeds()
    {
        var client = _factory.CreateClient();
        var session = await TestHelpers.RegisterAndLoginAsync(client, TestHelpers.UniqueEmail("changepw"));
        TestHelpers.Authed(client, session.AccessToken);
        var change = await client.PostAsJsonAsync("/api/auth/change-password",
            new { CurrentPassword = TestHelpers.Password, NewPassword = "NewPassword456!@#" });
        Assert.Equal(HttpStatusCode.NoContent, change.StatusCode);

        var anon = _factory.CreateClient();
        var login = await anon.PostAsJsonAsync("/api/auth/login",
            new { Email = session.Email, Password = "NewPassword456!@#" });
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
    }

    [Fact]
    public async Task Me_Unauthenticated_Returns401()
    {
        var client = _factory.CreateClient();
        var response = await client.GetAsync("/api/auth/me");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    private record AuthBody(string AccessToken, string RefreshToken, UserBody User);
    private record UserBody(string Id, string Email);
}
