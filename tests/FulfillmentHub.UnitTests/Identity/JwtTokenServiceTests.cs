using FulfillmentHub.Domain.Common;
using FulfillmentHub.Domain.Customers;
using FulfillmentHub.Domain.Identity;
using FulfillmentHub.Infrastructure.Identity;
using Microsoft.Extensions.Time.Testing;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace FulfillmentHub.UnitTests.Identity;

public sealed class JwtTokenServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 18, 12, 0, 0, TimeSpan.Zero);

    private static readonly JwtOptions Options = new()
    {
        Issuer = "fh-tests",
        Audience = "fh-api-tests",
        SigningKey = "unit-tests-signing-key-with-at-least-32-bytes!",
        AccessTokenLifetimeMinutes = 15,
    };

    [Fact]
    public async Task Issue_ProducesTokenWithMinimalClaims_ThatValidatesWithTheSameKey()
    {
        var user = User.Create(EmailAddress.Of("ana@example.com"), "hash", [Role.Customer, Role.Operator], Now);
        var customerId = CustomerId.New();
        var service = new JwtTokenService(Microsoft.Extensions.Options.Options.Create(Options), new FakeTimeProvider(Now));

        var token = service.Issue(user, customerId);

        token.ExpiresAt.ShouldBe(Now.AddMinutes(15));

        var result = await new JsonWebTokenHandler().ValidateTokenAsync(token.Token, new TokenValidationParameters
        {
            ValidIssuer = Options.Issuer,
            ValidAudience = Options.Audience,
            IssuerSigningKey = JwtTokenService.CreateSigningKey(Options),
            ValidateLifetime = false, // the fixed clock is in the past relative to the real one; lifetime is asserted below
        });

        result.IsValid.ShouldBeTrue(result.Exception?.Message);
        var jwt = (JsonWebToken)result.SecurityToken;
        jwt.Subject.ShouldBe(user.Id.Value.ToString());
        jwt.Claims.Where(c => c.Type == JwtClaimNames.Role).Select(c => c.Value).ShouldBe(["Customer", "Operator"]);
        jwt.GetClaim(JwtClaimNames.CustomerId).Value.ShouldBe(customerId.Value.ToString());
        jwt.ValidTo.ShouldBe(Now.AddMinutes(15).UtcDateTime);
        jwt.Claims.Select(c => c.Type).ShouldNotContain("email");
        jwt.Claims.Select(c => c.Type).ShouldNotContain("name");
    }

    [Fact]
    public void Issue_OmitsCustomerClaim_ForNonCustomers()
    {
        var user = User.Create(EmailAddress.Of("ops@example.com"), "hash", [Role.Operator], Now);
        var service = new JwtTokenService(Microsoft.Extensions.Options.Options.Create(Options), new FakeTimeProvider(Now));

        var token = new JsonWebTokenHandler().ReadJsonWebToken(service.Issue(user, null).Token);

        token.TryGetClaim(JwtClaimNames.CustomerId, out _).ShouldBeFalse();
    }
}
