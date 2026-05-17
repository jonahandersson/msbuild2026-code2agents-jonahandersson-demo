using Azure.Monitor.OpenTelemetry.AspNetCore;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OpenTelemetry;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;

namespace DeploymentMcp.ServiceDefaults;

/// <summary>
/// Shared host setup for every service in the demo.
///
/// Call <c>builder.AddServiceDefaults()</c> from any .NET host (the Function
/// app, the agent console app, any future service) and you get:
///   - OpenTelemetry tracing + metrics + logging
///   - Export to Azure Monitor (App Insights) when the connection string is set
///   - Export to the .NET Aspire dashboard via OTLP when running locally
///   - Service discovery + standard resilience for HttpClient
///
/// This is the Aspire-recommended pattern: the AppHost project wires the
/// dashboard URLs, individual services don't care whether they're running
/// under Aspire, in Azure, or standalone — same code, three contexts.
/// </summary>
public static class Extensions
{
    public static IHostApplicationBuilder AddServiceDefaults(
        this IHostApplicationBuilder builder)
    {
        builder.ConfigureOpenTelemetry();
        builder.AddDefaultHealthChecks();

        builder.Services.AddServiceDiscovery();

        builder.Services.ConfigureHttpClientDefaults(http =>
        {
            // Polly-style resilience for every HttpClient — retries with
            // exponential backoff and jitter, circuit breaker, timeout.
            http.AddStandardResilienceHandler();

            // Service discovery so URLs like "https+http://mcp-server" resolve
            // automatically when running under Aspire.
            http.AddServiceDiscovery();
        });

        return builder;
    }

    public static IHostApplicationBuilder ConfigureOpenTelemetry(
        this IHostApplicationBuilder builder)
    {
        // Structured logs with scope + format included.
        builder.Logging.AddOpenTelemetry(logging =>
        {
            logging.IncludeFormattedMessage = true;
            logging.IncludeScopes = true;
        });

        // Metrics + tracing. We keep instrumentation minimal — runtime
        // counters, HTTP client + server, and our own ActivitySources.
        builder.Services.AddOpenTelemetry()
            .WithMetrics(metrics =>
            {
                metrics
                    .AddAspNetCoreInstrumentation()
                    .AddHttpClientInstrumentation()
                    .AddRuntimeInstrumentation();
            })
            .WithTracing(tracing =>
            {
                tracing
                    .AddAspNetCoreInstrumentation()
                    .AddHttpClientInstrumentation()
                    // Trace anything that uses these source names. The MCP
                    // extension emits its own spans under these.
                    .AddSource("Microsoft.Azure.Functions.Worker")
                    .AddSource("DeploymentMcp.*")
                    .AddSource("DevOpsAgent.*");
            });

        builder.AddOpenTelemetryExporters();

        return builder;
    }

    private static IHostApplicationBuilder AddOpenTelemetryExporters(
        this IHostApplicationBuilder builder)
    {
        // OTLP exporter — used by the Aspire dashboard locally.
        var useOtlp = !string.IsNullOrWhiteSpace(
            builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"]);

        if (useOtlp)
        {
            builder.Services.AddOpenTelemetry().UseOtlpExporter();
        }

        // Azure Monitor exporter — ships traces, logs, and metrics to
        // App Insights. The same telemetry the speaker shows on stage.
        var appInsights = builder.Configuration[
            "APPLICATIONINSIGHTS_CONNECTION_STRING"];

        if (!string.IsNullOrWhiteSpace(appInsights))
        {
            builder.Services.AddOpenTelemetry()
                .UseAzureMonitor(o => o.ConnectionString = appInsights);
        }

        return builder;
    }

    public static IHostApplicationBuilder AddDefaultHealthChecks(
        this IHostApplicationBuilder builder)
    {
        builder.Services.AddHealthChecks()
            .AddCheck("self", () => Microsoft.Extensions.Diagnostics
                .HealthChecks.HealthCheckResult.Healthy(),
                ["live"]);

        return builder;
    }

    public static WebApplication MapDefaultEndpoints(this WebApplication app)
    {
        // Health endpoints — Aspire dashboard probes these.
        if (app.Environment.IsDevelopment())
        {
            app.MapHealthChecks("/health");
            app.MapHealthChecks("/alive", new()
            {
                Predicate = r => r.Tags.Contains("live"),
            });
        }

        return app;
    }
}
