namespace FulfillmentHub.Infrastructure.Identity;

/// <summary>Claim names used in FulfillmentHub tokens (short JWT names; inbound mapping is disabled on validation).</summary>
public static class JwtClaimNames
{
    public const string Subject = "sub";

    public const string Role = "role";

    public const string CustomerId = "customer_id";
}
