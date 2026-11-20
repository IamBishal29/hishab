using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Hishab.Domain.Shops;
using Hishab.IntegrationTests.Support;

namespace Hishab.IntegrationTests.Shops;

public class StaffTests(HishabApiFactory factory) : IClassFixture<HishabApiFactory>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private readonly HttpClient _client = factory.CreateClient();

    /// <summary>A new shop on Business (15 users), so tests other than the limit tests never hit the user limit.</summary>
    private async Task<JsonElement> NewOwner()
    {
        var owner = await factory.SignInAsync(_client, Ct);
        await factory.ChangePlanAsync(owner, Plan.Business, Ct);
        return owner;
    }

    private async Task<HttpResponseMessage> Send(HttpMethod method, string path, JsonElement user, object? body = null)
    {
        using var request = new HttpRequestMessage(method, path);
        request.Headers.Authorization = AuthSession.Bearer(user);
        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }

        return await _client.SendAsync(request, Ct);
    }

    private static async Task<JsonElement> Json(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync(Ct)).RootElement.Clone();

    private static Guid Id(JsonElement e, string property = "userId") => e.GetProperty(property).GetGuid();

    [Fact]
    public async Task Invited_phone_joins_the_owners_shop_at_first_sign_in()
    {
        var owner = await NewOwner();
        var phone = AuthSession.NewPhone();

        var invite = await Send(HttpMethod.Post, "/api/staff", owner, new { phone, role = "Moderator" });

        invite.StatusCode.ShouldBe(HttpStatusCode.Created);
        (await Json(invite)).GetProperty("status").GetString().ShouldBe("Invited");

        await _client.PostAsJsonAsync("/api/auth/otp", new { phone }, Ct);
        var login = await Json(await _client.PostAsJsonAsync(
            "/api/auth/otp/verify", new { phone, code = factory.Otps.LatestCodeFor(phone), tokenDelivery = "Body" }, Ct));

        Id(login, "shopId").ShouldBe(Id(owner, "shopId"));
        login.GetProperty("role").GetString().ShouldBe("Moderator");
        login.GetProperty("isNewShop").GetBoolean().ShouldBeFalse();

        var team = await Json(await Send(HttpMethod.Get, "/api/staff", owner));
        var member = team.EnumerateArray().Single(m => m.GetProperty("id").GetGuid() == Id(login));
        member.GetProperty("status").GetString().ShouldBe("Active");
        team.GetArrayLength().ShouldBe(2); // owner + moderator
    }

    [Fact]
    public async Task Phone_that_already_belongs_to_a_shop_cannot_be_invited()
    {
        var owner = await NewOwner();
        var otherShopsPhone = AuthSession.NewPhone();
        await factory.SignInWithPhoneAsync(_client, otherShopsPhone, Ct);

        var response = await Send(HttpMethod.Post, "/api/staff", owner, new { phone = otherShopsPhone, role = "Packer" });

        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Inviting_the_same_phone_twice_conflicts()
    {
        var owner = await NewOwner();
        var phone = AuthSession.NewPhone();
        await Send(HttpMethod.Post, "/api/staff", owner, new { phone, role = "Packer" });

        (await Send(HttpMethod.Post, "/api/staff", owner, new { phone, role = "Packer" })).StatusCode.ShouldBe(HttpStatusCode.Conflict);
    }

    [Theory]
    [InlineData("Owner")]
    [InlineData("Superuser")]
    public async Task Only_staff_roles_can_be_given(string role)
    {
        var owner = await NewOwner();

        var response = await Send(HttpMethod.Post, "/api/staff", owner, new { phone = AuthSession.NewPhone(), role });

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Role_change_takes_effect_at_the_next_refresh()
    {
        var owner = await NewOwner();
        var staff = await factory.InviteAndSignInAsync(_client, owner, "Moderator", Ct);

        (await Send(HttpMethod.Put, $"/api/staff/{Id(staff)}/role", owner, new { role = "Packer" })).StatusCode.ShouldBe(HttpStatusCode.OK);

        var refreshed = await Json(await _client.PostAsJsonAsync("/api/auth/refresh", new { refreshToken = staff.GetProperty("refreshToken").GetString() }, Ct));
        var me = await Json(await Send(HttpMethod.Get, "/api/auth/me", refreshed));
        me.GetProperty("role").GetString().ShouldBe("Packer");
    }

    [Fact]
    public async Task Removing_a_member_ends_their_sessions_and_frees_the_phone()
    {
        var owner = await NewOwner();
        var staff = await factory.InviteAndSignInAsync(_client, owner, "Packer", Ct);

        (await Send(HttpMethod.Delete, $"/api/staff/{Id(staff)}", owner)).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var refresh = await _client.PostAsJsonAsync("/api/auth/refresh", new { refreshToken = staff.GetProperty("refreshToken").GetString() }, Ct);
        refresh.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await Json(await Send(HttpMethod.Get, "/api/staff", owner))).GetArrayLength().ShouldBe(1);
    }

    [Fact]
    public async Task Owner_cannot_be_removed_or_demoted()
    {
        var owner = await NewOwner();

        (await Send(HttpMethod.Delete, $"/api/staff/{Id(owner)}", owner)).StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await Send(HttpMethod.Put, $"/api/staff/{Id(owner)}/role", owner, new { role = "Manager" })).StatusCode.ShouldBe(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Another_shops_member_is_not_found()
    {
        var ownerA = await NewOwner();
        var staffA = await factory.InviteAndSignInAsync(_client, ownerA, "Packer", Ct);
        var ownerB = await NewOwner();

        (await Send(HttpMethod.Put, $"/api/staff/{Id(staffA)}/role", ownerB, new { role = "Manager" })).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await Send(HttpMethod.Delete, $"/api/staff/{Id(staffA)}", ownerB)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await Json(await Send(HttpMethod.Get, "/api/staff", ownerB))).GetArrayLength().ShouldBe(1);
    }

    [Fact]
    public async Task Managers_cannot_manage_staff()
    {
        var owner = await NewOwner();
        var manager = await factory.InviteAndSignInAsync(_client, owner, "Manager", Ct);

        (await Send(HttpMethod.Get, "/api/staff", manager)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await Send(HttpMethod.Post, "/api/staff", manager, new { phone = AuthSession.NewPhone(), role = "Packer" })).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Free_plan_allows_only_the_owner()
    {
        var owner = await factory.SignInAsync(_client, Ct); // Free: 1 user

        var response = await Send(HttpMethod.Post, "/api/staff", owner, new { phone = AuthSession.NewPhone(), role = "Packer" });

        response.StatusCode.ShouldBe(HttpStatusCode.PaymentRequired);
        (await Json(response)).GetProperty("detail").GetString().ShouldBe("Your Free plan allows 1 user. Upgrade your plan to add more.");
    }

    [Fact]
    public async Task Invites_stop_at_the_plan_user_limit()
    {
        var owner = await factory.SignInAsync(_client, Ct);
        await factory.ChangePlanAsync(owner, Plan.Starter, Ct); // 2 users: owner + 1

        (await Send(HttpMethod.Post, "/api/staff", owner, new { phone = AuthSession.NewPhone(), role = "Packer" })).StatusCode.ShouldBe(HttpStatusCode.Created);
        var second = await Send(HttpMethod.Post, "/api/staff", owner, new { phone = AuthSession.NewPhone(), role = "Packer" });

        second.StatusCode.ShouldBe(HttpStatusCode.PaymentRequired);
    }

    [Fact]
    public async Task Parallel_invites_never_exceed_the_user_limit()
    {
        var owner = await factory.SignInAsync(_client, Ct);
        await factory.ChangePlanAsync(owner, Plan.Growth, Ct); // 5 users: owner + 4

        var responses = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ =>
            Send(HttpMethod.Post, "/api/staff", owner, new { phone = AuthSession.NewPhone(), role = "Packer" })));

        responses.Count(r => r.StatusCode == HttpStatusCode.Created).ShouldBe(4);
        responses.Count(r => r.StatusCode == HttpStatusCode.PaymentRequired).ShouldBe(4);
        (await Json(await Send(HttpMethod.Get, "/api/staff", owner))).GetArrayLength().ShouldBe(5);
    }
}
