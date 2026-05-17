using Microsoft.EntityFrameworkCore;
using ShopApi.Data;
using ShopApi.Models;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDbContext<ShopDbContext>(o =>
    o.UseSqlite(builder.Configuration.GetConnectionString("Shop")
        ?? "Data Source=shop.db"));

builder.Services.AddOpenApi();

var app = builder.Build();

app.MapOpenApi();

app.MapGet("/", () => Results.Ok(new
{
    name = "shop-api",
    status = "ok",
    version = "1.0.0"
}));

app.MapGet("/customers", async (ShopDbContext db) =>
    await db.Customers.AsNoTracking().ToListAsync());

app.MapGet("/orders", async (ShopDbContext db) =>
    await db.Orders.AsNoTracking()
        .Include(o => o.Customer)
        .ToListAsync());

app.MapPost("/orders", async (Order order, ShopDbContext db) =>
{
    db.Orders.Add(order);
    await db.SaveChangesAsync();
    return Results.Created($"/orders/{order.Id}", order);
});

app.Run();
