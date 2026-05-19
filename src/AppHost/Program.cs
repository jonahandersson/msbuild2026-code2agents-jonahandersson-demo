// .NET Aspire AppHost — one command brings the whole local environment up.
//
//   dotnet run --project src/AppHost
//
// Two modes:
//   1) LOCAL mode (default): spawns the MCP server via `func start` on
//      http://localhost:7071 and points the agent at it.
//   2) REMOTE mode: when the MCP_SERVER_URL environment variable is set
//      (typically to a deployed Function App's MCP endpoint), AppHost
//      skips `func start` and wires the agent directly to that URL.
//      Use this when local `func start` is broken (e.g. the upstream
//      Core Tools v4.10 + .NET 10 MCP loader incompatibility) or when
//      you simply want to demo against the cloud-deployed server.
//
// Pick REMOTE mode quickly with:
//   $env:MCP_SERVER_URL = (azd env get-value MCP_ENDPOINT); dotnet run --project src/AppHost
//
// What you get either way:
//   - Aspire dashboard at https://localhost:17081 with traces, logs, metrics
//   - The DevOps agent ready to talk to whichever MCP server is wired up
//   - Environment variables wired automatically — no hardcoded URLs in the agent

var builder = DistributedApplication.CreateBuilder(args);

// --- Parameters the user supplies via user-secrets or env ---
var foundryEndpoint = builder.AddParameter(
    "foundry-endpoint", secret: true);

var foundryModel = builder.AddParameter(
    "foundry-model", value: "gpt-4o-mini");

// --- Decide: local func start vs deployed MCP ---
var remoteMcpUrl = Environment.GetEnvironmentVariable("MCP_SERVER_URL");
var useRemoteMcp = !string.IsNullOrWhiteSpace(remoteMcpUrl);

var agent = builder.AddProject<Projects.DevOpsAgent>("devops-agent")
    .WithEnvironment("FOUNDRY_PROJECT_ENDPOINT", foundryEndpoint)
    .WithEnvironment("FOUNDRY_MODEL", foundryModel);

if (useRemoteMcp)
{
    // REMOTE mode — point the agent at the deployed Function App.
    agent.WithEnvironment("MCP_SERVER_URL", remoteMcpUrl!);
}
else
{
    // LOCAL mode — spawn the Function App via `func start`. Aspire treats it
    // as an external executable since Aspire 9 has no first-class Functions
    // resource. Logs and stdout stream into the Aspire dashboard.
    var mcpServer = builder.AddExecutable(
            name: "mcp-server",
            command: "func",
            workingDirectory: "../DeploymentMcp",
            args: ["start", "--csharp", "--port", "7071"])
        .WithHttpEndpoint(port: 7071, name: "http")
        .WithEnvironment("DemoMode", "true");

    agent.WithReference(mcpServer.GetEndpoint("http"))
         .WithEnvironment("MCP_SERVER_URL",
             "http://localhost:7071/runtime/webhooks/mcp")
         .WaitFor(mcpServer);
}

builder.Build().Run();
