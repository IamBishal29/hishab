using System.Globalization;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Hishab.Application.Common.Tenancy;
using Hishab.Domain.Shops;
using Hishab.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Hishab.IntegrationTests.Support;

/// <summary>Signs in through the real OTP and JWT endpoints.</summary>
public static class AuthSession
{
    /// <summary>A fresh, valid number per call so tests never share per-phone limits.</summary>
    public static string NewPhone() =>
        "017" + Random.Shared.Next(10_000_000, 99_999_999).ToString(CultureInfo.InvariantCulture);

    /// <summary>Signs up a new shop; the response body holds the tokens (refresh token in the body unless <paramref name="tokenDelivery"/> is "Cookie").</summary>
    public static Task<JsonElement> SignInAsync(
        this HishabApiFactory factory, HttpClient client, CancellationToken cancellationToken, string tokenDelivery = "Body") =>
        factory.SignInWithPhoneAsync(client, NewPhone(), cancellationToken, tokenDelivery);

    /// <summary>OTP sign-in for a given phone: signs up a new shop, or joins the shop that invited it.</summary>
    public static async Task<JsonElement> SignInWithPhoneAsync(
        this HishabApiFactory factory, HttpClient client, string phone, CancellationToken cancellationToken, string tokenDelivery = "Body")
    {
        (await client.PostAsJsonAsync("/api/auth/otp", new { phone }, cancellationToken)).EnsureSuccessStatusCode();

        var code = factory.Otps.LatestCodeFor(phone);
        var response = await client.PostAsJsonAsync("/api/auth/otp/verify", new { phone, code, tokenDelivery }, cancellationToken);
        response.EnsureSuccessStatusCode();
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken)).RootElement.Clone();
    }

    /// <summary>
    /// The owner invites a new phone with <paramref name="role"/>; that person then signs in with
    /// OTP and joins the shop. Moves the shop to the Business plan first, because Free allows a
    /// single user (tests of the limits themselves set plans explicitly).
    /// </summary>
    public static async Task<JsonElement> InviteAndSignInAsync(
        this HishabApiFactory factory, HttpClient client, JsonElement owner, string role, CancellationToken cancellationToken)
    {
        await factory.ChangePlanAsync(owner, Plan.Business, cancellationToken);
        var phone = NewPhone();
        using var invite = new HttpRequestMessage(HttpMethod.Post, "/api/staff") { Content = JsonContent.Create(new { phone, role }) };
        invite.Headers.Authorization = Bearer(owner);
        (await client.SendAsync(invite, cancellationToken)).EnsureSuccessStatusCode();

        return await factory.SignInWithPhoneAsync(client, phone, cancellationToken);
    }

    /// <summary>Puts the owner's shop on <paramref name="plan"/>. Stands in for billing, which does not exist yet.</summary>
    public static async Task ChangePlanAsync(this HishabApiFactory factory, JsonElement owner, Plan plan, CancellationToken cancellationToken)
    {
        using var scope = factory.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<ITenantContext>().SetTenant(owner.GetProperty("shopId").GetGuid());
        var db = scope.ServiceProvider.GetRequiredService<HishabDbContext>();
        var shop = await db.Shops.SingleAsync(cancellationToken);
        shop.ChangePlan(plan);
        await db.SaveChangesAsync(cancellationToken);
    }

    public static AuthenticationHeaderValue Bearer(JsonElement tokens) =>
        new("Bearer", tokens.GetProperty("accessToken").GetString());
}
