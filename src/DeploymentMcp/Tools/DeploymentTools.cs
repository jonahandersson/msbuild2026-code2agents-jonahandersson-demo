using DeploymentMcp.Services;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Extensions.Mcp;
using Microsoft.Extensions.Logging;

namespace DeploymentMcp.Tools;

/// <summary>
/// The MCP tools an agent can call to investigate and roll back deployments.
///
/// Three tools, three attributes. That's the whole contract with the agent.
///
/// Attribute API (verified against Azure-Samples/remote-mcp-functions-dotnet,
/// Microsoft.Azure.Functions.Worker.Extensions.Mcp v1.x):
///   [McpToolTrigger(toolName, description)]
///   [McpToolProperty(name, description, required = false)]
/// Property TYPE is inferred from the C# parameter type — don't pass it.
/// </summary>
public sealed class DeploymentTools(
    IDeploymentService deployments,
    ILogger<DeploymentTools> logger)
{
    [Function(nameof(GetRecentDeployments))]
    public async Task<IReadOnlyList<Deployment>> GetRecentDeployments(
        [McpToolTrigger(
            "get_recent_deployments",
            "List the 5 most recent deployments for a repo and branch.")]
        ToolInvocationContext context,

        [McpToolProperty(
            "repo",
            "Repository name, e.g. 'shop-api'.",
            true)]
        string repo,

        [McpToolProperty(
            "branch",
            "Git branch, e.g. 'main'.",
            true)]
        string branch,

        CancellationToken cancellationToken)
    {
        using var scope = logger.BeginScope(new Dictionary<string, object>
        {
            ["Tool"] = "get_recent_deployments",
            ["Repo"] = repo,
            ["Branch"] = branch,
        });
        logger.LogInformation(
            "MCP tool {Tool} called for {Repo}/{Branch}",
            "get_recent_deployments", repo, branch);

        return await deployments.GetRecentAsync(
            repo, branch, take: 5, cancellationToken);
    }

    [Function(nameof(DiagnoseDeployment))]
    public async Task<DiagnosisResult> DiagnoseDeployment(
        [McpToolTrigger(
            "diagnose_deployment",
            "Analyze a failed deployment and return the likely root cause " +
            "plus the last known-good commit SHA.")]
        ToolInvocationContext context,

        [McpToolProperty(
            "deploymentId",
            "Deployment / build ID from get_recent_deployments.",
            true)]
        string deploymentId,

        CancellationToken cancellationToken)
    {
        using var scope = logger.BeginScope(new Dictionary<string, object>
        {
            ["Tool"] = "diagnose_deployment",
            ["DeploymentId"] = deploymentId,
        });
        logger.LogInformation(
            "MCP tool {Tool} called for {DeploymentId}",
            "diagnose_deployment", deploymentId);

        return await deployments.DiagnoseAsync(deploymentId, cancellationToken);
    }

    [Function(nameof(CreateRollbackPr))]
    public async Task<RollbackPr> CreateRollbackPr(
        [McpToolTrigger(
            "create_rollback_pr",
            "Create a rollback pull request that reverts the repo " +
            "to a known-good commit. The 'reason' becomes the PR description.")]
        ToolInvocationContext context,

        [McpToolProperty(
            "repo",
            "Repository name.",
            true)]
        string repo,

        [McpToolProperty(
            "targetCommit",
            "Commit SHA to roll back to.",
            true)]
        string targetCommit,

        [McpToolProperty(
            "reason",
            "Plain-English reason — shown in the PR description.",
            true)]
        string reason,

        CancellationToken cancellationToken)
    {
        using var scope = logger.BeginScope(new Dictionary<string, object>
        {
            ["Tool"] = "create_rollback_pr",
            ["Repo"] = repo,
            ["TargetCommit"] = targetCommit,
        });
        logger.LogInformation(
            "MCP tool {Tool} called for {Repo} -> {Sha} (reason: {Reason})",
            "create_rollback_pr", repo, targetCommit, reason);

        return await deployments.CreateRollbackPrAsync(
            repo, targetCommit, reason, cancellationToken);
    }
}
