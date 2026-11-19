namespace Hishab.Domain.Common;

/// <summary>
/// An expected business failure. The API maps each subtype to an HTTP status and a
/// ProblemDetails body; messages are shown to users, so never put personal data in them.
/// </summary>
public abstract class DomainException(string message) : Exception(message);

/// <summary>The requested resource does not exist for the current tenant (HTTP 404).</summary>
public sealed class NotFoundException(string resource, object key)
    : DomainException($"{resource} '{key}' was not found.");

/// <summary>The request conflicts with the current state, e.g. an invalid order transition (HTTP 409).</summary>
public sealed class ConflictException(string message) : DomainException(message);

/// <summary>Input broke a business rule; <see cref="Errors"/> is keyed by field name (HTTP 400).</summary>
public sealed class DomainValidationException(IReadOnlyDictionary<string, string[]> errors)
    : DomainException("One or more validation errors occurred.")
{
    public IReadOnlyDictionary<string, string[]> Errors { get; } = errors;

    public DomainValidationException(string field, string error)
        : this(new Dictionary<string, string[]> { [field] = [error] })
    {
    }
}
