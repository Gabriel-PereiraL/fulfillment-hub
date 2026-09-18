using FulfillmentHub.Application.Common;

namespace FulfillmentHub.Infrastructure.Telemetry;

/// <summary>
/// Names shared between instrumentation producers and the OpenTelemetry registration.
/// </summary>
public static class TelemetryNames
{
    public const string ActivitySource = ApplicationTelemetry.Name;

    public const string Meter = ApplicationTelemetry.Name;
}
