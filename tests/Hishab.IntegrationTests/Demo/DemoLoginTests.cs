using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Hishab.Api.Controllers;
using Hishab.Infrastructure.Demo;
using Hishab.IntegrationTests.Couriers;
using Hishab.IntegrationTests.Support;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;

namespace Hishab.IntegrationTests.Demo;

/// <summary>The API as the hosted demo runs it: Demo environment with Demo:Login on.</summary>
public sealed class DemoLoginApiFactory : HishabApiFactory
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.UseEnvironment("Demo");
        builder.UseSetting("Demo:Login", "true");
    }
}

public class DemoLoginTests(DemoLoginApiFactory factory) : IClassFixture<DemoLoginApiFactory>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private readonly HttpClient _client = factory.CreateClient();

    [Fact]
    public async Task The_demo_lists_the_roles_a_visitor_can_try_owner_first()
    {
        var info = await _client.GetFromJsonAsync<JsonElement>("/api/auth/demo", Ct);

        info.GetProperty("roles").EnumerateArray().Select(r => r.GetString())
            .ShouldBe(["Owner", "Manager", "Moderator", "Packer"]);
    }

    [Theory]
    [InlineData("Owner")]
    [InlineData("Packer")] // an invited staff member who never signed in: confirmed on first use, as with an OTP
    public async Task A_visitor_signs_in_to_the_demo_shop_with_one_click(string role)
    {
        await factory.Services.GetRequiredService<DemoSeeder>().SeedAsync(Ct);

        var response = await _client.PostAsJsonAsync("/api/auth/demo", new { role }, Ct);
        var login = await response.Content.ReadFromJsonAsync<JsonElement>(Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        login.GetProperty("role").GetString().ShouldBe(role);
        login.GetProperty("refreshToken").ValueKind.ShouldBe(JsonValueKind.Null); // only in the HttpOnly cookie
        response.Headers.GetValues("Set-Cookie").ShouldContain(c => c.StartsWith(AuthController.RefreshCookie + "=") && c.Contains("httponly"));

        using var me = new HttpRequestMessage(HttpMethod.Get, "/api/auth/me");
        me.Headers.Authorization = AuthSession.Bearer(login);
        var who = await (await _client.SendAsync(me, Ct)).Content.ReadFromJsonAsync<JsonElement>(Ct);
        who.GetProperty("role").GetString().ShouldBe(role);
        who.GetProperty("shopId").GetString().ShouldBe(login.GetProperty("shopId").GetString());
    }

    [Fact]
    public async Task An_unknown_role_is_a_validation_error() =>
        (await _client.PostAsJsonAsync("/api/auth/demo", new { role = "Admin" }, Ct)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
}

/// <summary>Demo environment, but Demo:Login left off: the route exists and still answers 404.</summary>
public class DemoLoginOffTests(DemoEnvironmentApiFactory factory) : IClassFixture<DemoEnvironmentApiFactory>
{
    [Fact]
    public async Task Demo_sign_in_is_not_found_until_the_switch_is_on() =>
        await DemoLoginAbsentTests.ShouldBeAbsentAsync(factory.CreateClient());
}

/// <summary>Outside Development and Demo the route does not exist at all.</summary>
public class DemoLoginAbsentTests(HishabApiFactory factory) : IClassFixture<HishabApiFactory>
{
    [Fact]
    public async Task Demo_sign_in_does_not_exist_in_other_environments() =>
        await ShouldBeAbsentAsync(factory.CreateClient());

    internal static async Task ShouldBeAbsentAsync(HttpClient client)
    {
        var ct = TestContext.Current.CancellationToken;
        (await client.GetAsync("/api/auth/demo", ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await client.PostAsJsonAsync("/api/auth/demo", new { role = "Owner" }, ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }
}
