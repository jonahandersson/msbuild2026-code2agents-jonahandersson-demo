using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Builder;
using Microsoft.Extensions.Hosting;

// .NET 10 isolated worker, Functions v4.
// Step 0: empty scaffold. No tools, no services, no MCP extension yet.
// The next branch (step-1-mcp-tool) adds three MCP tools.

var builder = FunctionsApplication.CreateBuilder(args);

builder.ConfigureFunctionsWebApplication();

builder.Build().Run();

