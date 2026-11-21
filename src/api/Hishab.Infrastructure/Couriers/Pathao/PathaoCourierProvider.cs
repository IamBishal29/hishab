using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Hishab.Application.Couriers;
using Hishab.Domain.Common;
using Hishab.Domain.Couriers;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Polly.CircuitBreaker;
using Polly.Timeout;

namespace Hishab.Infrastructure.Couriers.Pathao;

/// <summary>
/// Pathao's merchant API ("Aladdin"), sandbox by default. Sellers connect with their client keys and
/// merchant login once; the login is traded for tokens at once and never stored (CLAUDE.md
/// guardrail). Calls go through a named HttpClient from the factory (a singleton must not keep one
/// HttpClient: DNS changes) with a resilience pipeline that never re-sends a booking on its own.
/// Field names and status codes follow Pathao's merchant docs; see the PR for the sandbox check.
/// </summary>
internal sealed partial class PathaoCourierProvider(
    IHttpClientFactory httpFactory,
    PathaoTokens tokens,
    IOptions<PathaoOptions> options,
    TimeProvider clock,
    ILogger<PathaoCourierProvider> logger) : ICourierProvider
{
    public const string HttpClientName = "pathao";
    public const string ClientIdKey = "clientId";
    public const string ClientSecretKey = "clientSecret";
    public const string StoreIdKey = "storeId";
    private const string UsernameKey = "username";
    private const string PasswordKey = "password";

    public CourierProvider Code => CourierProvider.Pathao;

    public IReadOnlyList<CourierCredentialField> CredentialFields { get; } =
    [
        new(ClientIdKey, "Client ID"),
        new(ClientSecretKey, "Client secret"),
        new(StoreIdKey, "Store ID"),
        new(UsernameKey, "Merchant login email"),
        new(PasswordKey, "Merchant password (used once to connect, never stored)"),
    ];

    /// <summary>No bulk endpoint we can rely on: bulk booking sends single calls, a few at a time.</summary>
    public int? MaxBulkBookingSize => null;

    public async Task<CredentialCheck> ValidateCredentialsAsync(CourierCredentials credentials, CancellationToken cancellationToken)
    {
        if (!int.TryParse(credentials[StoreIdKey], NumberStyles.None, CultureInfo.InvariantCulture, out _))
        {
            return CredentialCheck.Invalid("The Pathao store ID is a number; find it under Stores in your Pathao merchant panel.");
        }

        var issued = await PathaoTokens.IssueAsync(Http(), clock, new
        {
            client_id = credentials[ClientIdKey],
            client_secret = credentials[ClientSecretKey],
            grant_type = "password",
            username = credentials[UsernameKey],
            password = credentials[PasswordKey],
        }, cancellationToken);

        return issued is null
            ? CredentialCheck.Invalid("Pathao rejected these keys or this login. Check them in your Pathao merchant panel.")
            : CredentialCheck.ValidKeeping(issued.Into(credentials.Values)); // the password ends here
    }

    public Task<CourierQuote> QuoteAsync(CourierCredentials credentials, ParcelRequest parcel, CancellationToken cancellationToken) =>
        throw new DomainValidationException("courier", "Pathao prices need the delivery city and zone, which orders do not have yet.");

    public async Task<BookingResult> BookAsync(CourierCredentials credentials, ParcelRequest parcel, CancellationToken cancellationToken)
    {
        var order = new
        {
            store_id = int.Parse(credentials[StoreIdKey], CultureInfo.InvariantCulture),
            merchant_order_id = parcel.MerchantReference,
            recipient_name = parcel.RecipientName,
            recipient_phone = LocalPhone(parcel.RecipientPhone),
            recipient_address = parcel.RecipientAddress,
            delivery_type = options.Value.DeliveryType,
            item_type = 2, // parcel (1 = document)
            special_instruction = parcel.Note,
            item_quantity = parcel.ItemCount,
            item_weight = options.Value.DefaultItemWeightKg,
            amount_to_collect = parcel.CodAmount.Amount,
        };

        using var response = await AuthorizedAsync(credentials, HttpMethod.Post, "aladdin/api/v1/orders", order, cancellationToken);
        if (response.StatusCode == HttpStatusCode.UnprocessableEntity)
        {
            var problem = await ReadAsync<Envelope<JsonElement>>(response, cancellationToken);
            LogRefused(logger, parcel.MerchantReference); // Pathao's text may echo the address: not logged
            return BookingResult.Rejected(parcel.MerchantReference, RefusalFor(problem));
        }

        EnsureAnswered(response);
        var created = (await ReadAsync<Envelope<CreatedOrder>>(response, cancellationToken))?.Data;
        if (created is not { ConsignmentId.Length: > 0 })
        {
            throw new CourierUnavailableException(Code); // a 200 without a parcel ID: outcome unknown, retry is safe
        }

        return BookingResult.Booked(parcel.MerchantReference, created.ConsignmentId, created.ConsignmentId, Money.Bdt(created.DeliveryFee ?? 0m));
    }

    public Task<IReadOnlyList<BookingResult>> BookManyAsync(
        CourierCredentials credentials, IReadOnlyList<ParcelRequest> parcels, CancellationToken cancellationToken) =>
        throw new NotSupportedException("Pathao has no bulk endpoint here; MaxBulkBookingSize is null.");

    public async Task<ParcelStatusResult> GetStatusAsync(CourierCredentials credentials, string consignmentId, CancellationToken cancellationToken)
    {
        using var response = await AuthorizedAsync(credentials, HttpMethod.Get, $"aladdin/api/v1/orders/{Uri.EscapeDataString(consignmentId)}", null, cancellationToken);
        EnsureAnswered(response);
        var info = (await ReadAsync<Envelope<OrderInfo>>(response, cancellationToken))?.Data
            ?? throw new CourierUnavailableException(Code);
        var status = PathaoStatuses.Map(info.OrderStatus);
        return new ParcelStatusResult(consignmentId, status, info.UpdatedAt, status == ParcelStatus.Unknown ? info.OrderStatus : null);
    }

    private HttpClient Http() => httpFactory.CreateClient(HttpClientName);

    /// <summary>Sends with the connection's access token; on 401 gets a newer one once and resends (Pathao did nothing with a 401).</summary>
    private async Task<HttpResponseMessage> AuthorizedAsync(CourierCredentials credentials, HttpMethod method, string path, object? body, CancellationToken cancellationToken)
    {
        var http = Http();
        var token = await tokens.AccessTokenAsync(http, credentials, rejected: null, cancellationToken);
        var response = await SendAsync(http, method, path, token, body, cancellationToken);
        if (response.StatusCode != HttpStatusCode.Unauthorized)
        {
            return response;
        }

        response.Dispose();
        token = await tokens.AccessTokenAsync(http, credentials, rejected: token, cancellationToken);
        response = await SendAsync(http, method, path, token, body, cancellationToken);
        if (response.StatusCode == HttpStatusCode.Unauthorized)
        {
            response.Dispose();
            throw new CourierAuthExpiredException(CourierProvider.Pathao);
        }

        return response;
    }

    /// <summary>One HTTP call; no answer at all (network, timeout, open circuit) becomes <see cref="CourierUnavailableException"/>.</summary>
    internal static async Task<HttpResponseMessage> SendAsync(HttpClient http, HttpMethod method, string path, string? token, object? body, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(method, path);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        if (token is not null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        if (body is not null)
        {
            request.Content = PathaoJson.Content(body);
        }

        try
        {
            return await http.SendAsync(request, cancellationToken);
        }
        catch (Exception e) when (e is HttpRequestException or TimeoutRejectedException or BrokenCircuitException
            || (e is TaskCanceledException && !cancellationToken.IsCancellationRequested))
        {
            throw new CourierUnavailableException(CourierProvider.Pathao);
        }
    }

    internal static async Task<T?> ReadAsync<T>(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        try
        {
            return await response.Content.ReadFromJsonAsync<T>(PathaoJson.Options, cancellationToken);
        }
        catch (JsonException)
        {
            return default; // HTML error pages from a proxy, truncated bodies: the caller decides
        }
    }

    /// <summary>5xx (after the pipeline's retries for reads) and other surprises: the outcome is unknown.</summary>
    private void EnsureAnswered(HttpResponseMessage response)
    {
        if (!response.IsSuccessStatusCode)
        {
            LogUnexpectedStatus(logger, (int)response.StatusCode);
            throw new CourierUnavailableException(Code);
        }
    }

    private static string RefusalFor(Envelope<JsonElement>? problem)
    {
        var errors = problem?.Errors ?? [];
        if (errors.Keys.Any(k => k.Equals("merchant_order_id", StringComparison.OrdinalIgnoreCase)))
        {
            // We cannot look a parcel up by our reference: never book blindly a second time.
            return "Pathao already has an order with this reference. Check your Pathao merchant panel before booking again.";
        }

        var details = errors.SelectMany(e => e.Value).Where(m => !string.IsNullOrWhiteSpace(m)).ToList();
        return details.Count > 0 ? string.Join(" ", details) : problem?.Message ?? "Pathao refused this parcel.";
    }

    /// <summary>Pathao wants the 11-digit local number (01XXXXXXXXX); we store E.164 (+8801XXXXXXXXX).</summary>
    internal static string LocalPhone(string e164) => e164.StartsWith("+880", StringComparison.Ordinal) ? "0" + e164[4..] : e164;

    private sealed record Envelope<T>(string? Message, int? Code, T? Data, Dictionary<string, string[]>? Errors);

    private sealed record CreatedOrder(string? ConsignmentId, string? MerchantOrderId, string? OrderStatus, decimal? DeliveryFee);

    private sealed record OrderInfo(string? ConsignmentId, string? OrderStatus, DateTimeOffset? UpdatedAt);

    [LoggerMessage(Level = LogLevel.Information, Message = "Pathao refused parcel {MerchantReference}")]
    private static partial void LogRefused(ILogger logger, string merchantReference);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Pathao answered HTTP {StatusCode}")]
    private static partial void LogUnexpectedStatus(ILogger logger, int statusCode);
}
