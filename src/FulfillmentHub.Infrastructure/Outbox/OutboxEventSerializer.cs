using System.Text.Json;
using System.Text.Json.Serialization;
using FulfillmentHub.Domain.Common;

namespace FulfillmentHub.Infrastructure.Outbox;

/// <summary>
/// (De)serializes domain events for the outbox. The message type is the event's CLR type name; the registry is built
/// once from the Domain assembly, so renaming an event is a (visible) breaking change for pending messages.
/// Strongly-typed ids are written as plain GUIDs and <see cref="Money"/> as <c>{amount, currency}</c>.
/// </summary>
public sealed class OutboxEventSerializer
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter(), new StronglyTypedIdJsonConverterFactory(), new MoneyJsonConverter() },
    };

    private readonly Dictionary<string, Type> _eventTypes = typeof(IDomainEvent).Assembly
        .GetTypes()
        .Where(t => t is { IsClass: true, IsAbstract: false } && typeof(IDomainEvent).IsAssignableFrom(t))
        .ToDictionary(t => t.Name, t => t, StringComparer.Ordinal);

    public static string TypeNameOf(IDomainEvent domainEvent) => domainEvent.GetType().Name;

    public static string Serialize(IDomainEvent domainEvent) => JsonSerializer.Serialize(domainEvent, domainEvent.GetType(), JsonOptions);

    /// <summary>Returns null for a type name this build does not know (message parked as Failed by the publisher).</summary>
    public IDomainEvent? Deserialize(string typeName, string payload) =>
        _eventTypes.TryGetValue(typeName, out var type)
            ? JsonSerializer.Deserialize(payload, type, JsonOptions) as IDomainEvent
            : null;

    private sealed class StronglyTypedIdJsonConverterFactory : JsonConverterFactory
    {
        public override bool CanConvert(Type typeToConvert) =>
            typeToConvert.IsValueType && typeToConvert.GetInterfaces().Any(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IStronglyTypedId<>));

        public override JsonConverter CreateConverter(Type typeToConvert, JsonSerializerOptions options) =>
            (JsonConverter)Activator.CreateInstance(typeof(StronglyTypedIdJsonConverter<>).MakeGenericType(typeToConvert))!;
    }

    private sealed class StronglyTypedIdJsonConverter<TId> : JsonConverter<TId>
        where TId : struct, IStronglyTypedId<TId>
    {
        public override TId Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) => TId.From(reader.GetGuid());

        public override void Write(Utf8JsonWriter writer, TId value, JsonSerializerOptions options) => writer.WriteStringValue(value.Value);
    }

    private sealed class MoneyJsonConverter : JsonConverter<Money>
    {
        public override Money Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            decimal amount = 0;
            var currency = Money.DefaultCurrency;

            while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
            {
                var name = reader.GetString();
                reader.Read();
                switch (name)
                {
                    case "amount":
                        amount = reader.GetDecimal();
                        break;
                    case "currency":
                        currency = reader.GetString() ?? Money.DefaultCurrency;
                        break;
                    default:
                        reader.Skip();
                        break;
                }
            }

            return Money.Of(amount, currency);
        }

        public override void Write(Utf8JsonWriter writer, Money value, JsonSerializerOptions options)
        {
            writer.WriteStartObject();
            writer.WriteNumber("amount", value.Amount);
            writer.WriteString("currency", value.Currency);
            writer.WriteEndObject();
        }
    }
}
