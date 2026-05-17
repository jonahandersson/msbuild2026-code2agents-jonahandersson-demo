namespace DeploymentMcp.Services;

/// <summary>
/// The abstraction the MCP tools depend on. Two implementations exist:
///
/// - <see cref="AzureDevOpsClient"/> talks to a real Azure DevOps org via Managed Identity.
/// - <see cref="FakeDeploymentService"/> serves canned data for local dev and the Wi-Fi fallback.
///
/// Same interface, swappable at startup. That's how you keep demos reliable
/// without lying to the audience about what production looks like.
/// </summary>
public interface IDeploymentService
{
    Task<IReadOnlyList<Deployment>> GetRecentAsync(
        string repo,
        string branch,
        int take,
        CancellationToken cancellationToken);

    Task<DiagnosisResult> DiagnoseAsync(
        string deploymentId,
        CancellationToken cancellationToken);

    Task<RollbackPr> CreateRollbackPrAsync(
        string repo,
        string targetCommit,
        string reason,
        CancellationToken cancellationToken);
}
