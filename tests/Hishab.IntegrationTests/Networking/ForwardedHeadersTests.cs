using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;

namespace Hishab.IntegrationTests.Networking;

/// <summary>
/// As on Render: the static site's rewrite and the platform's edge each append one X-Forwarded-For entry
/// (ForwardLimit 2), with a per-IP limit of 2 OTP requests a minute.
/// </summary>
public sealed class BehindProxyApiFactory : HishabApiFactory
{
    protected override int OtpPermitsPerMinute => 2;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.UseSetting("ForwardedHeaders:Enabled", "true");
        builder.UseSetting("ForwardedHeaders:ForwardLimit", "2");
    }
}

public class ForwardedHeadersTests(BehindProxyApiFactory factory) : IClassFixture<BehindProxyApiFactory>
{
    private const string Edge = "10.0.0.9"; // the proxy closest to the app: the same for every visitor

    private readonly HttpClient _client = factory.CreateClient();

    [Fact]
    public async Task Visitors_behind_the_same_proxy_get_their_own_rate_limit()
    {
        await RequestOtp($"203.0.113.10, {Edge}");
        await RequestOtp($"203.0.113.10, {Edge}");

        (await RequestOtp($"203.0.113.10, {Edge}")).ShouldBe(HttpStatusCode.TooManyRequests);
        (await RequestOtp($"198.51.100.20, {Edge}")).ShouldBe(HttpStatusCode.Accepted); // someone else, same proxy
    }

    [Fact]
    public async Task A_client_cannot_escape_its_limit_by_writing_its_own_forwarded_for()
    {
        // The client sends "X-Forwarded-For: <anything>"; the proxies append the real address after it.
        await RequestOtp($"1.1.1.1, 203.0.113.30, {Edge}");
        await RequestOtp($"2.2.2.2, 203.0.113.30, {Edge}");

        (await RequestOtp($"3.3.3.3, 203.0.113.30, {Edge}")).ShouldBe(HttpStatusCode.TooManyRequests);
    }

    private async Task<HttpStatusCode> RequestOtp(string forwardedFor)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/otp")
        {
            // A new phone each time: only the IP limit is under test, not the per-phone one.
            Content = JsonContent.Create(new { phone = "017" + Random.Shared.Next(10_000_000, 99_999_999).ToString(CultureInfo.InvariantCulture) }),
        };
        request.Headers.Add("X-Forwarded-For", forwardedFor);
        return (await _client.SendAsync(request, TestContext.Current.CancellationToken)).StatusCode;
    }
}
