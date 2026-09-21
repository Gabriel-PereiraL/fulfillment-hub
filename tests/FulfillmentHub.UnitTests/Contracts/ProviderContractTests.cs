using System.ComponentModel.DataAnnotations;
using System.Reflection;
using System.Text.Json;
using FulfillmentHub.Infrastructure.Providers.Deliveries;
using FulfillmentHub.Infrastructure.Providers.Payments;
using FulfillmentHub.ProviderSimulator.Deliveries;
using FulfillmentHub.ProviderSimulator.Payments;

namespace FulfillmentHub.UnitTests.Contracts;

/// <summary>
/// Contract tests (BL-068, docs/TEST_STRATEGY.md T21): the wire records of the provider clients and of the simulator
/// must agree, field by field, on the snake_case JSON they exchange. For every field the <em>consumer</em> reads, the
/// <em>producer</em> must have a field with the same wire name and a compatible type; a field the consumer requires
/// (non-nullable, or <c>[Required]</c> on the simulator's request models) must not be nullable on the producer.
/// Extra producer fields are fine (a real provider adds fields; System.Text.Json ignores them), so drift is caught
/// exactly where it would break the integration.
/// </summary>
public sealed class ProviderContractTests
{
    private static readonly NullabilityInfoContext Nullability = new();

    public static TheoryData<string, Type, Type> ResponsesReadByTheClient => new()
    {
        // consumer = client record, producer = simulator record
        { "payment", typeof(ProviderPaymentResponse), typeof(PaymentResponse) },
        { "refund", typeof(ProviderRefundResponse), typeof(RefundResponse) },
        { "payment webhook", NestedClientType(typeof(PaymentWebhookProcessor), "PaymentWebhookPayload"), typeof(PaymentWebhookEvent) },
        { "delivery token", typeof(ProviderTokenResponse), typeof(TokenResponse) },
        { "delivery quote", typeof(ProviderQuoteResponse), typeof(DeliveryQuoteResponse) },
        { "delivery", typeof(ProviderDeliveryResponse), typeof(DeliveryResponse) },
        { "delivery webhook", NestedClientType(typeof(DeliveryWebhookProcessor), "DeliveryWebhookPayload"), typeof(DeliveryStatusEvent) },
    };

    public static TheoryData<string, Type, Type> RequestsReadByTheSimulator => new()
    {
        // consumer = simulator record (its [Required] fields are the contract), producer = client record
        { "create payment", typeof(CreatePaymentRequest), typeof(ProviderCreatePaymentRequest) },
        { "refund", typeof(RefundRequest), typeof(ProviderRefundRequest) },
        { "delivery quote", typeof(DeliveryQuoteRequest), typeof(ProviderQuoteRequest) },
        { "create delivery", typeof(CreateDeliveryRequest), typeof(ProviderCreateDeliveryRequest) },
    };

    [Theory]
    [MemberData(nameof(ResponsesReadByTheClient))]
    public void SimulatorResponses_SatisfyTheClientContracts(string contract, Type consumer, Type producer)
    {
        var violations = Compare(consumer, producer, path: contract);

        violations.ShouldBeEmpty(string.Join(Environment.NewLine, violations));
    }

    [Theory]
    [MemberData(nameof(RequestsReadByTheSimulator))]
    public void ClientRequests_SatisfyTheSimulatorContracts(string contract, Type consumer, Type producer)
    {
        var violations = Compare(consumer, producer, path: contract);

        violations.ShouldBeEmpty(string.Join(Environment.NewLine, violations));

        // Every field the client sends must mean something to the simulator, otherwise it is silently dropped.
        var unknown = Properties(producer).Select(WireName).Except(Properties(consumer).Select(WireName)).ToList();
        unknown.ShouldBeEmpty($"{contract}: the client sends fields the simulator does not know: {string.Join(", ", unknown)}");
    }

    // Self-check: the comparer must see a renamed field, a nullable-vs-required field, a type change and a nested drift.
    private sealed record ConsumerSample(string Id, long Amount, string Status, ConsumerNested Nested, string? Note);
    private sealed record ConsumerNested(string Code);
    private sealed record ProducerSample(string Id, string Amount, string? Status, ProducerNested Nested, string Extra);
    private sealed record ProducerNested(string Kode);

    [Fact]
    public void Comparer_DetectsRenamedNullableTypedAndNestedDrift()
    {
        var violations = Compare(typeof(ConsumerSample), typeof(ProducerSample), "sample");

        violations.ShouldBe(
            [
                "sample.amount: consumer expects Int64, producer sends String",
                "sample.status: required by the consumer but the producer may send null",
                "sample.nested.code: required by the consumer but not produced",
            ],
            ignoreOrder: true);
    }

    private static List<string> Compare(Type consumer, Type producer, string path)
    {
        var violations = new List<string>();
        var produced = Properties(producer).ToDictionary(WireName, p => p);

        foreach (var wanted in Properties(consumer))
        {
            var name = WireName(wanted);
            var field = $"{path}.{name}";

            if (!produced.TryGetValue(name, out var offered))
            {
                if (IsRequired(wanted))
                {
                    violations.Add($"{field}: required by the consumer but not produced");
                }

                continue;
            }

            if (IsRequired(wanted) && IsNullable(offered))
            {
                violations.Add($"{field}: required by the consumer but the producer may send null");
            }

            violations.AddRange(CompareTypes(Unwrap(wanted.PropertyType), Unwrap(offered.PropertyType), field));
        }

        return violations;
    }

    private static IEnumerable<string> CompareTypes(Type wanted, Type offered, string field)
    {
        if (wanted == offered)
        {
            return [];
        }

        if (wanted == typeof(string) && offered.IsEnum)
        {
            return []; // the simulator serializes enums as snake_case strings
        }

        if (wanted.IsArray && offered.IsArray)
        {
            return CompareTypes(Unwrap(wanted.GetElementType()!), Unwrap(offered.GetElementType()!), field + "[]");
        }

        if (IsRecordLike(wanted) && IsRecordLike(offered))
        {
            return Compare(wanted, offered, field);
        }

        if (IsNumeric(wanted) && IsNumeric(offered))
        {
            return [];
        }

        return [$"{field}: consumer expects {wanted.Name}, producer sends {offered.Name}"];
    }

    private static IEnumerable<PropertyInfo> Properties(Type type) =>
        type.GetProperties(BindingFlags.Instance | BindingFlags.Public).Where(p => p.CanRead && p.GetIndexParameters().Length == 0);

    private static string WireName(PropertyInfo property) => JsonNamingPolicy.SnakeCaseLower.ConvertName(property.Name);

    private static bool IsRequired(PropertyInfo property)
    {
        if (property.GetCustomAttribute<RequiredAttribute>() is not null)
        {
            return true;
        }

        return !IsNullable(property) && !HasDefaultValue(property);
    }

    private static bool IsNullable(PropertyInfo property) =>
        Nullable.GetUnderlyingType(property.PropertyType) is not null
        || Nullability.Create(property).ReadState == NullabilityState.Nullable;

    /// <summary>A primary-constructor parameter with a default value (e.g. <c>bool Capture = true</c>) is optional on the wire.</summary>
    private static bool HasDefaultValue(PropertyInfo property) =>
        property.DeclaringType!.GetConstructors()
            .SelectMany(c => c.GetParameters())
            .Any(p => string.Equals(p.Name, property.Name, StringComparison.OrdinalIgnoreCase) && p.HasDefaultValue);

    private static Type Unwrap(Type type) => Nullable.GetUnderlyingType(type) ?? type;

    private static bool IsRecordLike(Type type) => type.IsClass && type != typeof(string) && !type.IsArray && type.Namespace?.StartsWith("FulfillmentHub", StringComparison.Ordinal) == true;

    private static bool IsNumeric(Type type) => type == typeof(int) || type == typeof(long) || type == typeof(double) || type == typeof(decimal);

    private static Type NestedClientType(Type owner, string name) =>
        owner.GetNestedType(name, BindingFlags.NonPublic) ?? throw new InvalidOperationException($"{owner.Name} has no nested record {name}");
}
