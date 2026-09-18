using FulfillmentHub.Domain.Common;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace FulfillmentHub.Api.ErrorHandling;

/// <summary>
/// Converts unhandled exceptions into RFC 9457 ProblemDetails. Domain invariant violations map to 422; malformed
/// requests (unreadable JSON body, wrong types) keep their 4xx; everything else is a 500 whose internal details are
/// only exposed in Development (by the ProblemDetails service).
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
        var (statusCode, title, detail) = exception switch
        {
            DomainException => (StatusCodes.Status422UnprocessableEntity, "Domain rule violated", exception.Message),
            // Minimal APIs throw this instead of answering 400 when RouteHandlerOptions.ThrowOnBadRequest is on (Development default).
            BadHttpRequestException bad => (bad.StatusCode, "Malformed request", "The request could not be read. Check the JSON body and the types of its fields."),
            _ => (StatusCodes.Status500InternalServerError, "An unexpected error occurred", null),
        };

        switch (exception)
        {
            case DomainException:
                LogDomainRuleViolated(exception.Message, httpContext.Request.Method, httpContext.Request.Path);
                break;
            case BadHttpRequestException:
                LogMalformedRequest(exception.Message, httpContext.Request.Method, httpContext.Request.Path);
                break;
            default:
                LogUnhandledException(exception, httpContext.Request.Method, httpContext.Request.Path);
                break;
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
                Detail = detail,
            },
        });
    }

    [LoggerMessage(EventId = 1000, Level = LogLevel.Error, Message = "Unhandled exception for {Method} {Path}")]
    private partial void LogUnhandledException(Exception exception, string method, PathString path);

    [LoggerMessage(EventId = 1002, Level = LogLevel.Warning, Message = "Domain rule violated ({Reason}) for {Method} {Path}")]
    private partial void LogDomainRuleViolated(string reason, string method, PathString path);

    [LoggerMessage(EventId = 1003, Level = LogLevel.Warning, Message = "Malformed request ({Reason}) for {Method} {Path}")]
    private partial void LogMalformedRequest(string reason, string method, PathString path);
}
