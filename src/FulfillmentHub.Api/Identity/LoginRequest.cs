using System.ComponentModel.DataAnnotations;

namespace FulfillmentHub.Api.Identity;

/// <summary>Login payload. Shape is validated by the framework; credentials are checked by the use case.</summary>
public sealed record LoginRequest(
    [property: Required, EmailAddress, MaxLength(254)] string Email,
    [property: Required, MaxLength(256)] string Password);
