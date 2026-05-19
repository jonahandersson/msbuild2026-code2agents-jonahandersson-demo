namespace ShopApi.Models;

/// <summary>
/// Product in the catalog. Owned by the Web storefront prototype
/// (<c>ShopWeb</c>) — the API does not yet expose a Products endpoint.
/// </summary>
public sealed class Product
{
    public int Id { get; set; }
    public required string Sku { get; set; }
    public required string Name { get; set; }
    public required string Tagline { get; set; }
    public required string Description { get; set; }
    public required string Category { get; set; }
    public decimal Price { get; set; }
    public string Currency { get; set; } = "USD";
    public int StockOnHand { get; set; }
    public string? ImageEmoji { get; set; }
        // Lightweight stand-in for a product image — keeps the demo
        // self-contained with zero binary assets.
}
