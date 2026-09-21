using System.Reflection;
using System.Text.RegularExpressions;
using FulfillmentHub.Application.Common.Persistence;
using FulfillmentHub.Infrastructure.Persistence;
using Microsoft.Extensions.Logging;

namespace FulfillmentHub.ArchitectureTests;

/// <summary>
/// Guards docs/SECURITY.md §1.5 / D-85: no <see cref="LoggerMessageAttribute"/> in any host or library carries personal
/// data or secrets. Instead of redacting at the sink, the field never reaches the logger — this test keeps it that way
/// when new log events are added.
/// </summary>
public sealed partial class LogMessagePrivacyTests
{
    private static readonly Assembly[] Assemblies =
    [
        typeof(IFulfillmentHubDbContext).Assembly,        // Application
        typeof(FulfillmentHubDbContext).Assembly,         // Infrastructure
        typeof(FulfillmentHub.Api.Middleware.CorrelationIdMiddleware).Assembly,
        typeof(FulfillmentHub.Worker.WorkerMetrics).Assembly,
        typeof(FulfillmentHub.ProviderSimulator.Common.ChaosOptions).Assembly,
    ];

    // Placeholder/parameter names that would put personal data or a credential into a log line.
    private static readonly string[] ForbiddenFragments =
    [
        "email", "phone", "password", "secret", "token", "signature", "apikey", "signingkey", "connectionstring",
        "authorization", "body", "payload", "customername", "fullname", "street", "cardnumber",
    ];

    // Names that contain a forbidden fragment but are not sensitive.
    private static readonly string[] Allowed =
    [
        "IdempotencyKey", // client-chosen opaque key, required to correlate replays
        "ExpiresIn",      // token lifetime in seconds, not the token
    ];

    [Fact]
    public void LoggerMessages_DoNotCarryPersonalDataOrSecrets()
    {
        var offenders = new List<string>();
        var inspected = 0;

        foreach (var method in LoggerMessageMethods())
        {
            inspected++;
            var attribute = method.GetCustomAttribute<LoggerMessageAttribute>()!;
            var names = method.GetParameters().Select(p => p.Name!)
                .Concat(PlaceholderPattern().Matches(attribute.Message ?? string.Empty).Select(m => m.Groups[1].Value));

            foreach (var name in names.Where(IsForbidden))
            {
                offenders.Add($"{method.DeclaringType!.FullName}.{method.Name} ({attribute.EventId}): '{name}'");
            }
        }

        inspected.ShouldBeGreaterThan(20, "the scan should find the existing LoggerMessage catalog");
        offenders.ShouldBeEmpty(string.Join(Environment.NewLine, offenders));
    }

    private static IEnumerable<MethodInfo> LoggerMessageMethods() =>
        Assemblies
            .SelectMany(static assembly => assembly.GetTypes())
            .SelectMany(static type => type.GetMethods(
                BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
            .Where(static method => method.IsDefined(typeof(LoggerMessageAttribute), inherit: false));

    private static bool IsForbidden(string name) =>
        !Allowed.Contains(name, StringComparer.OrdinalIgnoreCase)
        && ForbiddenFragments.Any(fragment => name.Contains(fragment, StringComparison.OrdinalIgnoreCase));

    [GeneratedRegex(@"\{(\w+)")]
    private static partial Regex PlaceholderPattern();
}
