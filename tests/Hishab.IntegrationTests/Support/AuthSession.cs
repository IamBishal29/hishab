using System.Globalization;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace Hishab.IntegrationTests.Support;

/// <summary>Signs in through the real OTP and JWT endpoints.</summary>
public static class AuthSession
{
    /// <summary>A fresh, valid number per call so tests never share per-phone limits.</summary>
    public static string NewPhone() =>
        "017" + Random.Shared.Next(10_000_000, 99_999_999).ToString(CultureInfo.InvariantCulture);

    /// <summary>Signs up a new shop; the response body holds the tokens (refresh token in the body unless <paramref name="tokenDelivery"/> is "Cookie").</summary>
    public static async Task<JsonElement> SignInAsync(
        this HishabApiFactory factory, HttpClient client, CancellationToken cancellationToken, string tokenDelivery = "Body")
    {
        var phone = NewPhone();
        (await client.PostAsJsonAsync("/auth/otp", new { phone }, cancellationToken)).EnsureSuccessStatusCode();

        var code = factory.Otps.LatestCodeFor(phone);
        var response = await client.PostAsJsonAsync("/auth/otp/verify", new { phone, code, tokenDelivery }, cancellationToken);
        response.EnsureSuccessStatusCode();
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken)).RootElement.Clone();
    }

    public static AuthenticationHeaderValue Bearer(JsonElement tokens) =>
        new("Bearer", tokens.GetProperty("accessToken").GetString());
}
