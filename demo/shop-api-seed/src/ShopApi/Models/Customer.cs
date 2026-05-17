namespace ShopApi.Models;

public sealed class Customer
{
    public int Id { get; set; }
    public required string Name { get; set; }
    public required string Email { get; set; }

    // Added in the breaking migration — see 20260515_AddCustomerLoyalty.cs
    public string LoyaltyTier { get; set; } = "standard";
}
