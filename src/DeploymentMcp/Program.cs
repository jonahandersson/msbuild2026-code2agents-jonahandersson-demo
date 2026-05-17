using DeploymentMcp.Services;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

// .NET 10 isolated worker, Functions v4.
// Step 1 of the demo: the simplest possible MCP server on Functions.
// Just three tools backed by an in-memory fake. Nothing fancy.

var builder = FunctionsApplication.CreateBuilder(args);

builder.ConfigureFunctionsWebApplication();

// In-memory fake so the demo runs without any Azure DevOps wiring.
builder.Services.AddSingleton<IDeploymentService, FakeDeploymentService>();

builder.Build().Run();

