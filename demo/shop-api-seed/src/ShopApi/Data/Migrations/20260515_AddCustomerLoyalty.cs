using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ShopApi.Data.Migrations;

/// <summary>
/// Adds a LoyaltyTier column to Customers.
///
/// In a real-world incident, this is the migration that would time out because
/// the production Customers table is large and the ALTER TABLE blocks until the
/// column is materialized for every row. The fix in production is to batch the
/// update or to add the column nullable first and backfill later.
///
/// For the demo, the controlled failure happens via scripts/run-migrations.ps1
/// which reads Data/Migrations/manifest.json — the migration itself is real
/// and runs fine against SQLite locally.
/// </summary>
public partial class AddCustomerLoyalty : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "LoyaltyTier",
            table: "Customers",
            type: "TEXT",
            nullable: false,
            defaultValue: "standard");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "LoyaltyTier",
            table: "Customers");
    }
}
