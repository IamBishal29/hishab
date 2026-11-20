using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Json;
using Hishab.Api.Controllers;
using Hishab.IntegrationTests.Support;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Hishab.IntegrationTests.Shops;

public class TokenAuthTests(HishabApiFactory factory) : IClassFixture<HishabApiFactory>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    // No cookie container: the refresh cookie is Secure, so tests read and send it by hand.
    private readonly HttpClient _client = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });

    private Task<JsonElement> SignIn(string tokenDelivery = "Body") => factory.SignInAsync(_client, Ct, tokenDelivery);

    private async Task<HttpResponseMessage> Me(string? accessToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/auth/me");
        if (accessToken is not null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        }

        return await _client.SendAsync(request, Ct);
    }

    private Task<HttpResponseMessage> Refresh(string? refreshToken) =>
        _client.PostAsJsonAsync("/api/auth/refresh", new { refreshToken }, Ct);

    private static string Access(JsonElement tokens) => tokens.GetProperty("accessToken").GetString()!;

    private static string RefreshTokenOf(JsonElement tokens) => tokens.GetProperty("refreshToken").GetString()!;

    private static async Task<JsonElement> Json(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync(Ct)).RootElement.Clone();

    private static string RefreshCookieHeader(HttpResponseMessage response) =>
        response.Headers.GetValues("Set-Cookie").Single(c => c.StartsWith(AuthController.RefreshCookie + "=", StringComparison.Ordinal));

    [Fact]
    public async Task Access_token_identifies_the_user_and_sets_the_tenant()
    {
        var login = await SignIn();

        var response = await Me(Access(login));

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var me = await Json(response);
        me.GetProperty("userId").GetGuid().ShouldBe(login.GetProperty("userId").GetGuid());
        me.GetProperty("shopId").GetGuid().ShouldBe(login.GetProperty("shopId").GetGuid()); // read from ITenantContext
        me.GetProperty("role").GetString().ShouldBe("Owner");
        login.GetProperty("accessTokenExpiresAt").GetDateTimeOffset()
            .ShouldBe(factory.Clock.GetUtcNow().AddMinutes(15), TimeSpan.FromSeconds(1));
    }

    [Fact]
    public async Task Request_without_a_token_is_unauthorized()
    {
        (await Me(accessToken: null)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Expired_access_token_is_unauthorized()
    {
        var login = await SignIn();

        factory.Clock.Advance(TimeSpan.FromMinutes(15) + TimeSpan.FromSeconds(31)); // lifetime + clock skew

        (await Me(Access(login))).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Token_signed_with_another_key_is_unauthorized()
    {
        var login = await SignIn();
        var forged = new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = "hishab",
            Audience = "hishab-api",
            Expires = factory.Clock.GetUtcNow().AddMinutes(10).UtcDateTime,
            Subject = new ClaimsIdentity([new Claim("sub", Guid.NewGuid().ToString()), new Claim("tenant_id", login.GetProperty("shopId").GetString()!)]),
            SigningCredentials = new SigningCredentials(new SymmetricSecurityKey(RandomNumberGenerator.GetBytes(32)), SecurityAlgorithms.HmacSha256),
        });

        (await Me(forged)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Refresh_returns_a_new_pair_and_the_new_access_token_works()
    {
        var login = await SignIn();

        var response = await Refresh(RefreshTokenOf(login));

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var next = await Json(response);
        RefreshTokenOf(next).ShouldNotBe(RefreshTokenOf(login));
        (await Me(Access(next))).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Replaying_a_used_refresh_token_revokes_the_whole_family()
    {
        var login = await SignIn();
        var next = await Json(await Refresh(RefreshTokenOf(login)));

        var replay = await Refresh(RefreshTokenOf(login)); // e.g. a thief with a copy of the first token

        replay.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await Json(replay)).GetProperty("detail").GetString().ShouldBe("Your session has expired. Please sign in again.");
        (await Refresh(RefreshTokenOf(next))).StatusCode.ShouldBe(HttpStatusCode.Unauthorized); // the legitimate chain is gone too
    }

    [Fact]
    public async Task Reuse_in_one_family_does_not_affect_another_login()
    {
        var victim = await SignIn();
        var other = await SignIn();
        await Refresh(RefreshTokenOf(victim));
        await Refresh(RefreshTokenOf(victim));

        (await Refresh(RefreshTokenOf(other))).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Expired_refresh_token_is_unauthorized()
    {
        var login = await SignIn();

        factory.Clock.Advance(TimeSpan.FromDays(30) + TimeSpan.FromMinutes(1));

        (await Refresh(RefreshTokenOf(login))).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Theory]
    [InlineData("not-a-real-token")]
    [InlineData(null)]
    public async Task Unknown_or_missing_refresh_token_is_unauthorized(string? refreshToken)
    {
        (await Refresh(refreshToken)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Logout_revokes_the_family()
    {
        var login = await SignIn();

        var logout = await _client.PostAsJsonAsync("/api/auth/logout", new { refreshToken = RefreshTokenOf(login) }, Ct);

        logout.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await Refresh(RefreshTokenOf(login))).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Browser_login_puts_the_refresh_token_only_in_a_locked_down_cookie()
    {
        var phone = AuthSession.NewPhone();
        await _client.PostAsJsonAsync("/api/auth/otp", new { phone }, Ct);

        var login = await _client.PostAsJsonAsync("/api/auth/otp/verify", new { phone, code = factory.Otps.LatestCodeFor(phone) }, Ct);

        login.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await Json(login)).GetProperty("refreshToken").ValueKind.ShouldBe(JsonValueKind.Null);
        var cookie = RefreshCookieHeader(login).ToLowerInvariant();
        cookie.ShouldContain("httponly");
        cookie.ShouldContain("secure");
        cookie.ShouldContain("samesite=strict");
        cookie.ShouldContain("path=/api/auth");
    }

    [Fact]
    public async Task Refresh_with_the_cookie_rotates_the_cookie()
    {
        var phone = AuthSession.NewPhone();
        await _client.PostAsJsonAsync("/api/auth/otp", new { phone }, Ct);
        var login = await _client.PostAsJsonAsync("/api/auth/otp/verify", new { phone, code = factory.Otps.LatestCodeFor(phone) }, Ct);
        var cookie = RefreshCookieHeader(login).Split(';')[0];

        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/refresh");
        request.Headers.Add("Cookie", cookie);
        var response = await _client.SendAsync(request, Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await Json(response)).GetProperty("refreshToken").ValueKind.ShouldBe(JsonValueKind.Null);
        RefreshCookieHeader(response).Split(';')[0].ShouldNotBe(cookie);
    }
}
