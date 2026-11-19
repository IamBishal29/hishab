using Hishab.Domain.Common;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace Hishab.Api.Errors;

/// <summary>
/// The one place that turns expected business failures into RFC 7807 ProblemDetails.
/// Anything that is not a <see cref="DomainException"/> falls through to the default
/// handler: a generic 500 with no exception details, logged as an error.
/// </summary>
public sealed partial class DomainExceptionHandler(
    IProblemDetailsService problemDetails,
    ILogger<DomainExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        if (exception is not DomainException domainException)
        {
            return false;
        }

        var (status, title) = domainException switch
        {
            NotFoundException => (StatusCodes.Status404NotFound, "Resource not found"),
            ConflictException => (StatusCodes.Status409Conflict, "Conflict with current state"),
            DomainValidationException => (StatusCodes.Status400BadRequest, "Validation failed"),
            _ => (StatusCodes.Status422UnprocessableEntity, "Business rule violated"),
        };

        ProblemDetails body = domainException is DomainValidationException validation
            ? new HttpValidationProblemDetails(validation.Errors.ToDictionary(e => e.Key, e => e.Value))
            : new ProblemDetails();
        body.Status = status;
        body.Title = title;
        body.Detail = domainException.Message;

        // Expected outcome, not a server fault: Information, without a stack trace.
        var exceptionType = domainException.GetType().Name;
        LogDomainException(logger, exceptionType, status);

        httpContext.Response.StatusCode = status;
        return await problemDetails.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            ProblemDetails = body,
            Exception = exception,
        });
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Request failed with {ExceptionType}, returning {StatusCode}")]
    private static partial void LogDomainException(ILogger logger, string exceptionType, int statusCode);
}
