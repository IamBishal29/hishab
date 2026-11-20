namespace Hishab.Application.Common.Auditing;

/// <summary>Who is acting and in which request. HTTP requests read the token; background jobs supply their own.</summary>
public interface IRequestContext
{
    /// <summary>Null for anonymous calls (sign-up) and system work.</summary>
    Guid? UserId { get; }

    string? CorrelationId { get; }
}
