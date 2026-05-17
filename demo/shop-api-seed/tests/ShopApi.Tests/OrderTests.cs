using FluentAssertions;
using ShopApi.Models;

namespace ShopApi.Tests;

public class OrderTests
{
    [Fact]
    public void New_order_defaults_to_pending_status()
    {
        var order = new Order { CustomerId = 1, Total = 99.99m };

        order.Status.Should().Be("pending");
    }

    [Fact]
    public void New_order_has_recent_created_timestamp()
    {
        var before = DateTimeOffset.UtcNow.AddSeconds(-1);

        var order = new Order { CustomerId = 1, Total = 50m };

        order.CreatedAt.Should().BeAfter(before);
    }

    [Fact]
    public void Customer_defaults_to_standard_loyalty_tier()
    {
        var customer = new Customer { Name = "Test", Email = "t@example.com" };

        customer.LoyaltyTier.Should().Be("standard");
    }
}
