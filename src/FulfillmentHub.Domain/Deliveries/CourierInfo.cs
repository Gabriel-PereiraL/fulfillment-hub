using FulfillmentHub.Domain.Common;

namespace FulfillmentHub.Domain.Deliveries;

/// <summary>Courier details reported by the provider. Phone is stored masked: it is never needed in full.</summary>
public sealed record CourierInfo
{
    public string Name { get; }

    public string? PhoneMasked { get; }

    public string? VehicleType { get; }

    public double? Latitude { get; }

    public double? Longitude { get; }

    private CourierInfo(string name, string? phoneMasked, string? vehicleType, double? latitude, double? longitude)
    {
        Name = name;
        PhoneMasked = phoneMasked;
        VehicleType = vehicleType;
        Latitude = latitude;
        Longitude = longitude;
    }

    public static CourierInfo Create(string name, PhoneNumber? phone, string? vehicleType, double? latitude, double? longitude)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new DomainException("Courier name is required.");
        }

        return new CourierInfo(name.Trim(), phone?.Masked, vehicleType?.Trim(), latitude, longitude);
    }
}
