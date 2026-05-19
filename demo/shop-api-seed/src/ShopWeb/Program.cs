using Microsoft.EntityFrameworkCore;
using ShopWeb.Components;
using ShopWeb.Data;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

builder.Services.AddDbContextFactory<ShopWebDbContext>(opts =>
    opts.UseInMemoryDatabase("ShopWeb"));

var app = builder.Build();

// Seed the in-memory catalog on first start.
using (var scope = app.Services.CreateScope())
{
    var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<ShopWebDbContext>>();
    using var db = factory.CreateDbContext();
    if (!db.Products.Any())
    {
        db.Products.AddRange(SeedData.Products);
        db.SaveChanges();
    }
}

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}

app.UseStaticFiles();
app.UseAntiforgery();

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.MapGet("/health", () => Results.Ok(new { status = "healthy" }));

app.Run();
