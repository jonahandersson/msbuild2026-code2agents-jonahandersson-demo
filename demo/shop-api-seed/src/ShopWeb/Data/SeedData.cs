using ShopApi.Models;

namespace ShopWeb.Data;

/// <summary>
/// Hard-coded catalog for the Build 2026 demo storefront. Generic dev swag,
/// no real-world brands. Twelve items so the grid wraps to two rows on a 1080p
/// projector with the cobalt theme.
/// </summary>
internal static class SeedData
{
    public static IReadOnlyList<Product> Products { get; } = new[]
    {
        new Product
        {
            Id = 1,
            Sku = "DEV-TEE-001",
            Name = "Cobalt Developer Tee",
            Tagline = "Soft cotton. Hard problems.",
            Description = "Classic-fit crew neck in deep cobalt blue. The official "
                + "uniform of someone who just shipped to prod on a Friday.",
            Category = "Apparel",
            Price = 28.00m,
            StockOnHand = 142,
            ImageEmoji = "👕",
        },
        new Product
        {
            Id = 2,
            Sku = "DEV-HOOD-002",
            Name = "Late Night Hoodie",
            Tagline = "For the 02:00 deploy.",
            Description = "Heavyweight zip hoodie. Kangaroo pocket holds a "
                + "YubiKey, a Yubico, and your hopes for the sprint.",
            Category = "Apparel",
            Price = 64.00m,
            StockOnHand = 38,
            ImageEmoji = "🧥",
        },
        new Product
        {
            Id = 3,
            Sku = "DEV-MUG-003",
            Name = "Stack Trace Mug",
            Tagline = "Holds 16 fl oz of regret.",
            Description = "Ceramic mug printed with a stack trace that resolves "
                + "to NullReferenceException at line 42. Dishwasher safe.",
            Category = "Drinkware",
            Price = 14.00m,
            StockOnHand = 220,
            ImageEmoji = "☕",
        },
        new Product
        {
            Id = 4,
            Sku = "DEV-BOTL-004",
            Name = "Insulated Hydration Bottle",
            Tagline = "Stay hydrated. Stay async.",
            Description = "Double-walled stainless steel. Keeps cold drinks cold "
                + "for 24 hours, hot drinks hot for 12, and bugs warm forever.",
            Category = "Drinkware",
            Price = 32.00m,
            StockOnHand = 95,
            ImageEmoji = "🧊",
        },
        new Product
        {
            Id = 5,
            Sku = "DEV-NB-005",
            Name = "Pocket Architect Notebook",
            Tagline = "Whiteboard you can pocket.",
            Description = "A5 dot-grid notebook with a soft-touch cobalt cover. "
                + "192 numbered pages. Lay-flat binding.",
            Category = "Stationery",
            Price = 18.00m,
            StockOnHand = 310,
            ImageEmoji = "📓",
        },
        new Product
        {
            Id = 6,
            Sku = "DEV-PEN-006",
            Name = "Refactor Rollerball",
            Tagline = "Smooth as a clean merge.",
            Description = "Brass-bodied rollerball in matte cobalt finish. "
                + "Ships with a 0.5 mm black refill. Engravable.",
            Category = "Stationery",
            Price = 22.00m,
            StockOnHand = 76,
            ImageEmoji = "🖊️",
        },
        new Product
        {
            Id = 7,
            Sku = "DEV-STK-007",
            Name = "Sticker Pack (12)",
            Tagline = "Decorate the laptop. Decorate the lid.",
            Description = "Twelve vinyl die-cut stickers — open source mascots, "
                + "obscure HTTP codes, and one cat. Dishwasher proof, peer-pressure proof.",
            Category = "Accessories",
            Price = 9.00m,
            StockOnHand = 540,
            ImageEmoji = "🏷️",
        },
        new Product
        {
            Id = 8,
            Sku = "DEV-CAP-008",
            Name = "Build Cap",
            Tagline = "Shade for the LCD years.",
            Description = "Six-panel structured cap in cobalt twill with a tonal "
                + "embroidered curly brace on the front.",
            Category = "Apparel",
            Price = 26.00m,
            StockOnHand = 64,
            ImageEmoji = "🧢",
        },
        new Product
        {
            Id = 9,
            Sku = "DEV-BAG-009",
            Name = "Daily Driver Tote",
            Tagline = "13L. Laptop. Lunch. Lore.",
            Description = "Heavy-canvas tote with a padded 16-inch laptop sleeve "
                + "and a reinforced base. Cobalt strap, natural body.",
            Category = "Accessories",
            Price = 38.00m,
            StockOnHand = 88,
            ImageEmoji = "🎒",
        },
        new Product
        {
            Id = 10,
            Sku = "DEV-PIN-010",
            Name = "Enamel Pin Set",
            Tagline = "Wear your stack.",
            Description = "Three hard-enamel pins: { }, </>, and a tiny semicolon. "
                + "Cobalt and brass. Rubber backs included.",
            Category = "Accessories",
            Price = 12.00m,
            StockOnHand = 410,
            ImageEmoji = "📌",
        },
        new Product
        {
            Id = 11,
            Sku = "DEV-SOCK-011",
            Name = "Deploy Socks (pair)",
            Tagline = "Lucky for Fridays only.",
            Description = "Crew-length combed cotton. Cobalt with white "
                + "polka semicolons. One size, all egos.",
            Category = "Apparel",
            Price = 11.00m,
            StockOnHand = 198,
            ImageEmoji = "🧦",
        },
        new Product
        {
            Id = 12,
            Sku = "DEV-PWR-012",
            Name = "10K Pocket Power Bank",
            Tagline = "One more talk. One more demo.",
            Description = "10 000 mAh USB-C PD power bank. Charges a laptop "
                + "long enough to finish the slide deck you started in the cab.",
            Category = "Tech",
            Price = 45.00m,
            StockOnHand = 52,
            ImageEmoji = "🔋",
        },
    };
}
