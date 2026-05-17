// .NET Aspire AppHost — one command brings the whole local environment up.
//
//   dotnet run --project src/AppHost
//
// What you get:
//   - The MCP server (Azure Function) running on http://localhost:7071
//     started via `func start`. It's added as an external executable here
//     because Aspire 9 doesn't have first-class Function support yet.
//   - The DevOps agent (console app) ready to talk to it.
//   - The Aspire dashboard at https://localhost:17081 showing every trace,
//     log, and metric flowing through the system in real time.
//   - Environment variables wired automatically — the agent finds the
//     MCP server through service discovery, no hardcoded URLs.

var builder = DistributedApplication.CreateBuilder(args);

// --- Parameters the user supplies via user-secrets or env ---
var foundryEndpoint = builder.AddParameter(
    "foundry-endpoint", secret: true);

var foundryModel = builder.AddParameter(
    "foundry-model", value: "gpt-4o-mini");

// --- The MCP server: Azure Function started via `func start` ---
// Aspire treats this as an external executable. Logs and stdout stream
// into the Aspire dashboard the same as any other resource.
var mcpServer = builder.AddExecutable(
        name: "mcp-server",
        command: "func",
        workingDirectory: "../DeploymentMcp",
        args: ["start", "--csharp", "--port", "7071"])
    .WithHttpEndpoint(port: 7071, name: "http")
    .WithEnvironment("DemoMode", "true");

// --- The DevOps Agent: regular .NET project ---
// Aspire's service discovery rewrites these references at runtime so
// the agent can connect via "https+http://mcp-server" if you prefer
// names over ports.
builder.AddProject<Projects.DevOpsAgent>("devops-agent")
    .WithReference(mcpServer.GetEndpoint("http"))
    .WithEnvironment("MCP_SERVER_URL",
        "http://localhost:7071/runtime/webhooks/mcp")
    .WithEnvironment("FOUNDRY_PROJECT_ENDPOINT", foundryEndpoint)
    .WithEnvironment("FOUNDRY_MODEL", foundryModel)
    .WaitFor(mcpServer);

builder.Build().Run();
