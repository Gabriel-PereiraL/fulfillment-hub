using System.Net;

namespace FulfillmentHub.ProviderSimulator.Common;

/// <summary>Error envelope shared by the simulated providers: <c>{ "code": "...", "message": "...", "kind": "error" }</c>.</summary>
public sealed record ProviderError(string Code, string Message, string Kind = "error");

public static class ProviderErrors
{
    public static IResult Problem(HttpStatusCode status, string code, string message) =>
        Results.Json(new ProviderError(code, message), statusCode: (int)status);
}
