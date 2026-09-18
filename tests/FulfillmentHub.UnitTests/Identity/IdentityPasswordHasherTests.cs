using FulfillmentHub.Application.Identity;
using FulfillmentHub.Infrastructure.Identity;

namespace FulfillmentHub.UnitTests.Identity;

public sealed class IdentityPasswordHasherTests
{
    private readonly IdentityPasswordHasher _hasher = new();

    [Fact]
    public void Hash_IsSaltedAndVerifiable()
    {
        var first = _hasher.Hash("correct horse battery staple");
        var second = _hasher.Hash("correct horse battery staple");

        first.ShouldNotBe(second, "each hash uses its own salt");
        first.ShouldNotContain("correct horse");
        _hasher.Verify(first, "correct horse battery staple").ShouldBe(PasswordVerification.Success);
        _hasher.Verify(first, "Correct horse battery staple").ShouldBe(PasswordVerification.Failed);
    }

    [Fact]
    public void Verify_WithMalformedStoredHash_FailsInsteadOfThrowing()
    {
        _hasher.Verify("not-a-valid-hash", "anything").ShouldBe(PasswordVerification.Failed);
    }

    [Fact]
    public void DecoyHash_IsAValidHashThatNeverMatches()
    {
        _hasher.DecoyHash.ShouldNotBeNullOrWhiteSpace();
        _hasher.Verify(_hasher.DecoyHash, "anything").ShouldBe(PasswordVerification.Failed);
        _hasher.Verify(_hasher.DecoyHash, string.Empty).ShouldBe(PasswordVerification.Failed);
    }
}
