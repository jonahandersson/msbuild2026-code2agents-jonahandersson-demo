using DeploymentMcp.Services;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Extensions.Mcp;
using Microsoft.Extensions.Logging;

namespace DeploymentMcp.Tools;

/// <summary>
/// The MCP tools an agent can call to investigate and roll back deployments.
///
/// Three tools, three attributes. That's the whole contract with the agent.
/// </summary>
public sealed class DeploymentTools(
    IDeploymentService deployments,
    ILogger<DeploymentTools> logger)
{
    [Function(nameof(GetRecentDeployments))]
    public async Task<IReadOnlyList<Deployment>> GetRecentDeployments(
        [McpToolTrigger(
            toolName: "get_recent_deployments",
            description: "List the 5 most recent deployments for a repo and branch.")]
        ToolInvocationContext context,

        [McpToolProperty(
            propertyName: "repo",
            propertyType: "string",
            description: "Repository name, e.g. 'shop-api'.")]
        string repo,

        [McpToolProperty(
            propertyName: "branch",
            propertyType: "string",
            description: "Git branch, e.g. 'main'.")]
        string branch,

        CancellationToken cancellationToken)
    {
        logger.LogInformation(
            "MCP tool {Tool} called for {Repo}/{Branch}",
            nameof(GetRecentDeployments), repo, branch);

        return await deployments.GetRecentAsync(
            repo, branch, take: 5, cancellationToken);
    }

    [Function(nameof(DiagnoseDeployment))]
    public async Task<DiagnosisResult> DiagnoseDeployment(
        [McpToolTrigger(
            toolName: "diagnose_deployment",
            description:
                "Analyze a failed deployment and return the likely root cause " +
                "plus the last known-good commit SHA.")]
        ToolInvocationContext context,

        [McpToolProperty(
            propertyName: "deploymentId",
            propertyType: "string",
            description: "Deployment / build ID from get_recent_deployments.")]
        string deploymentId,

        CancellationToken cancellationToken)
    {
        logger.LogInformation(
            "Diagnosing deployment {DeploymentId}", deploymentId);

        return await deployments.DiagnoseAsync(deploymentId, cancellationToken);
    }

    [Function(nameof(CreateRollbackPr))]
    public async Task<RollbackPr> CreateRollbackPr(
        [McpToolTrigger(
            toolName: "create_rollback_pr",
            description:
                "Create a rollback pull request that reverts the repo " +
                "to a known-good commit. The 'reason' becomes the PR description.")]
        ToolInvocationContext context,

        [McpToolProperty(
            propertyName: "repo",
            propertyType: "string",
            description: "Repository name.")]
        string repo,

        [McpToolProperty(
            propertyName: "targetCommit",
            propertyType: "string",
            description: "Commit SHA to roll back to.")]
        string targetCommit,

        [McpToolProperty(
            propertyName: "reason",
            propertyType: "string",
            description: "Plain-English reason — shown in the PR description.")]
        string reason,

        CancellationToken cancellationToken)
    {
        logger.LogInformation(
            "Creating rollback PR in {Repo} to {Sha} — reason: {Reason}",
            repo, targetCommit, reason);

        return await deployments.CreateRollbackPrAsync(
            repo, targetCommit, reason, cancellationToken);
    }
}
