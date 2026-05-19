using Microsoft.EntityFrameworkCore;
using ShopApi.Models;

namespace ShopWeb.Data;

public sealed class ShopWebDbContext(DbContextOptions<ShopWebDbContext> options)
    : DbContext(options)
{
    public DbSet<Product> Products => Set<Product>();
}
