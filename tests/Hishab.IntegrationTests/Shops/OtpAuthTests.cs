using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Hishab.Application.Shops.Auth;
using Hishab.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Hishab.IntegrationTests.Shops;

public class OtpAuthTests(HishabApiFactory factory) : IClassFixture<HishabApiFactory>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private readonly HttpClient _client = factory.CreateClient();

    /// <summary>A fresh, valid number per test so tests never share per-phone limits.</summary>
    private static string NewPhone() =>
        "017" + Random.Shared.Next(10_000_000, 99_999_999).ToString(CultureInfo.InvariantCulture);

    private Task<HttpResponseMessage> RequestOtp(string phone) =>
        _client.PostAsJsonAsync("/auth/otp", new { phone }, Ct);

    private Task<HttpResponseMessage> Verify(string phone, string code) =>
        _client.PostAsJsonAsync("/auth/otp/verify", new { phone, code }, Ct);

    private static async Task<JsonElement> Json(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync(Ct)).RootElement;

    private static string WrongCode(string code) => code == "000000" ? "111111" : "000000";

    private static async Task<string> CodeErrorAsync(HttpResponseMessage response)
    {
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        return (await Json(response)).GetProperty("errors").GetProperty("code")[0].GetString()!;
    }

    [Fact]
    public async Task First_login_creates_a_shop_and_its_owner()
    {
        var phone = NewPhone();

        var sent = await RequestOtp(phone);
        sent.StatusCode.ShouldBe(HttpStatusCode.Accepted);
        (await Json(sent)).GetProperty("expiresInSeconds").GetInt32().ShouldBe(300);

        var login = await Verify(phone, factory.Otps.LatestCodeFor(phone));

        login.StatusCode.ShouldBe(HttpStatusCode.OK);
        var body = await Json(login);
        body.GetProperty("isNewShop").GetBoolean().ShouldBeTrue();
        body.GetProperty("role").GetString().ShouldBe("Owner");

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<HishabDbContext>();
        var shopId = body.GetProperty("shopId").GetGuid();
        (await db.Shops.AnyAsync(s => s.Id == shopId, Ct)).ShouldBeTrue();
        var owner = await db.Users.IgnoreQueryFilters([HishabDbContext.TenantFilter])
            .SingleAsync(u => u.Id == body.GetProperty("userId").GetGuid(), Ct);
        owner.TenantId.ShouldBe(shopId);
        owner.PhoneNumber.ShouldBe("+88" + phone);
    }

    [Fact]
    public async Task Next_login_with_another_spelling_of_the_number_reuses_the_shop()
    {
        var phone = NewPhone();
        await RequestOtp(phone);
        var first = await Json(await Verify(phone, factory.Otps.LatestCodeFor(phone)));

        var spelledDifferently = "+880 " + phone[1..5] + "-" + phone[5..];
        factory.Clock.Advance(OtpSendPolicy.MinInterval);
        (await RequestOtp(spelledDifferently)).StatusCode.ShouldBe(HttpStatusCode.Accepted);
        var second = await Json(await Verify(spelledDifferently, factory.Otps.LatestCodeFor(phone)));

        second.GetProperty("isNewShop").GetBoolean().ShouldBeFalse();
        second.GetProperty("shopId").GetGuid().ShouldBe(first.GetProperty("shopId").GetGuid());
        second.GetProperty("userId").GetGuid().ShouldBe(first.GetProperty("userId").GetGuid());
    }

    [Fact]
    public async Task Five_wrong_codes_lock_the_challenge_even_for_the_right_code()
    {
        var phone = NewPhone();
        await RequestOtp(phone);
        var code = factory.Otps.LatestCodeFor(phone);

        for (var i = 0; i < 5; i++)
        {
            (await CodeErrorAsync(await Verify(phone, WrongCode(code)))).ShouldBe(OtpSendPolicy.InvalidCodeMessage);
        }

        (await CodeErrorAsync(await Verify(phone, code))).ShouldBe(OtpSendPolicy.InvalidCodeMessage);
    }

    [Fact]
    public async Task An_expired_code_is_rejected_with_the_same_message()
    {
        var phone = NewPhone();
        await RequestOtp(phone);

        factory.Clock.Advance(TimeSpan.FromMinutes(5));

        (await CodeErrorAsync(await Verify(phone, factory.Otps.LatestCodeFor(phone)))).ShouldBe(OtpSendPolicy.InvalidCodeMessage);
    }

    [Fact]
    public async Task Verifying_a_phone_that_never_requested_a_code_looks_like_any_other_failure()
    {
        (await CodeErrorAsync(await Verify(NewPhone(), "123456"))).ShouldBe(OtpSendPolicy.InvalidCodeMessage);
    }

    [Fact]
    public async Task Resending_within_30_seconds_returns_429_with_retry_after()
    {
        var phone = NewPhone();
        await RequestOtp(phone);

        var again = await RequestOtp(phone);

        again.StatusCode.ShouldBe(HttpStatusCode.TooManyRequests);
        again.Headers.RetryAfter!.Delta!.Value.TotalSeconds.ShouldBeInRange(1, 30);
    }

    [Fact]
    public async Task A_fourth_code_within_15_minutes_is_refused()
    {
        var phone = NewPhone();
        for (var i = 0; i < OtpSendPolicy.MaxPerWindow; i++)
        {
            (await RequestOtp(phone)).StatusCode.ShouldBe(HttpStatusCode.Accepted);
            factory.Clock.Advance(OtpSendPolicy.MinInterval);
        }

        (await RequestOtp(phone)).StatusCode.ShouldBe(HttpStatusCode.TooManyRequests);
    }

    [Fact]
    public async Task An_invalid_phone_returns_400_for_the_phone_field()
    {
        var response = await RequestOtp("02912345678");

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await Json(response)).GetProperty("errors").GetProperty("phone")[0].GetString().ShouldNotBeNullOrEmpty();
    }

    [Fact]
    public async Task Registered_and_unknown_phones_get_identical_responses()
    {
        var registered = NewPhone();
        await RequestOtp(registered);
        await Verify(registered, factory.Otps.LatestCodeFor(registered));
        factory.Clock.Advance(OtpSendPolicy.MinInterval);

        var forRegistered = await RequestOtp(registered);
        var forUnknown = await RequestOtp(NewPhone());

        forRegistered.StatusCode.ShouldBe(forUnknown.StatusCode);
        (await forRegistered.Content.ReadAsStringAsync(Ct)).ShouldBe(await forUnknown.Content.ReadAsStringAsync(Ct));
    }
}

public class OtpRateLimitTests(LowRateLimitApiFactory factory) : IClassFixture<LowRateLimitApiFactory>
{
    [Fact]
    public async Task Too_many_otp_requests_from_one_ip_return_429_problem_details()
    {
        using var client = factory.CreateClient();
        var ct = TestContext.Current.CancellationToken;

        for (var i = 0; i < 2; i++)
        {
            await client.PostAsJsonAsync("/auth/otp", new { phone = "02900000000" }, ct); // any request counts, even invalid ones
        }

        var limited = await client.PostAsJsonAsync("/auth/otp", new { phone = "01712345678" }, ct);

        limited.StatusCode.ShouldBe(HttpStatusCode.TooManyRequests);
        limited.Content.Headers.ContentType!.MediaType.ShouldBe("application/problem+json");
    }
}
