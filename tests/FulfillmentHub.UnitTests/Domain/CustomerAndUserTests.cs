using FulfillmentHub.Domain.Common;
using FulfillmentHub.Domain.Customers;
using FulfillmentHub.Domain.Identity;

namespace FulfillmentHub.UnitTests.Domain;

public sealed class CustomerAndUserTests
{
    private static readonly DateTimeOffset Now = TestData.Now;

    [Fact]
    public void FirstAddress_BecomesDefault_AndDefaultMovesOnRemoval()
    {
        var customer = TestData.Customer();

        var home = customer.AddAddress("Home", TestData.Address(), Now);
        var work = customer.AddAddress("Work", TestData.Address(), Now);

        home.IsDefault.ShouldBeTrue();
        work.IsDefault.ShouldBeFalse();

        customer.SetDefaultAddress(work.Id, Now);
        customer.DefaultAddress.ShouldBe(work);

        customer.RemoveAddress(work.Id, Now);
        customer.DefaultAddress.ShouldBe(home);
    }

    [Fact]
    public void AddAddress_BeyondLimit_Throws()
    {
        var customer = TestData.Customer();
        for (var i = 0; i < Customer.MaxAddresses; i++)
        {
            customer.AddAddress($"Addr {i}", TestData.Address(), Now);
        }

        Should.Throw<DomainException>(() => customer.AddAddress("One more", TestData.Address(), Now));
    }

    [Fact]
    public void RemoveAddress_FromAnotherCustomer_Throws()
    {
        var customer = TestData.Customer();

        Should.Throw<DomainException>(() => customer.RemoveAddress(Guid.NewGuid(), Now));
    }

    [Fact]
    public void Register_WithBlankName_Throws()
    {
        Should.Throw<DomainException>(() =>
            Customer.Register(UserId.New(), "  ", EmailAddress.Of("a@b.co"), PhoneNumber.Of("+5581999990000"), Now));
    }

    [Fact]
    public void User_MustKeepAtLeastOneRole()
    {
        var user = TestData.User(Role.Customer);

        Should.Throw<DomainException>(() => user.RevokeRole(Role.Customer));
        Should.Throw<DomainException>(() => User.Create(EmailAddress.Of("x@y.co"), "hash", [], Now));

        user.GrantRole(Role.Operator);
        user.GrantRole(Role.Operator);
        user.Roles.ShouldBe([Role.Customer, Role.Operator]);
        user.RevokeRole(Role.Customer);
        user.HasRole(Role.Customer).ShouldBeFalse();
    }
}
