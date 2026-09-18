using FulfillmentHub.Application.Common;
using Microsoft.AspNetCore.Http.HttpResults;

namespace FulfillmentHub.Api.Common;

/// <summary>Single place where use-case failures become RFC 9457 ProblemDetails.</summary>
public static class FailureResults
{
    public static ProblemHttpResult ToProblem(this Failure failure)
    {
        var (status, title) = failure.Kind switch
        {
            FailureKind.Validation => (StatusCodes.Status400BadRequest, "Invalid request"),
            FailureKind.NotFound => (StatusCodes.Status404NotFound, "Not found"),
            FailureKind.Conflict => (StatusCodes.Status409Conflict, "Conflict"),
            FailureKind.Unauthorized => (StatusCodes.Status401Unauthorized, "Authentication failed"),
            FailureKind.Forbidden => (StatusCodes.Status403Forbidden, "Forbidden"),
            FailureKind.Unavailable => (StatusCodes.Status503ServiceUnavailable, "Service unavailable"),
            _ => (StatusCodes.Status500InternalServerError, "Unexpected failure"),
        };

        return TypedResults.Problem(
            title: title,
            detail: failure.Message,
            statusCode: status,
            extensions: new Dictionary<string, object?> { ["code"] = failure.Code });
    }
}
