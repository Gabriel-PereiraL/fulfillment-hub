using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FulfillmentHub.Application.Common;
using FulfillmentHub.Application.Payments;
using FulfillmentHub.Domain.Payments;
using FulfillmentHub.Infrastructure.Telemetry;
using Microsoft.Extensions.Logging;
using Polly.CircuitBreaker;
using Polly.Timeout;

namespace FulfillmentHub.Infrastructure.Providers.Payments;

/// <summary>
/// HTTP adapter over the simulated payment provider. Retries, timeouts and the circuit breaker live in the
/// <see cref="HttpClient"/> pipeline (see <see cref="PaymentProviderServiceCollectionExtensions"/>); this class
/// only translates the wire contract and the provider's error codes into domain statuses and <see cref="Failure"/>s.
/// </summary>
public sealed partial class SimulatedPaymentGatewayClient(HttpClient httpClient, ILogger<SimulatedPaymentGatewayClient> logger) : IPaymentGatewayClient
{
    public const string Name = "simulated-psp";

    private static readonly ActivitySource ActivitySource = new(TelemetryNames.ActivitySource);

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
    };

    public string ProviderName => Name;

    public async Task<Result<GatewayPayment>> CreateAsync(GatewayCreatePayment request, CancellationToken cancellationToken)
    {
        using var activity = StartActivity("CreatePayment");

        var body = new ProviderCreatePaymentRequest(
            ToCents(request.Amount.Amount),
            request.Amount.Currency.ToLowerInvariant(),
            request.OrderReference,
            request.CustomerReference,
            Capture: true);

        using var message = new HttpRequestMessage(HttpMethod.Post, "payments/v1/payments") { Content = JsonContent.Create(body, options: JsonOptions) };
        message.Headers.Add("Idempotency-Key", request.IdempotencyKey);

        return await SendAsync(message, activity, ReadPaymentAsync, cancellationToken);
    }

    public async Task<Result<GatewayPayment>> GetAsync(string providerPaymentId, CancellationToken cancellationToken)
    {
        using var activity = StartActivity("GetPayment");
        using var message = new HttpRequestMessage(HttpMethod.Get, $"payments/v1/payments/{Uri.EscapeDataString(providerPaymentId)}");

        return await SendAsync(message, activity, ReadPaymentAsync, cancellationToken);
    }

    public async Task<Result<GatewayRefund>> RefundAsync(string providerPaymentId, string idempotencyKey, CancellationToken cancellationToken)
    {
        using var activity = StartActivity("RefundPayment");
        using var message = new HttpRequestMessage(HttpMethod.Post, $"payments/v1/payments/{Uri.EscapeDataString(providerPaymentId)}/refunds")
        {
            Content = JsonContent.Create(new ProviderRefundRequest(null), options: JsonOptions),
        };
        message.Headers.Add("Idempotency-Key", idempotencyKey);

        return await SendAsync(message, activity, static async (response, ct) =>
        {
            var refund = await response.Content.ReadFromJsonAsync<ProviderRefundResponse>(JsonOptions, ct)
                ?? throw new InvalidOperationException("Empty refund response.");
            return new GatewayRefund(refund.RefundId, PaymentStatus.Refunded);
        }, cancellationToken);
    }

    private async Task<Result<T>> SendAsync<T>(
        HttpRequestMessage message,
        Activity? activity,
        Func<HttpResponseMessage, CancellationToken, Task<T>> readSuccess,
        CancellationToken cancellationToken)
    {
        HttpResponseMessage response;
        try
        {
            response = await httpClient.SendAsync(message, cancellationToken);
        }
        catch (Exception exception) when (exception is HttpRequestException or TimeoutRejectedException or BrokenCircuitException
            || (exception is TaskCanceledException && !cancellationToken.IsCancellationRequested))
        {
            activity?.SetStatus(ActivityStatusCode.Error, exception.GetType().Name);
            LogTransportFailure(message.Method.Method, message.RequestUri?.ToString(), exception.GetType().Name);
            return Failure.Unavailable("provider.unavailable", $"Payment provider unreachable ({exception.GetType().Name}).");
        }

        using (response)
        {
            activity?.SetTag("http.response.status_code", (int)response.StatusCode);

            if (response.IsSuccessStatusCode)
            {
                return Result.Ok(await readSuccess(response, cancellationToken));
            }

            var error = await ReadErrorAsync(response, cancellationToken);
            activity?.SetStatus(ActivityStatusCode.Error, error.Code);
            LogProviderError(message.Method.Method, message.RequestUri?.ToString(), (int)response.StatusCode, error.Code);

            return MapFailure(response.StatusCode, error);
        }
    }

    private static Failure MapFailure(HttpStatusCode status, ProviderErrorResponse error)
    {
        var code = "provider." + (error.Code ?? "error");
        var message = error.Message ?? $"Payment provider returned HTTP {(int)status}.";

        return status switch
        {
            HttpStatusCode.BadRequest or HttpStatusCode.UnprocessableEntity => Failure.Validation(code, message),
            HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden => Failure.Forbidden(code, message),
            HttpStatusCode.NotFound => Failure.NotFound(code, message),
            HttpStatusCode.Conflict => Failure.Conflict(code, message),
            HttpStatusCode.TooManyRequests or HttpStatusCode.RequestTimeout => Failure.Unavailable(code, message),
            >= HttpStatusCode.InternalServerError => Failure.Unavailable(code, message),
            _ => Failure.Conflict(code, message),
        };
    }

    private static async Task<GatewayPayment> ReadPaymentAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var payment = await response.Content.ReadFromJsonAsync<ProviderPaymentResponse>(JsonOptions, cancellationToken)
            ?? throw new InvalidOperationException("Empty payment response.");

        return new GatewayPayment(payment.Id, MapStatus(payment.Status), payment.FailureCode, payment.UpdatedAt);
    }

    private static async Task<ProviderErrorResponse> ReadErrorAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        try
        {
            return await response.Content.ReadFromJsonAsync<ProviderErrorResponse>(JsonOptions, cancellationToken) ?? new ProviderErrorResponse(null, null);
        }
        catch (JsonException)
        {
            return new ProviderErrorResponse(null, null);
        }
    }

    public static PaymentStatus MapStatus(string providerStatus) => providerStatus switch
    {
        "pending" => PaymentStatus.Pending,
        "authorized" => PaymentStatus.Authorized,
        "paid" => PaymentStatus.Paid,
        "failed" => PaymentStatus.Failed,
        "cancelled" or "canceled" => PaymentStatus.Cancelled,
        "refunded" => PaymentStatus.Refunded,
        _ => throw new InvalidOperationException($"Unknown provider payment status '{providerStatus}'."),
    };

    internal static long ToCents(decimal amount) => (long)decimal.Round(amount * 100m, 0, MidpointRounding.ToEven);

    private static Activity? StartActivity(string operation)
    {
        var activity = ActivitySource.StartActivity($"Provider {operation}", ActivityKind.Client);
        activity?.SetTag("peer.service", Name);
        activity?.SetTag("provider.operation", operation);
        return activity;
    }

    [LoggerMessage(EventId = 5100, Level = LogLevel.Warning, Message = "Payment provider transport failure on {Method} {Uri}: {Reason}")]
    private partial void LogTransportFailure(string method, string? uri, string reason);

    [LoggerMessage(EventId = 5101, Level = LogLevel.Warning, Message = "Payment provider error on {Method} {Uri}: HTTP {StatusCode} {Code}")]
    private partial void LogProviderError(string method, string? uri, int statusCode, string? code);
}
