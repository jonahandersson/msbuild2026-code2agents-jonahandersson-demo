using System.Diagnostics;
using Azure.AI.Projects;
using Azure.AI.Projects.Agents;
using Azure.Identity;
using DeploymentMcp.ServiceDefaults;
using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Foundry;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OpenAI.Responses;

// ---------------------------------------------------------------------------
// DevOps Agent — the MCP *client*.
//
// Connects to Microsoft Foundry for the model and to your Azure Function
// app for the MCP tools. We use Foundry's HOSTED MCP tool: Foundry
// invokes the MCP tools on the server side (no local MCP client wiring).
// The agent decides, on its own, which tools to call and in what order.
//
// Verified against microsoft/agent-framework sample
// dotnet/samples/02-agents/ModelContextProtocol/FoundryAgent_Hosted_MCP.
//
// Pre-warm mode: pass a prompt as args[0] and the agent runs one turn and
// exits — used by the rehearsal checklist to warm the model before stage.
//
// Telemetry: every turn opens an Activity under "DevOpsAgent.Turn" so the
// trace shows up in the Aspire dashboard locally and in App Insights in the
// cloud — end-to-end from the user prompt through every MCP tool call all
// the way to the Azure DevOps REST call.
// ---------------------------------------------------------------------------

var builder = Host.CreateApplicationBuilder(args);

builder.AddServiceDefaults();

var host = builder.Build();
var logger = host.Services
    .GetRequiredService<ILoggerFactory>()
    .CreateLogger("DevOpsAgent");

using var activitySource = new ActivitySource("DevOpsAgent");

var foundryEndpoint = RequireEnv("FOUNDRY_PROJECT_ENDPOINT");
var foundryModel    = Environment.GetEnvironmentVariable("FOUNDRY_MODEL")
                      ?? "gpt-5.4-mini";
var mcpServerUrl    = RequireEnv("MCP_SERVER_URL");

logger.LogInformation(
    "Starting DevOps agent. Foundry={Endpoint} Model={Model} Mcp={Mcp}",
    foundryEndpoint, foundryModel, mcpServerUrl);

// --- 1. Connect to Foundry ---
var aiProjectClient = new AIProjectClient(
    new Uri(foundryEndpoint),
    new DefaultAzureCredential());

// --- 2. Define the hosted MCP tool that points at our Function app ---
// Foundry will fetch the tool list from our /runtime/webhooks/mcp endpoint
// and invoke them as needed.
//
// Approval policy:
//   - Demo (default):  NeverRequireApproval — keeps the 15-min flow tight.
//   - Production:      AlwaysRequireApproval — `create_rollback_pr` is
//     destructive (writes to AzDO). Set AGENT_APPROVAL_MODE=prod to opt in.
var approvalMode = Environment.GetEnvironmentVariable("AGENT_APPROVAL_MODE")
                   ?? "demo";
var approvalPolicy = approvalMode.Equals("prod", StringComparison.OrdinalIgnoreCase)
    ? GlobalMcpToolCallApprovalPolicy.AlwaysRequireApproval
    : GlobalMcpToolCallApprovalPolicy.NeverRequireApproval;

logger.LogInformation(
    "MCP tool approval policy: {Policy} (AGENT_APPROVAL_MODE={Mode})",
    approvalPolicy, approvalMode);

var deploymentMcpTool = ResponseTool.CreateMcpTool(
    serverLabel: "deployment_ops",
    serverUri: new Uri(mcpServerUrl),
    toolCallApprovalPolicy: new McpToolCallApprovalPolicy(approvalPolicy));

// --- 3. Create the agent on the Foundry side ---
ProjectsAgentVersion agentVersion;
AIAgent agent;
try
{
    agentVersion =
        await aiProjectClient.AgentAdministrationClient.CreateAgentVersionAsync(
            agentName: "DevOpsRollbackAgent",
            options: new ProjectsAgentVersionCreationOptions(
                new DeclarativeAgentDefinition(model: foundryModel)
                {
                    Instructions = """
                        You are a senior Site Reliability Engineer.
                        When a deployment fails, you:
                          1. Call get_recent_deployments to find the failing deployment.
                          2. Call diagnose_deployment on the most recent failure.
                          3. If rollback is recommended, call create_rollback_pr
                             targeting the last_known_good_commit_sha from the diagnosis.
                          4. Summarize what you did, including the PR URL.

                        Rules:
                          - Never roll back without diagnosing first.
                          - Always include a clear, human-readable reason in the PR.
                          - If the diagnosis says rollback is NOT recommended, explain
                            what you'd do instead and stop.
                        Be concise. Avoid filler.
                        """,
                    Tools = { deploymentMcpTool }
                }));

    agent = aiProjectClient.AsAIAgent(agentVersion);
}
catch (Exception ex)
{
    logger.LogError(ex,
        "Failed to create Foundry agent (model={Model}, endpoint={Endpoint}). " +
        "Verify FOUNDRY_PROJECT_ENDPOINT and FOUNDRY_MODEL, that you're logged in " +
        "(`az login`), and that the model deployment exists.",
        foundryModel, foundryEndpoint);
    Console.Error.WriteLine();
    Console.Error.WriteLine($"Could not start agent: {ex.Message}");
    Console.Error.WriteLine(
        "Check: model deployment name, Foundry endpoint, az login status, " +
        "and that MCP_SERVER_URL is reachable.");
    Environment.ExitCode = 1;
    return;
}

try
{
    // --- 4a. Single-turn pre-warm mode (rehearsal checklist) ---
    if (args.Length > 0)
    {
        var prompt = string.Join(' ', args);
        logger.LogInformation("Pre-warm prompt: {Prompt}", prompt);
        var response = await RunTurnAsync(agent, prompt, activitySource, logger);
        Console.WriteLine(response);
        return;
    }

    // --- 4b. Interactive REPL ---
    Console.WriteLine("""

        Agent ready. Try:
          "The latest deployment of shop-api to main is failing.
           Investigate and roll back if needed."

        Type 'exit' to quit.

        """);

    while (true)
    {
        Console.Write("> ");
        var prompt = Console.ReadLine();
        if (string.IsNullOrWhiteSpace(prompt)) continue;
        if (prompt.Equals("exit", StringComparison.OrdinalIgnoreCase)) break;

        try
        {
            var response = await RunTurnAsync(agent, prompt, activitySource, logger);
            Console.WriteLine();
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine($"🤖 {response}");
            Console.ResetColor();
            Console.WriteLine();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Agent turn failed");
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"Agent error: {ex.Message}");
            Console.ResetColor();
        }
    }
}
finally
{
    // Clean up the Foundry-side agent so the demo can be re-run cleanly.
    try
    {
        aiProjectClient.AgentAdministrationClient.DeleteAgent(agent.Name);
    }
    catch (Exception ex)
    {
        logger.LogWarning(ex, "Failed to delete agent {Name}", agent.Name);
    }
}

return;

// ---------------------------------------------------------------------------
static async Task<string> RunTurnAsync(
    AIAgent agent,
    string prompt,
    ActivitySource activitySource,
    ILogger logger)
{
    using var turnActivity = activitySource.StartActivity(
        "Agent.Turn",
        ActivityKind.Client);
    turnActivity?.SetTag("agent.prompt_length", prompt.Length);

    var session = await agent.CreateSessionAsync();
    var response = await agent.RunAsync(prompt, session);

    var text = response.Text ?? string.Empty;
    turnActivity?.SetTag("agent.response_length", text.Length);
    turnActivity?.SetStatus(ActivityStatusCode.Ok);
    logger.LogInformation("Turn complete ({Bytes} bytes)", text.Length);
    return text;
}

static string RequireEnv(string name) =>
    Environment.GetEnvironmentVariable(name)
        ?? throw new InvalidOperationException(
            $"Environment variable {name} is required.");
