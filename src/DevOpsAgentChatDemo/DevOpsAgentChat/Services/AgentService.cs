using System.Diagnostics;
using Azure.AI.Projects;
using Azure.AI.Projects.Agents;
using Azure.Identity;
using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Foundry;
using OpenAI.Responses;

namespace DevOpsAgentChat.Services;

public sealed class AgentService : IAsyncDisposable
{
    private static readonly ActivitySource Source = new("DevOpsAgentChat");

    private readonly ILogger<AgentService> _logger;
    private readonly IConfiguration _config;
    private readonly SemaphoreSlim _init = new(1, 1);

    private AIProjectClient? _projectClient;
    private ProjectsAgentVersion? _agentVersion;
    private AIAgent? _agent;

    public AgentService(ILogger<AgentService> logger, IConfiguration config)
    {
        _logger = logger;
        _config = config;
    }

    public async Task<string> RunAsync(string prompt)
    {
        var agent = await EnsureAgentAsync();

        using var act = Source.StartActivity("Agent.Turn", ActivityKind.Client);
        act?.SetTag("agent.prompt_length", prompt.Length);

        var session = await agent.CreateSessionAsync();
        var response = await agent.RunAsync(prompt, session);

        var text = response.Text ?? string.Empty;
        act?.SetTag("agent.response_length", text.Length);
        act?.SetStatus(ActivityStatusCode.Ok);
        return text;
    }

    private async Task<AIAgent> EnsureAgentAsync()
    {
        if (_agent is not null) return _agent;
        await _init.WaitAsync();
        try
        {
            if (_agent is not null) return _agent;

            var endpoint = _config["FOUNDRY_PROJECT_ENDPOINT"]
                ?? throw new InvalidOperationException("FOUNDRY_PROJECT_ENDPOINT is required");
            var model = _config["FOUNDRY_MODEL"] ?? "gpt-4.1-mini";
            var mcpUrl = _config["MCP_SERVER_URL"]
                ?? _config["MCP_AGENT_ENDPOINT"]
                ?? throw new InvalidOperationException("MCP_SERVER_URL is required");

            _logger.LogInformation(
                "Initialising agent: Foundry={Endpoint} Model={Model} MCP={Mcp}",
                endpoint, model, mcpUrl);

            _projectClient = new AIProjectClient(
                new Uri(endpoint),
                new DefaultAzureCredential());

            var mcpTool = ResponseTool.CreateMcpTool(
                serverLabel: "deployment_ops",
                serverUri: new Uri(mcpUrl),
                toolCallApprovalPolicy: new McpToolCallApprovalPolicy(
                    GlobalMcpToolCallApprovalPolicy.NeverRequireApproval));

            _agentVersion = await _projectClient.AgentAdministrationClient.CreateAgentVersionAsync(
                agentName: "DevOpsChatAgent",
                options: new ProjectsAgentVersionCreationOptions(
                    new DeclarativeAgentDefinition(model: model)
                    {
                        Instructions = """
                            You are a senior Site Reliability Engineer chatting with a developer.

                            Environment context (always assume this unless told otherwise):
                              - Azure DevOps organization: https://dev.azure.com/jonahanderssonazuredemos
                              - Project: msbuild2026eshopdemo
                              - Primary repository: shop-api (branch: main)
                              - Project URL: https://dev.azure.com/jonahanderssonazuredemos/msbuild2026eshopdemo
                            When a user mentions "the repo", "shop-api", "the pipeline", or "the org"
                            without qualification, they mean the resources above. Pass these
                            values to MCP tools when they need an org/project/repo argument.

                            When asked about deployments, you:
                              1. Call get_recent_deployments to find recent failures.
                              2. Call diagnose_deployment on the most recent failure.
                              3. If rollback is recommended, call create_rollback_pr
                                 against last_known_good_commit_sha.
                              4. Reply with a concise summary including the PR URL.
                            Rules:
                              - Never roll back without diagnosing first.
                              - Always include a clear, human-readable reason in the PR.
                              - If rollback is NOT recommended, say what you'd do instead and stop.
                            Be concise. Avoid filler.
                            """,
                        Tools = { mcpTool }
                    }));

            _agent = _projectClient.AsAIAgent(_agentVersion);
            _logger.LogInformation("Agent ready: {Name}", _agent.Name);
            return _agent;
        }
        finally
        {
            _init.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            if (_projectClient is not null && _agent is not null)
            {
                _projectClient.AgentAdministrationClient.DeleteAgent(_agent.Name);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to delete Foundry agent on shutdown");
        }
        _init.Dispose();
        await Task.CompletedTask;
    }
}
