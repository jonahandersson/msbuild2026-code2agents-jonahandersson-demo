using Microsoft.Extensions.Logging;

namespace DeploymentMcp.Services;

/// <summary>
/// In-memory deployment service for local dev and the conference Wi-Fi fallback.
/// Don't ship this to production. Do use it on stage when the demo gods are angry.
///
/// DEMO-ONLY CONSTRAINT: The hardcoded scenario only works for the "shop-api → main"
/// rollback narrative — build IDs 2887–2891 and commit SHAs (a4f9c12e failing,
/// b1e3d847 last-known-good) are wired specifically for that path. Any other
/// repo/branch returns the same fixture. For real environments use
/// <see cref="AzureDevOpsClient"/> instead (set DemoMode=false).
/// </summary>
public sealed class FakeDeploymentService(ILogger<FakeDeploymentService> logger)
    : IDeploymentService
{
    private static readonly DateTimeOffset BaseTime =
        DateTimeOffset.Parse("2026-05-15T08:00:00Z");

    public Task<IReadOnlyList<Deployment>> GetRecentAsync(
        string repo, string branch, int take, CancellationToken cancellationToken)
    {
        logger.LogInformation(
            "[FAKE] GetRecentAsync({Repo}, {Branch})", repo, branch);

        IReadOnlyList<Deployment> results =
        [
            // The failing one — what the agent will diagnose
            new Deployment(
                Id: "build-2891",
                Repo: repo,
                Branch: branch,
                CommitSha: "a4f9c12e",
                Status: "failed",
                StartedAt: BaseTime.AddMinutes(-12),
                FinishedAt: BaseTime.AddMinutes(-6),
                FailureMessage:
                    "Migration 20260515_AddCustomerLoyalty timed out " +
                    "after 30s during ALTER TABLE Orders ADD COLUMN..."),

            // Slightly older, also failed — but for a different reason
            new Deployment(
                Id: "build-2890",
                Repo: repo,
                Branch: branch,
                CommitSha: "a4f9c12e",
                Status: "failed",
                StartedAt: BaseTime.AddMinutes(-40),
                FinishedAt: BaseTime.AddMinutes(-34),
                FailureMessage: "Same as build-2891"),

            // The last known-good — this is what the agent should roll back to
            new Deployment(
                Id: "build-2889",
                Repo: repo,
                Branch: branch,
                CommitSha: "b1e3d847",
                Status: "succeeded",
                StartedAt: BaseTime.AddHours(-3),
                FinishedAt: BaseTime.AddHours(-3).AddMinutes(7),
                FailureMessage: null),

            new Deployment(
                Id: "build-2888",
                Repo: repo,
                Branch: branch,
                CommitSha: "9c2a55f1",
                Status: "succeeded",
                StartedAt: BaseTime.AddHours(-8),
                FinishedAt: BaseTime.AddHours(-8).AddMinutes(6),
                FailureMessage: null),

            new Deployment(
                Id: "build-2887",
                Repo: repo,
                Branch: branch,
                CommitSha: "5e8b3290",
                Status: "succeeded",
                StartedAt: BaseTime.AddHours(-14),
                FinishedAt: BaseTime.AddHours(-14).AddMinutes(5),
                FailureMessage: null),
        ];

        return Task.FromResult(results);
    }

    public Task<DiagnosisResult> DiagnoseAsync(
        string deploymentId, CancellationToken cancellationToken)
    {
        logger.LogInformation("[FAKE] DiagnoseAsync({DeploymentId})", deploymentId);

        var result = new DiagnosisResult(
            DeploymentId: deploymentId,
            LikelyRootCause:
                "Schema migration timeout. ALTER TABLE on Orders ran longer " +
                "than the 30s pipeline timeout. The table is large and the " +
                "migration didn't batch its updates.",
            Evidence:
                "Pipeline log line 412: 'Command timeout expired'. " +
                "The same migration ran cleanly on staging where Orders is empty.",
            LastKnownGoodCommitSha: "b1e3d847",
            RollbackRecommended: true);

        return Task.FromResult(result);
    }

    public Task<RollbackPr> CreateRollbackPrAsync(
        string repo, string targetCommit, string reason,
        CancellationToken cancellationToken)
    {
        logger.LogInformation(
            "[FAKE] CreateRollbackPrAsync({Repo}, {Sha})", repo, targetCommit);

        // Deterministic fake PR ID so the demo output is consistent.
        var prId = 4271;

        var result = new RollbackPr(
            PullRequestId: prId,
            Url:
                $"https://dev.azure.com/contoso/Shop/_git/{repo}/pullrequest/{prId}",
            Title: $"Rollback {repo} to {targetCommit[..7]} — automated by agent",
            SourceBranch: $"rollback/auto-{targetCommit[..7]}",
            TargetBranch: "main");

        return Task.FromResult(result);
    }
}
