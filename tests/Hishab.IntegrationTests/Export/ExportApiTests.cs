using System.Net;
using System.Text;
using System.Text.Json;
using Hishab.IntegrationTests.Finance;
using Hishab.IntegrationTests.Support;

namespace Hishab.IntegrationTests.Export;

/// <summary>GET /api/export/orders.csv and customers.csv: file headers, escaping, filters and isolation.</summary>
public class ExportApiTests(HishabApiFactory factory) : IClassFixture<HishabApiFactory>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static readonly byte[] _bom = [0xEF, 0xBB, 0xBF];

    private readonly FinanceScenario _shop = new(factory);

    private const string OrderHeader =
        "number,created_at,status,source,customer_name,customer_phone,delivery_address,area,items,subtotal,delivery_charge,total,advance_paid,cod_amount,product_cost,courier,consignment_id,note";

    /// <summary>A New order for a buyer with the given name and address (one 500 line costing 300, 60 delivery).</summary>
    private async Task<int> Order(JsonElement owner, string phone, string name, string address, string? note = null)
    {
        var product = await _shop.Ok(HttpMethod.Post, "/api/products", owner, new
        {
            name = "Polo shirt",
            variants = new[] { new { sku = "POLO-" + Guid.NewGuid().ToString("N")[..6], size = "M", price = 500m, costPrice = 300m } },
        });
        var created = await _shop.Ok(HttpMethod.Post, "/api/orders", owner, new
        {
            phone,
            customerName = name,
            deliveryAddress = address,
            isInsideDhaka = true,
            source = "Facebook",
            advancePaid = 100m,
            note,
            lines = new[] { new { variantId = product.GetProperty("variants")[0].GetProperty("id").GetString(), quantity = 2 } },
        });
        return created.GetProperty("order").GetProperty("number").GetInt32();
    }

    private async Task<(HttpResponseMessage Response, string[] Lines)> Download(JsonElement user, string path)
    {
        var response = await _shop.Send(HttpMethod.Get, path, user);
        response.EnsureSuccessStatusCode();
        var bytes = await response.Content.ReadAsByteArrayAsync(Ct);
        bytes.Take(3).ShouldBe(_bom); // Excel reads the Bangla as UTF-8 only with the BOM
        var text = Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3);
        return (response, text.Split("\r\n", StringSplitOptions.RemoveEmptyEntries));
    }

    [Fact]
    public async Task Orders_download_as_an_Excel_ready_file_with_formulas_neutralised()
    {
        var owner = await _shop.ShopWithFake();
        var number = await Order(owner, "01712345678", "=HYPERLINK(\"http://evil.example\",\"Click\")", "বাড়ি ৪, রোড ২, মিরপুর", note: "@SUM(A1)");

        var (response, lines) = await Download(owner, "/api/export/orders.csv");

        response.Content.Headers.ContentType!.MediaType.ShouldBe("text/csv");
        response.Content.Headers.ContentDisposition!.DispositionType.ShouldBe("attachment");
        response.Content.Headers.ContentDisposition.FileName!.Trim('"').ShouldStartWith("orders-");
        response.Headers.CacheControl!.NoStore.ShouldBeTrue();
        lines[0].ShouldBe(OrderHeader);
        lines.Length.ShouldBe(2);

        var row = lines[1];
        row.ShouldStartWith($"{number},");
        row.ShouldContain(",New,Facebook,\"'=HYPERLINK(\"\"http://evil.example\"\",\"\"Click\"\")\",01712-345678,");
        row.ShouldContain("\"বাড়ি ৪, রোড ২, মিরপুর\",Inside Dhaka,Polo shirt (M) x2,1000,60,1060,100,960,600,,,'@SUM(A1)");
    }

    [Fact]
    public async Task Order_filters_match_the_order_list()
    {
        var owner = await _shop.ShopWithFake();
        var confirmed = await _shop.ConfirmedOrder(owner, price: 440m, advance: 0m);
        await Order(owner, "01812345678", "Karim", "Dhanmondi");
        var confirmedNumber = (await _shop.Ok(HttpMethod.Get, $"/api/orders/{confirmed}", owner)).GetProperty("order").GetProperty("number").GetInt32();

        var (_, byStatus) = await Download(owner, "/api/export/orders.csv?status=Confirmed");
        byStatus.Skip(1).Select(l => l.Split(',')[0]).ShouldBe([confirmedNumber.ToString(System.Globalization.CultureInfo.InvariantCulture)]);

        var (_, bySearch) = await Download(owner, "/api/export/orders.csv?search=Karim");
        bySearch.Length.ShouldBe(2);
        bySearch[1].ShouldContain(",Karim,01812-345678,");

        (await _shop.Send(HttpMethod.Get, "/api/export/orders.csv?from=2026-11-10&to=2026-11-01", owner)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Customers_download_with_their_order_counts()
    {
        var owner = await _shop.ShopWithFake();
        await _shop.DeliveredOrder(owner, price: 440m);   // FinanceScenario's buyer: Rahima Begum, 01912345678
        await _shop.ReturnedOrder(owner, price: 440m);
        await Order(owner, "01612345678", "-Mitu", "Uttara");

        var (_, lines) = await Download(owner, "/api/export/customers.csv");

        lines[0].ShouldBe("name,phone,address,notes,blocked,blocked_reason,created_at,orders,delivered,returned,cancelled");
        lines.Length.ShouldBe(3);
        // The test clock stands still, so both share created_at: find rows by content, not position.
        var mitu = lines.Single(l => l.StartsWith("'-Mitu,", StringComparison.Ordinal)); // neutralised
        mitu.ShouldStartWith("'-Mitu,01612-345678,Uttara,,no,,");
        mitu.ShouldEndWith(",1,0,0,0");
        var rahima = lines.Single(l => l.StartsWith("Rahima Begum,", StringComparison.Ordinal));
        rahima.ShouldStartWith("Rahima Begum,01912-345678,\"House 4, Road 2, Mirpur\",,no,,");
        rahima.ShouldEndWith(",2,1,1,0");
    }

    [Fact]
    public async Task Another_shops_rows_never_appear()
    {
        var shopA = await _shop.ShopWithFake();
        await Order(shopA, "01712345678", "Shop A buyer", "Mirpur");
        var shopB = await _shop.ShopWithFake();

        (await Download(shopB, "/api/export/orders.csv")).Lines.ShouldBe([OrderHeader]);
        (await Download(shopB, "/api/export/customers.csv")).Lines.Length.ShouldBe(1);
    }

    [Theory]
    [InlineData("Manager")]
    [InlineData("Packer")]
    public async Task Only_the_owner_exports(string role)
    {
        var owner = await _shop.ShopWithFake();
        var staff = await factory.InviteAndSignInAsync(_shop.Client, owner, role, Ct);

        (await _shop.Send(HttpMethod.Get, "/api/export/orders.csv", staff)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await _shop.Send(HttpMethod.Get, "/api/export/customers.csv", staff)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }
}
