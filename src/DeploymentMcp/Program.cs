using Azure.Identity;
using DeploymentMcp.Configuration;
using DeploymentMcp.ServiceDefaults;
using DeploymentMcp.Services;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

// .NET 10 isolated worker, Functions v4.
// This is the whole bootstrap — nothing magical.

var builder = FunctionsApplication.CreateBuilder(args);

builder.ConfigureFunctionsWebApplication();

// --- Telemetry ---
// AddServiceDefaults wires OpenTelemetry tracing + metrics + logging,
// plus Azure Monitor export (when APPLICATIONINSIGHTS_CONNECTION_STRING is
// set) and OTLP export (when running under .NET Aspire).
//
// We also keep the Functions-specific Application Insights worker so the
// host-level telemetry (cold starts, scale events) flows through too.
builder.AddServiceDefaults();
builder.Services
    .AddApplicationInsightsTelemetryWorkerService()
    .ConfigureFunctionsApplicationInsights();

// --- Options ---
// Validated at startup ONLY when DemoMode is false. In demo mode the fake
// service ignores these, so we don't want a missing OrgUrl to crash boot.
var demoMode = builder.Configuration.GetValue<bool>("DemoMode");

var azdoOptions = builder.Services
    .AddOptions<AzureDevOpsOptions>()
    .Bind(builder.Configuration.GetSection(AzureDevOpsOptions.SectionName));

if (!demoMode)
{
    azdoOptions
        .ValidateDataAnnotations()
        .ValidateOnStart();
}

// --- HTTP client for AzureDevOpsClient ---
// Resilience is wired through ServiceDefaults' ConfigureHttpClientDefaults.
// The bearer-token handler is per-request (no shared DefaultRequestHeaders
// state) and caches the AccessToken until it's about to expire.
builder.Services.AddTransient<AzureDevOpsAuthHandler>();
builder.Services.AddHttpClient<AzureDevOpsClient>()
    .AddHttpMessageHandler<AzureDevOpsAuthHandler>();

// --- The deployment service ---
// In DemoMode, swap the real Azure DevOps client for an in-memory fake.
// Same interface — that's the point.
if (demoMode)
{
    builder.Services.AddSingleton<IDeploymentService, FakeDeploymentService>();
}
else
{
    builder.Services.AddSingleton<IDeploymentService>(sp =>
        sp.GetRequiredService<AzureDevOpsClient>());
}

builder.Build().Run();
