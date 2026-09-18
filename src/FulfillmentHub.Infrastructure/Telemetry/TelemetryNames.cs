namespace FulfillmentHub.Infrastructure.Telemetry;

/// <summary>
/// Names shared between instrumentation producers and the OpenTelemetry registration.
/// </summary>
public static class TelemetryNames
{
    public const string ActivitySource = "FulfillmentHub";

    public const string Meter = "FulfillmentHub";
}
