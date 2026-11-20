namespace Hishab.Application.Common.Auditing;

/// <summary>Who is acting and in which request. HTTP requests read the token; background jobs supply their own.</summary>
public interface IRequestContext
{
    /// <summary>Null for anonymous calls (sign-up) and system work.</summary>
    Guid? UserId { get; }

    string? CorrelationId { get; }

    /// <summary>
    /// Whether the acting user holds <paramref name="permission"/>. Services use it for
    /// field-level rules (e.g. cost prices), so the rule holds whichever endpoint calls them.
    /// False outside a signed-in request.
    /// </summary>
    bool Has(Domain.Shops.Permission permission);
}
