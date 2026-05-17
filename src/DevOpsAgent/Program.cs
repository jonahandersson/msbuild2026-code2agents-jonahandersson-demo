using System.Diagnostics;
using Azure.AI.Projects;
using Azure.Identity;
using DeploymentMcp.ServiceDefaults;
using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Mcp;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

// ---------------------------------------------------------------------------
// DevOps Agent — the MCP *client*.
//
// It connects to Microsoft Foundry for the model, and to your Azure Function
// app for the MCP tools. The agent decides, on its own, which tools to call
// and in what order. Your job is just to wire it up.
//
// Telemetry: every turn opens an Activity under "DevOpsAgent.RunAsync" so
// the trace shows up in the Aspire dashboard locally and in App Insights
// in the cloud — end-to-end from the user prompt through every MCP tool
// call all the way to the Azure DevOps REST call.
// ---------------------------------------------------------------------------

var builder = Host.CreateApplicationBuilder(args);

builder.AddServiceDefaults();

var host = builder.Build();
var logger = host.Services
    .GetRequiredService<ILoggerFactory>()
    .CreateLogger("DevOpsAgent");

// Single ActivitySource for the agent — every turn becomes a span.
using var activitySource = new ActivitySource("DevOpsAgent");

var foundryEndpoint = RequireEnv("FOUNDRY_PROJECT_ENDPOINT");
var foundryModel    = Environment.GetEnvironmentVariable("FOUNDRY_MODEL")
                      ?? "gpt-4o-mini";
var mcpServerUrl    = RequireEnv("MCP_SERVER_URL");

logger.LogInformation(
    "Starting DevOps agent. Foundry={Endpoint} Model={Model} Mcp={Mcp}",
    foundryEndpoint, foundryModel, mcpServerUrl);

// --- 1. Connect to Foundry for the model ---
var foundry = new AIProjectClient(
    new Uri(foundryEndpoint),
    new DefaultAzureCredential());

// --- 2. Wire up the MCP client tool pointed at our Function app ---
var deploymentTools = new McpClientTool(
    name: "deployment_ops",
    serverUrl: new Uri(mcpServerUrl),
    credential: new DefaultAzureCredential());

// --- 3. Build the agent ---
AIAgent agent = foundry.AsAIAgent(
    model: foundryModel,
    name: "DevOpsRollbackAgent",
    instructions: """
        You are a senior Site Reliability Engineer.
        When a deployment fails, you:
          1. Call get_recent_deployments to find the failing deployment.
          2. Call diagnose_deployment on the most recent failure.
          3. If rollback is recommended, call create_rollback_pr targeting
             the last_known_good_commit_sha from the diagnosis.
          4. Summarize what you did, including the PR URL.

        Rules:
          - Never roll back without diagnosing first.
          - Always include a clear, human-readable reason in the PR.
          - If the diagnosis says rollback is NOT recommended, explain
            what you'd do instead and stop.
        Be concise. Avoid filler.
        """,
    tools: [deploymentTools]);

Console.WriteLine("""

    Agent ready. Try:
      "The latest deployment of shop-api to main is failing.
       Investigate and roll back if needed."

    Type 'exit' to quit.

    """);

// --- 4. Conversation loop ---
while (true)
{
    Console.Write("> ");
    var prompt = Console.ReadLine();

    if (string.IsNullOrWhiteSpace(prompt))    continue;
    if (prompt.Equals("exit", StringComparison.OrdinalIgnoreCase)) break;

    // Open a span for this turn — App Insights and Aspire both pick it up.
    using var turnActivity = activitySource.StartActivity(
        "Agent.Turn",
        ActivityKind.Client);
    turnActivity?.SetTag("agent.prompt_length", prompt.Length);

    try
    {
        var response = await agent.RunAsync(prompt);

        turnActivity?.SetTag("agent.response_length", response.Text.Length);
        turnActivity?.SetStatus(ActivityStatusCode.Ok);

        Console.WriteLine();
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine($"🤖 {response.Text}");
        Console.ResetColor();
        Console.WriteLine();
    }
    catch (Exception ex)
    {
        turnActivity?.SetStatus(ActivityStatusCode.Error, ex.Message);
        logger.LogError(ex, "Agent turn failed");

        Console.ForegroundColor = ConsoleColor.Red;
        Console.WriteLine($"Agent error: {ex.Message}");
        Console.ResetColor();
    }
}

return;

// ---------------------------------------------------------------------------
static string RequireEnv(string name) =>
    Environment.GetEnvironmentVariable(name)
        ?? throw new InvalidOperationException(
            $"Environment variable {name} is required.");
