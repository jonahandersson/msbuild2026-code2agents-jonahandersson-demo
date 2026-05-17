namespace DeploymentMcp.Services;

/// <summary>A single deployment / pipeline run.</summary>
public sealed record Deployment(
    string Id,
    string Repo,
    string Branch,
    string CommitSha,
    string Status,        // "succeeded" | "failed" | "running"
    DateTimeOffset StartedAt,
    DateTimeOffset? FinishedAt,
    string? FailureMessage);

/// <summary>What the diagnose tool returns to the agent.</summary>
public sealed record DiagnosisResult(
    string DeploymentId,
    string LikelyRootCause,
    string Evidence,
    string LastKnownGoodCommitSha,
    bool RollbackRecommended);

/// <summary>The receipt of a created rollback PR.</summary>
public sealed record RollbackPr(
    int PullRequestId,
    string Url,
    string Title,
    string SourceBranch,
    string TargetBranch);
