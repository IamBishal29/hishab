using System.Collections.Concurrent;
using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using Hishab.Application.Couriers;
using Hishab.Domain.Couriers;
using Microsoft.Extensions.Caching.Memory;

namespace Hishab.Infrastructure.Couriers.Pathao;

/// <summary>What we keep for a Pathao connection (encrypted): never the merchant's password.</summary>
internal sealed record PathaoTokenSet(string AccessToken, DateTimeOffset ExpiresAt, string RefreshToken)
{
    public const string AccessTokenKey = "accessToken";
    public const string ExpiresAtKey = "accessTokenExpiresAt";
    public const string RefreshTokenKey = "refreshToken";

    public static PathaoTokenSet? From(CourierCredentials credentials) =>
        credentials.Values.TryGetValue(AccessTokenKey, out var access)
        && credentials.Values.TryGetValue(RefreshTokenKey, out var refresh)
        && DateTimeOffset.TryParse(credentials.Values.GetValueOrDefault(ExpiresAtKey), CultureInfo.InvariantCulture, DateTimeStyles.None, out var expires)
            ? new PathaoTokenSet(access, expires, refresh)
            : null;

    /// <summary>The stored connection with these tokens (client keys and store kept, anything else dropped).</summary>
    public Dictionary<string, string> Into(IReadOnlyDictionary<string, string> keys) => new(StringComparer.Ordinal)
    {
        [PathaoCourierProvider.ClientIdKey] = keys[PathaoCourierProvider.ClientIdKey],
        [PathaoCourierProvider.ClientSecretKey] = keys[PathaoCourierProvider.ClientSecretKey],
        [PathaoCourierProvider.StoreIdKey] = keys[PathaoCourierProvider.StoreIdKey],
        [AccessTokenKey] = AccessToken,
        [ExpiresAtKey] = ExpiresAt.ToString("O", CultureInfo.InvariantCulture),
        [RefreshTokenKey] = RefreshToken,
    };
}

/// <summary>
/// Access tokens per Pathao connection. Uses the stored token while it is valid; otherwise refreshes
/// with the refresh token (never a password), once per connection even when many calls need it at
/// the same moment (bulk booking), and saves the rotated tokens through
/// <see cref="CourierCredentials.SaveRefreshedAsync"/>. Callers still holding the old snapshot find
/// the new tokens here, keyed by the refresh token they hold.
/// </summary>
internal sealed class PathaoTokens(IMemoryCache cache, TimeProvider clock)
{
    /// <summary>Refresh a little early: a token must not expire between our check and Pathao's.</summary>
    private static readonly TimeSpan _margin = TimeSpan.FromMinutes(5);

    private readonly ConcurrentDictionary<string, Lazy<Task<PathaoTokenSet>>> _refreshing = new(StringComparer.Ordinal);

    /// <param name="rejected">The token Pathao just refused with 401: get a newer one.</param>
    public async Task<string> AccessTokenAsync(HttpClient http, CourierCredentials credentials, string? rejected, CancellationToken cancellationToken)
    {
        var current = PathaoTokenSet.From(credentials) ?? throw new CourierAuthExpiredException(CourierProvider.Pathao);
        if (cache.TryGetValue<PathaoTokenSet>(Key(current.RefreshToken), out var newer) && newer is not null)
        {
            current = newer; // another call already rotated this connection's tokens
        }

        if (current.AccessToken != rejected && current.ExpiresAt - _margin > clock.GetUtcNow())
        {
            return current.AccessToken;
        }

        var key = Key(current.RefreshToken);
        var refresh = _refreshing.GetOrAdd(key, _ => new Lazy<Task<PathaoTokenSet>>(() => RefreshAsync(http, credentials, current.RefreshToken, key)));
        try
        {
            // Not the caller's token: once started, the refresh must finish and be saved, or the
            // rotated refresh token is lost and the seller has to reconnect.
            return (await refresh.Value.WaitAsync(cancellationToken)).AccessToken;
        }
        finally
        {
            if (refresh.Value.IsCompleted)
            {
                _refreshing.TryRemove(new KeyValuePair<string, Lazy<Task<PathaoTokenSet>>>(key, refresh));
            }
        }
    }

    private async Task<PathaoTokenSet> RefreshAsync(HttpClient http, CourierCredentials credentials, string refreshToken, string key)
    {
        var tokens = await IssueAsync(http, clock, new
        {
            client_id = credentials[PathaoCourierProvider.ClientIdKey],
            client_secret = credentials[PathaoCourierProvider.ClientSecretKey],
            grant_type = "refresh_token",
            refresh_token = refreshToken,
        }, CancellationToken.None) ?? throw new CourierAuthExpiredException(CourierProvider.Pathao);

        cache.Set(key, tokens, TimeSpan.FromHours(1));
        await credentials.SaveRefreshedAsync(tokens.Into(credentials.Values), CancellationToken.None);
        return tokens;
    }

    /// <summary>Pathao's token endpoint (password or refresh grant). Null when it refuses the grant.</summary>
    public static async Task<PathaoTokenSet?> IssueAsync(HttpClient http, TimeProvider clock, object grant, CancellationToken cancellationToken)
    {
        using var response = await PathaoCourierProvider.SendAsync(http, HttpMethod.Post, "aladdin/api/v1/issue-token", null, grant, cancellationToken);
        if (response.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.Unauthorized or HttpStatusCode.UnprocessableEntity)
        {
            return null;
        }

        var body = await PathaoCourierProvider.ReadAsync<TokenResponse>(response, cancellationToken);
        return body is { AccessToken: { Length: > 0 } access, RefreshToken: { Length: > 0 } refresh, ExpiresIn: > 0 }
            ? new PathaoTokenSet(access, clock.GetUtcNow().AddSeconds(body.ExpiresIn), refresh)
            : throw new CourierUnavailableException(CourierProvider.Pathao); // a 200 we cannot use: treat as no answer
    }

    /// <summary>Hashed: refresh tokens are secrets and must not sit in memory as cache keys.</summary>
    private static string Key(string refreshToken) =>
        "pathao:" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(refreshToken)));

    private sealed record TokenResponse(string? AccessToken, string? RefreshToken, long ExpiresIn);
}

internal static class PathaoJson
{
    public static readonly System.Text.Json.JsonSerializerOptions Options = new(System.Text.Json.JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.SnakeCaseLower,
    };

    public static JsonContent Content(object body) => JsonContent.Create(body, body.GetType(), options: Options);
}
