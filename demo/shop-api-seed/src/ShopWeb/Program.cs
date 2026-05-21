using Microsoft.EntityFrameworkCore;
using ShopWeb.Components;
using ShopWeb.Data;

// NOTE: This is the seed storefront for the demo narrative ("the site customers
// see"). It is an independent solution from src/ — it does NOT reference the
// Aspire ServiceDefaults shared project on purpose, so the demo seed stays
// self-contained and easy to clone into a fresh AzDO repo.
//
// For a production frontend you would add the standard Aspire wiring:
//   builder.AddServiceDefaults();
// (Polly retries + circuit breaker + OpenTelemetry + health endpoints.)

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
