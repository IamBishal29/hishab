using System.Net;
using System.Text.Json;
using Hishab.IntegrationTests.Support;

namespace Hishab.IntegrationTests.Shops;

/// <summary>Every role against every policy, written out so a matrix change shows up in review.</summary>
public class AuthorizationTests(HishabApiFactory factory) : IClassFixture<HishabApiFactory>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private readonly HttpClient _client = factory.CreateClient();

    public static TheoryData<string, string, bool> Matrix => new()
    {
        { "Owner", "manage-shop", true },
        { "Owner", "manage-staff", true },
        { "Owner", "manage-products", true },
        { "Owner", "enter-orders", true },
        { "Owner", "pack", true },
        { "Owner", "see-profit", true },
        { "Owner", "view-audit", true },
        { "Manager", "manage-shop", true },
        { "Manager", "manage-staff", false },
        { "Manager", "manage-products", true },
        { "Manager", "enter-orders", true },
        { "Manager", "pack", true },
        { "Manager", "see-profit", false },
        { "Manager", "view-audit", false },
        { "Moderator", "manage-shop", false },
        { "Moderator", "manage-staff", false },
        { "Moderator", "manage-products", false },
        { "Moderator", "enter-orders", true },
        { "Moderator", "pack", false },
        { "Moderator", "see-profit", false },
        { "Moderator", "view-audit", false },
        { "Packer", "manage-shop", false },
        { "Packer", "manage-staff", false },
        { "Packer", "manage-products", false },
        { "Packer", "enter-orders", false },
        { "Packer", "pack", true },
        { "Packer", "see-profit", false },
        { "Packer", "view-audit", false },
    };

    [Theory]
    [MemberData(nameof(Matrix))]
    public async Task Each_role_reaches_only_the_policies_it_holds(string role, string policy, bool allowed)
    {
        var owner = await factory.SignInAsync(_client, Ct);
        var user = role == "Owner" ? owner : await factory.InviteAndSignInAsync(_client, owner, role, Ct);

        var response = await Probe(policy, user);

        response.StatusCode.ShouldBe(allowed ? HttpStatusCode.OK : HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Anonymous_requests_are_unauthorized_not_forbidden()
    {
        (await _client.GetAsync(new Uri("/api/_test/policy/pack", UriKind.Relative), Ct)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    private async Task<HttpResponseMessage> Probe(string policy, JsonElement user)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"/api/_test/policy/{policy}");
        request.Headers.Authorization = AuthSession.Bearer(user);
        return await _client.SendAsync(request, Ct);
    }
}
