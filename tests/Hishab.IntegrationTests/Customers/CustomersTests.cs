using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Hishab.Api.Middleware;
using Hishab.IntegrationTests.Support;

namespace Hishab.IntegrationTests.Customers;

public class CustomersTests(HishabApiFactory factory) : IClassFixture<HishabApiFactory>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private readonly HttpClient _client = factory.CreateClient();

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

    /// <summary>A buyer's number, different from the staff numbers AuthSession uses.</summary>
    private static string BuyerPhone() => "019" + Random.Shared.Next(10_000_000, 99_999_999).ToString(CultureInfo.InvariantCulture);

    [Fact]
    public async Task New_phone_creates_a_customer_and_the_next_order_matches_it()
    {
        var owner = await factory.SignInAsync(_client, Ct);
        var phone = BuyerPhone();

        var first = await Send(HttpMethod.Post, "/api/customers", owner, new { phone, name = "Rahima Begum" });
        var again = await Send(HttpMethod.Post, "/api/customers", owner, new { phone = "+88 " + phone, name = "R", address = "Mirpur 10" });

        first.StatusCode.ShouldBe(HttpStatusCode.Created);
        again.StatusCode.ShouldBe(HttpStatusCode.OK);
        var customer = await Json(again);
        customer.GetProperty("id").GetGuid().ShouldBe((await Json(first)).GetProperty("id").GetGuid());
        customer.GetProperty("name").GetString().ShouldBe("Rahima Begum"); // kept
        customer.GetProperty("address").GetString().ShouldBe("Mirpur 10"); // filled
        customer.GetProperty("phone").GetString().ShouldBe("+88" + phone);
    }

    [Fact]
    public async Task Parallel_creates_for_one_new_phone_produce_one_customer()
    {
        var owner = await factory.SignInAsync(_client, Ct);
        var phone = BuyerPhone();

        var responses = await Task.WhenAll(Enumerable.Range(0, 10).Select(_ =>
            Send(HttpMethod.Post, "/api/customers", owner, new { phone })));

        responses.Count(r => r.StatusCode == HttpStatusCode.Created).ShouldBe(1);
        responses.Count(r => r.StatusCode == HttpStatusCode.OK).ShouldBe(9);
        var ids = await Task.WhenAll(responses.Select(async r => (await Json(r)).GetProperty("id").GetGuid()));
        ids.Distinct().ShouldHaveSingleItem();
    }

    [Fact]
    public async Task Lookup_finds_any_spelling_and_shows_the_block()
    {
        var owner = await factory.SignInAsync(_client, Ct);
        var phone = BuyerPhone();
        var created = await Json(await Send(HttpMethod.Post, "/api/customers", owner, new { phone }));
        await Send(HttpMethod.Post, $"/api/customers/{created.GetProperty("id").GetGuid()}/block", owner, new { reason = "Refused 3 parcels" });

        var found = await Send(HttpMethod.Post, "/api/customers/lookup", owner, new { phone = "+880 " + phone[1..5] + "-" + phone[5..] });

        found.StatusCode.ShouldBe(HttpStatusCode.OK);
        var customer = await Json(found);
        customer.GetProperty("isBlocked").GetBoolean().ShouldBeTrue();
        customer.GetProperty("blockedReason").GetString().ShouldBe("Refused 3 parcels");
    }

    [Fact]
    public async Task Lookup_of_an_unknown_phone_is_not_found_and_an_invalid_phone_is_rejected()
    {
        var owner = await factory.SignInAsync(_client, Ct);

        (await Send(HttpMethod.Post, "/api/customers/lookup", owner, new { phone = BuyerPhone() })).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await Send(HttpMethod.Post, "/api/customers/lookup", owner, new { phone = "12345" })).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Unblock_clears_the_block_and_update_replaces_details()
    {
        var owner = await factory.SignInAsync(_client, Ct);
        var id = (await Json(await Send(HttpMethod.Post, "/api/customers", owner, new { phone = BuyerPhone() }))).GetProperty("id").GetGuid();
        await Send(HttpMethod.Post, $"/api/customers/{id}/block", owner, new { reason = "Fake order" });

        var unblocked = await Json(await Send(HttpMethod.Delete, $"/api/customers/{id}/block", owner));
        var updated = await Json(await Send(HttpMethod.Put, $"/api/customers/{id}", owner, new { name = "Karim", notes = "Call before delivery" }));

        unblocked.GetProperty("isBlocked").GetBoolean().ShouldBeFalse();
        updated.GetProperty("name").GetString().ShouldBe("Karim");
        updated.GetProperty("notes").GetString().ShouldBe("Call before delivery");
    }

    [Fact]
    public async Task Search_by_name_or_phone_digits_with_paging()
    {
        var owner = await factory.SignInAsync(_client, Ct);
        var phone = BuyerPhone();
        await Send(HttpMethod.Post, "/api/customers", owner, new { phone, name = "Nusrat Jahan" });
        await Send(HttpMethod.Post, "/api/customers", owner, new { phone = BuyerPhone(), name = "Karim Uddin" });

        var byName = await Json(await Send(HttpMethod.Get, "/api/customers?search=nusrat", owner));
        var byDigits = await Json(await Send(HttpMethod.Get, $"/api/customers?search={phone[3..9]}", owner));
        var firstPage = await Json(await Send(HttpMethod.Get, "/api/customers?pageSize=1", owner));

        byName.GetProperty("items").EnumerateArray().ShouldHaveSingleItem().GetProperty("name").GetString().ShouldBe("Nusrat Jahan");
        byDigits.GetProperty("items").EnumerateArray().ShouldHaveSingleItem().GetProperty("name").GetString().ShouldBe("Nusrat Jahan");
        firstPage.GetProperty("totalCount").GetInt32().ShouldBe(2);
        firstPage.GetProperty("items").GetArrayLength().ShouldBe(1);
    }

    [Fact]
    public async Task Same_phone_in_two_shops_is_two_independent_customers()
    {
        var shopA = await factory.SignInAsync(_client, Ct);
        var shopB = await factory.SignInAsync(_client, Ct);
        var phone = BuyerPhone();

        var inA = await Json(await Send(HttpMethod.Post, "/api/customers", shopA, new { phone, name = "Name in A" }));
        var inB = await Send(HttpMethod.Post, "/api/customers", shopB, new { phone, name = "Name in B" });
        await Send(HttpMethod.Post, $"/api/customers/{inA.GetProperty("id").GetGuid()}/block", shopA, new { reason = "A only" });

        inB.StatusCode.ShouldBe(HttpStatusCode.Created); // B's first sight of this phone
        var seenByB = await Json(await Send(HttpMethod.Post, "/api/customers/lookup", shopB, new { phone }));
        seenByB.GetProperty("name").GetString().ShouldBe("Name in B");
        seenByB.GetProperty("isBlocked").GetBoolean().ShouldBeFalse(); // A's block stays in A
        (await Send(HttpMethod.Get, $"/api/customers/{inA.GetProperty("id").GetGuid()}", shopB)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Packers_cannot_see_customers()
    {
        var owner = await factory.SignInAsync(_client, Ct);
        var packer = await factory.InviteAndSignInAsync(_client, owner, "Packer", Ct);

        (await Send(HttpMethod.Get, "/api/customers", packer)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Customer_phone_and_details_stay_out_of_logs_and_audit()
    {
        var owner = await factory.SignInAsync(_client, Ct);
        var phone = BuyerPhone();

        var response = await Send(HttpMethod.Post, "/api/customers", owner, new { phone, name = "Shirin Akter", address = "Road 7, Uttara" });
        var correlationId = response.Headers.GetValues(CorrelationIdMiddleware.HeaderName).Single();

        await factory.Logs.WaitForAsync(correlationId, e => e.MessageTemplate.Text.StartsWith("HTTP", StringComparison.Ordinal), Ct);
        var logged = string.Join("\n", factory.Logs.For(correlationId).Select(e => e.RenderMessage(CultureInfo.InvariantCulture) + " " +
            string.Join(" ", e.Properties.Values)));
        logged.ShouldNotContain(phone[1..]);
        logged.ShouldNotContain("Shirin");

        var audit = await Json(await Send(HttpMethod.Get, "/api/audit", owner));
        var created = audit.GetProperty("items").EnumerateArray().Single(i => i.GetProperty("entityType").GetString() == "Customer");
        var changes = created.GetProperty("changes").ToString();
        changes.ShouldNotContain(phone[1..]);
        changes.ShouldNotContain("Shirin");
        changes.ShouldNotContain("Uttara");
    }
}
