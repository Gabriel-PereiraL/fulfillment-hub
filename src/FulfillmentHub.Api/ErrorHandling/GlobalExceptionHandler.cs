using FulfillmentHub.Domain.Common;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace FulfillmentHub.Api.ErrorHandling;

/// <summary>
/// Converts unhandled exceptions into RFC 9457 ProblemDetails. Domain invariant violations map to 422;
/// everything else is a 500 whose internal details are only exposed in Development (by the ProblemDetails service).
/// </summary>
public sealed partial class GlobalExceptionHandler(
    IProblemDetailsService problemDetailsService,
    ILogger<GlobalExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        var (statusCode, title) = exception switch
        {
            DomainException => (StatusCodes.Status422UnprocessableEntity, "Domain rule violated"),
            _ => (StatusCodes.Status500InternalServerError, "An unexpected error occurred"),
        };

        if (exception is DomainException)
        {
            LogDomainRuleViolated(exception.Message, httpContext.Request.Method, httpContext.Request.Path);
        }
        else
        {
            LogUnhandledException(exception, httpContext.Request.Method, httpContext.Request.Path);
        }

        httpContext.Response.StatusCode = statusCode;

        return await problemDetailsService.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            Exception = exception,
            ProblemDetails = new ProblemDetails
            {
                Status = statusCode,
                Title = title,
                Detail = exception is DomainException ? exception.Message : null,
            },
        });
    }

    [LoggerMessage(EventId = 1000, Level = LogLevel.Error, Message = "Unhandled exception for {Method} {Path}")]
    private partial void LogUnhandledException(Exception exception, string method, PathString path);

    [LoggerMessage(EventId = 1002, Level = LogLevel.Warning, Message = "Domain rule violated ({Reason}) for {Method} {Path}")]
    private partial void LogDomainRuleViolated(string reason, string method, PathString path);
}
