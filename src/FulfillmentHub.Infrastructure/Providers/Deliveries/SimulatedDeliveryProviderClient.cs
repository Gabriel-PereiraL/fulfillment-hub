using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FulfillmentHub.Application.Common;
using FulfillmentHub.Application.Deliveries;
using FulfillmentHub.Domain.Common;
using FulfillmentHub.Domain.Deliveries;
using FulfillmentHub.Infrastructure.Telemetry;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Polly.CircuitBreaker;
using Polly.Timeout;

namespace FulfillmentHub.Infrastructure.Providers.Deliveries;

/// <summary>
/// HTTP adapter over the simulated, Uber-like delivery provider (docs/INTEGRATIONS.md §2). Retries, timeouts, the
/// circuit breaker and the bearer token live in the <see cref="HttpClient"/> pipeline; this class translates the wire
/// contract and the provider's error codes into domain values and <see cref="Failure"/>s.
/// This project does not connect to Uber infrastructure.
/// </summary>
public sealed partial class SimulatedDeliveryProviderClient(
    HttpClient httpClient,
    IOptions<DeliveryProviderOptions> options,
    ILogger<SimulatedDeliveryProviderClient> logger) : IDeliveryProviderClient
{
    public const string Name = "uber-like-simulator";

    private static readonly ActivitySource ActivitySource = new(TelemetryNames.ActivitySource);

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
    };

    public string ProviderName => Name;

    public async Task<Result<GatewayQuote>> QuoteAsync(GatewayQuoteRequest request, CancellationToken cancellationToken)
    {
        using var activity = StartActivity("CreateQuote");

        var body = new ProviderQuoteRequest(
            ToAddressJson(request.Pickup.Address),
            ToAddressJson(request.Dropoff.Address),
            request.Pickup.Address.Latitude,
            request.Pickup.Address.Longitude,
            request.Dropoff.Address.Latitude,
            request.Dropoff.Address.Longitude,
            request.Pickup.Phone.Value,
            request.Dropoff.Phone.Value,
            request.ManifestTotalValue is { } total ? ToCents(total.Amount) : null);

        using var message = new HttpRequestMessage(HttpMethod.Post, $"{CustomerPath()}/delivery_quotes") { Content = JsonContent.Create(body, options: JsonOptions) };

        return await SendAsync(message, activity, static async (response, ct) =>
        {
            var quote = await response.Content.ReadFromJsonAsync<ProviderQuoteResponse>(JsonOptions, ct)
                ?? throw new InvalidOperationException("Empty quote response.");

            return new GatewayQuote(quote.Id, FromCents(quote.Fee, quote.Currency), quote.Expires, quote.DropoffEta, quote.Duration, quote.PickupDuration);
        }, cancellationToken);
    }

    public async Task<Result<GatewayDelivery>> CreateAsync(GatewayCreateDelivery request, CancellationToken cancellationToken)
    {
        using var activity = StartActivity("CreateDelivery");

        var body = new ProviderCreateDeliveryRequest(
            request.Pickup.Name,
            ToAddressJson(request.Pickup.Address),
            request.Pickup.Phone.Value,
            request.Dropoff.Name,
            ToAddressJson(request.Dropoff.Address),
            request.Dropoff.Phone.Value,
            request.Items.Select(i => new ProviderManifestItem(i.Name, i.Quantity, "small", ToCents(i.UnitPrice.Amount))).ToArray(),
            request.ProviderQuoteId,
            request.ManifestReference,
            ToCents(request.ManifestTotalValue.Amount),
            request.IdempotencyKey,
            ExternalId: request.IdempotencyKey);

        using var message = new HttpRequestMessage(HttpMethod.Post, $"{CustomerPath()}/deliveries") { Content = JsonContent.Create(body, options: JsonOptions) };

        return await SendAsync(message, activity, ReadDeliveryAsync, cancellationToken);
    }

    public async Task<Result<GatewayDelivery>> GetAsync(string providerDeliveryId, CancellationToken cancellationToken)
    {
        using var activity = StartActivity("GetDelivery");
        using var message = new HttpRequestMessage(HttpMethod.Get, $"{CustomerPath()}/deliveries/{Uri.EscapeDataString(providerDeliveryId)}");

        return await SendAsync(message, activity, ReadDeliveryAsync, cancellationToken);
    }

    public async Task<Result<GatewayDelivery>> CancelAsync(string providerDeliveryId, CancellationToken cancellationToken)
    {
        using var activity = StartActivity("CancelDelivery");
        using var message = new HttpRequestMessage(HttpMethod.Post, $"{CustomerPath()}/deliveries/{Uri.EscapeDataString(providerDeliveryId)}/cancel");

        return await SendAsync(message, activity, ReadDeliveryAsync, cancellationToken);
    }

    private string CustomerPath() => $"delivery/v1/customers/{Uri.EscapeDataString(options.Value.CustomerId)}";

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
            return Failure.Unavailable("provider.unavailable", $"Delivery provider unreachable ({exception.GetType().Name}).");
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

    /// <summary>docs/INTEGRATIONS.md §1.4: 4xx are contract/business errors (no retry); 409 carries the existing delivery id.</summary>
    private static Failure MapFailure(HttpStatusCode status, ProviderErrorResponse error)
    {
        var code = "provider." + (error.Code ?? "error");
        var message = error.Message ?? $"Delivery provider returned HTTP {(int)status}.";

        var failure = status switch
        {
            HttpStatusCode.BadRequest or HttpStatusCode.UnprocessableEntity => Failure.Validation(code, message),
            HttpStatusCode.Unauthorized or HttpStatusCode.PaymentRequired or HttpStatusCode.Forbidden => Failure.Forbidden(code, message),
            HttpStatusCode.NotFound => Failure.NotFound(code, message),
            HttpStatusCode.Conflict => Failure.Conflict(code, message),
            HttpStatusCode.TooManyRequests or HttpStatusCode.RequestTimeout => Failure.Unavailable(code, message),
            >= HttpStatusCode.InternalServerError => Failure.Unavailable(code, message),
            _ => Failure.Conflict(code, message),
        };

        return error.Metadata is { Count: > 0 } ? failure with { Metadata = error.Metadata } : failure;
    }

    private static async Task<GatewayDelivery> ReadDeliveryAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var delivery = await response.Content.ReadFromJsonAsync<ProviderDeliveryResponse>(JsonOptions, cancellationToken)
            ?? throw new InvalidOperationException("Empty delivery response.");

        return new GatewayDelivery(
            delivery.Id,
            MapStatus(delivery.Status),
            delivery.Status,
            FromCents(delivery.Fee, delivery.Currency),
            delivery.TrackingUrl,
            ToCourier(delivery.Courier),
            delivery.Updated);
    }

    private static async Task<ProviderErrorResponse> ReadErrorAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        try
        {
            return await response.Content.ReadFromJsonAsync<ProviderErrorResponse>(JsonOptions, cancellationToken) ?? new ProviderErrorResponse(null, null, null);
        }
        catch (JsonException)
        {
            return new ProviderErrorResponse(null, null, null);
        }
    }

    public static DeliveryStatus MapStatus(string providerStatus) => providerStatus switch
    {
        "pending" => DeliveryStatus.Pending,
        "pickup" => DeliveryStatus.Pickup,
        "pickup_complete" => DeliveryStatus.PickupComplete,
        "dropoff" => DeliveryStatus.Dropoff,
        "delivered" => DeliveryStatus.Delivered,
        "canceled" or "cancelled" => DeliveryStatus.Cancelled,
        "returned" => DeliveryStatus.Returned,
        _ => throw new InvalidOperationException($"Unknown provider delivery status '{providerStatus}'."),
    };

    private static CourierInfo? ToCourier(ProviderCourier? courier) =>
        courier is null ? null : MapCourier(courier.Name, courier.VehicleType, courier.PhoneNumber, courier.Location?.Lat, courier.Location?.Lng);

    /// <summary>Courier details as the provider reports them (API responses and webhooks alike); the phone is stored masked.</summary>
    public static CourierInfo? MapCourier(string? name, string? vehicleType, string? phoneNumber, double? latitude, double? longitude)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return null;
        }

        PhoneNumber? phone = null;
        if (!string.IsNullOrWhiteSpace(phoneNumber))
        {
            try
            {
                phone = PhoneNumber.Of(phoneNumber);
            }
            catch (DomainException)
            {
                // Provider phone formats vary; the courier phone is optional for us.
            }
        }

        return CourierInfo.Create(name, phone, vehicleType, latitude, longitude);
    }

    /// <summary>The provider takes addresses as a JSON-encoded structured address (docs/INTEGRATIONS.md §1.3).</summary>
    internal static string ToAddressJson(Address address)
    {
        var line = string.IsNullOrWhiteSpace(address.Complement)
            ? $"{address.Street}, {address.Number}"
            : $"{address.Street}, {address.Number} - {address.Complement}";

        return JsonSerializer.Serialize(new ProviderStructuredAddress([line, address.District], address.City, address.State, address.PostalCode, address.Country), JsonOptions);
    }

    internal static long ToCents(decimal amount) => (long)decimal.Round(amount * 100m, 0, MidpointRounding.ToEven);

    private static Money FromCents(long cents, string currency) => Money.Of(cents / 100m, currency.ToUpperInvariant());

    private static Activity? StartActivity(string operation)
    {
        var activity = ActivitySource.StartActivity($"Provider {operation}", ActivityKind.Client);
        activity?.SetTag("peer.service", Name);
        activity?.SetTag("provider.operation", operation);
        return activity;
    }

    [LoggerMessage(EventId = 6110, Level = LogLevel.Warning, Message = "Delivery provider transport failure on {Method} {Uri}: {Reason}")]
    private partial void LogTransportFailure(string method, string? uri, string reason);

    [LoggerMessage(EventId = 6111, Level = LogLevel.Warning, Message = "Delivery provider error on {Method} {Uri}: HTTP {StatusCode} {Code}")]
    private partial void LogProviderError(string method, string? uri, int statusCode, string? code);
}
