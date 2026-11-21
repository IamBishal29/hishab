namespace Hishab.Infrastructure.Couriers.Pathao;

/// <summary>Pathao merchant API (section "Couriers:Pathao"). Checked at startup.</summary>
public sealed class PathaoOptions
{
    public const string SectionName = "Couriers:Pathao";

    public const string SandboxUrl = "https://courier-api-sandbox.pathao.com/";

    /// <summary>
    /// The sandbox unless a deployment sets the production URL on purpose. Tests point it at
    /// WireMock: no test or CI run ever calls a real Pathao API.
    /// </summary>
    public Uri BaseUrl { get; set; } = new(SandboxUrl);

    /// <summary>Normal delivery (48 hours); 12 is on-demand.</summary>
    public int DeliveryType { get; set; } = 48;

    /// <summary>Pathao needs a weight; most social-commerce parcels are under half a kilo.</summary>
    public decimal DefaultItemWeightKg { get; set; } = 0.5m;

    public bool IsValid() =>
        BaseUrl is { IsAbsoluteUri: true } url && (url.Scheme == Uri.UriSchemeHttps || url.IsLoopback)
        && DeliveryType is 12 or 48
        && DefaultItemWeightKg is > 0 and <= 10;
}
