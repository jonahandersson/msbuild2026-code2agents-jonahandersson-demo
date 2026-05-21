using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using DeploymentMcp.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DeploymentMcp.Services;

/// <summary>
/// The real Azure DevOps client. Uses <see cref="Azure.Identity.DefaultAzureCredential"/>
/// via <see cref="AzureDevOpsAuthHandler"/> so the same code runs locally
/// (your az CLI login) and in Azure (Managed Identity).
///
/// **No PATs. No connection strings. That's the production story.**
///
/// Auth is injected per-request by the handler chained onto the typed
/// <see cref="HttpClient"/> in <c>Program.cs</c>. Tokens are cached inside
/// the handler (5-minute refresh skew), so this class never touches credentials.
/// </summary>
public sealed class AzureDevOpsClient(
    HttpClient http,
    IOptions<AzureDevOpsOptions> options,
    ILogger<AzureDevOpsClient> logger) : IDeploymentService
{
    // Azure DevOps API returns camelCase JSON; our DTOs use PascalCase.
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private readonly AzureDevOpsOptions _opts = options.Value;

    // Repo name -> GUID cache. The AzDO Builds API requires the repository
    // GUID + repositoryType=TfsGit; passing the repo name returns 400.
    private readonly Dictionary<string, string> _repoIdCache =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly SemaphoreSlim _repoIdLock = new(1, 1);

    public async Task<IReadOnlyList<Deployment>> GetRecentAsync(
        string repo, string branch, int take, CancellationToken cancellationToken)
    {
        var repoId = await ResolveRepoIdAsync(repo, cancellationToken);

        var url =
            $"{_opts.OrgUrl}/{_opts.Project}/_apis/build/builds" +
            $"?repositoryId={repoId}" +
            "&repositoryType=TfsGit" +
            $"&branchName=refs/heads/{Uri.EscapeDataString(branch)}" +
            $"&$top={take}" +
            "&api-version=7.1";

        logger.LogInformation("GET {Url}", url);

        var response = await http.GetFromJsonAsync<BuildsResponse>(
            url, JsonOptions, cancellationToken);

        if (response?.Value is null)
        {
            return [];
        }

        return response.Value
            .Select(b => new Deployment(
                Id: b.Id.ToString(),
                Repo: repo,
                Branch: branch,
                CommitSha: b.SourceVersion ?? string.Empty,
                Status: MapStatus(b.Result, b.Status),
                StartedAt: b.StartTime ?? DateTimeOffset.UtcNow,
                FinishedAt: b.FinishTime,
                FailureMessage: null))
            .ToList();
    }

    public async Task<DiagnosisResult> DiagnoseAsync(
        string deploymentId, CancellationToken cancellationToken)
    {
        // Three things have to happen here:
        //   1. Pull the build details (so we know which repo + branch this was)
        //   2. Pull the failing log content + pattern-match a root cause
        //   3. Find the most recent successful build → that's the rollback target
        //
        // Each step is independent. Today the first failed step short-circuits
        // to a Fallback diagnosis; see the note on GetBuildAsync below.

        logger.LogInformation(
            "Diagnosing deployment {DeploymentId}", deploymentId);

        // --- 1. Build details ---
        var build = await GetBuildAsync(deploymentId, cancellationToken);
        if (build is null)
        {
            return Fallback(deploymentId, "Build not found in Azure DevOps.");
        }

        // --- 2. Log analysis ---
        var logExcerpt = await FetchFailingLogExcerptAsync(
            deploymentId, cancellationToken);

        var (rootCause, evidence) = AnalyzeLog(logExcerpt);

        // --- 3. Last known-good commit ---
        var lastGoodSha = await FindLastKnownGoodCommitAsync(
            build.Repository.Id, build.SourceBranch, deploymentId, cancellationToken);

        var rollbackRecommended =
            !string.IsNullOrEmpty(lastGoodSha) &&
            lastGoodSha != build.SourceVersion &&
            IsRollbackable(rootCause);

        logger.LogInformation(
            "Diagnosis complete: rootCause={RootCause}, " +
            "lastGood={LastGood}, rollback={Rollback}",
            rootCause, lastGoodSha, rollbackRecommended);

        return new DiagnosisResult(
            DeploymentId: deploymentId,
            LikelyRootCause: rootCause,
            Evidence: evidence,
            LastKnownGoodCommitSha: lastGoodSha ?? "unknown",
            RollbackRecommended: rollbackRecommended);
    }

    // -----------------------------------------------------------------------
    // Diagnosis helpers
    // -----------------------------------------------------------------------

    private async Task<BuildDetail?> GetBuildAsync(
        string deploymentId, CancellationToken cancellationToken)
    {
        var url =
            $"{_opts.OrgUrl}/{_opts.Project}/_apis/build/builds/" +
            $"{deploymentId}?api-version=7.1";

        try
        {
            return await http.GetFromJsonAsync<BuildDetail>(
                url, JsonOptions, cancellationToken);
        }
        catch (HttpRequestException ex)
        {
            logger.LogWarning(ex,
                "Failed to fetch build {DeploymentId} details", deploymentId);
            return null;
        }
    }

    private async Task<string> FetchFailingLogExcerptAsync(
        string deploymentId, CancellationToken cancellationToken)
    {
        // Step 1: list the logs for this build (each task gets its own log).
        var listUrl =
            $"{_opts.OrgUrl}/{_opts.Project}/_apis/build/builds/" +
            $"{deploymentId}/logs?api-version=7.1";

        BuildLogsResponse? list;
        try
        {
            list = await http.GetFromJsonAsync<BuildLogsResponse>(
                listUrl, JsonOptions, cancellationToken);
        }
        catch (HttpRequestException ex)
        {
            logger.LogWarning(ex,
                "Failed to list logs for {DeploymentId}", deploymentId);
            return string.Empty;
        }

        if (list?.Value is not { Length: > 0 } logs)
        {
            return string.Empty;
        }

        // Step 2: the failing task is usually the last log. We fetch the
        // most recent few and let the analyzer pick the signal.
        // Each log can be many MB — we cap how much we read.
        var combined = new StringBuilder(capacity: 16_384);

        foreach (var log in logs.TakeLast(3))
        {
            var content = await FetchLogContentAsync(
                log.Url, MaxLogBytes, cancellationToken);
            if (!string.IsNullOrEmpty(content))
            {
                combined.AppendLine(content);
            }
        }

        return combined.ToString();
    }

    private async Task<string> FetchLogContentAsync(
        string logUrl, int maxBytes, CancellationToken cancellationToken)
    {
        try
        {
            using var response = await http.GetAsync(
                logUrl,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);

            response.EnsureSuccessStatusCode();

            await using var stream =
                await response.Content.ReadAsStreamAsync(cancellationToken);

            // Read up to maxBytes — large logs would blow memory otherwise.
            var buffer = new byte[maxBytes];
            var totalRead = 0;
            int read;
            while (totalRead < buffer.Length &&
                   (read = await stream.ReadAsync(
                       buffer.AsMemory(totalRead), cancellationToken)) > 0)
            {
                totalRead += read;
            }

            return Encoding.UTF8.GetString(buffer, 0, totalRead);
        }
        catch (HttpRequestException ex)
        {
            logger.LogWarning(ex,
                "Failed to fetch log content from {Url}", logUrl);
            return string.Empty;
        }
    }

    private static (string RootCause, string Evidence) AnalyzeLog(string logText)
    {
        if (string.IsNullOrWhiteSpace(logText))
        {
            return ("Build failed but no log content was retrievable.",
                "No log evidence available.");
        }

        // Walk through known failure patterns in priority order.
        foreach (var (pattern, rootCause) in FailurePatterns)
        {
            var match = pattern.Match(logText);
            if (!match.Success)
            {
                continue;
            }

            // Grab a few lines around the match for evidence.
            var evidence = ExtractEvidenceAround(logText, match.Index);
            return (rootCause, evidence);
        }

        // No known pattern matched. Return the last few ##[error] lines
        // as evidence — generic but better than nothing.
        var genericEvidence = ExtractLastErrorLines(logText);
        return (
            "Build failed with an unrecognized error pattern. " +
            "Manual review of the log is recommended.",
            genericEvidence);
    }

    private static string ExtractEvidenceAround(string log, int matchIndex)
    {
        // 200 chars before, 400 chars after — usually enough for context.
        var start = Math.Max(0, matchIndex - 200);
        var end = Math.Min(log.Length, matchIndex + 400);
        var slice = log[start..end].Trim();

        // Normalize whitespace so the agent's reasoning isn't cluttered.
        return Regex.Replace(slice, @"\s+", " ").Trim();
    }

    private static string ExtractLastErrorLines(string log)
    {
        var errorLines = log
            .Split('\n')
            .Where(l => l.Contains("##[error]", StringComparison.OrdinalIgnoreCase))
            .TakeLast(3)
            .ToArray();

        return errorLines.Length > 0
            ? string.Join(" | ", errorLines.Select(l => l.Trim()))
            : "No structured error lines found in log.";
    }

    private async Task<string?> FindLastKnownGoodCommitAsync(
        string repositoryId, string branchRef, string excludeBuildId,
        CancellationToken cancellationToken)
    {
        // Strip 'refs/heads/' prefix if present
        var branch = branchRef.StartsWith("refs/heads/")
            ? branchRef["refs/heads/".Length..]
            : branchRef;

        var url =
            $"{_opts.OrgUrl}/{_opts.Project}/_apis/build/builds" +
            $"?repositoryId={Uri.EscapeDataString(repositoryId)}" +
            "&repositoryType=TfsGit" +
            $"&branchName=refs/heads/{Uri.EscapeDataString(branch)}" +
            "&resultFilter=succeeded" +
            "&$top=10" +
            "&api-version=7.1";

        try
        {
            var response = await http.GetFromJsonAsync<BuildsResponse>(
                url, JsonOptions, cancellationToken);

            // First succeeded build that isn't the build we're diagnosing.
            return response?.Value
                .Where(b => b.Id.ToString() != excludeBuildId)
                .Select(b => b.SourceVersion)
                .FirstOrDefault(sha => !string.IsNullOrEmpty(sha));
        }
        catch (HttpRequestException ex)
        {
            logger.LogWarning(ex,
                "Could not find last known-good commit for {Repo}/{Branch}",
                repositoryId, branch);
            return null;
        }
    }

    private static bool IsRollbackable(string rootCause) =>
        // Rollback makes sense for runtime/deployment failures, not for
        // every kind of build error. A failing unit test, for example,
        // is not a rollback candidate — that's a code fix.
        !rootCause.Contains("tests failed", StringComparison.OrdinalIgnoreCase);

    private static DiagnosisResult Fallback(string deploymentId, string reason) =>
        new(
            DeploymentId: deploymentId,
            LikelyRootCause: reason,
            Evidence: "Diagnosis could not be completed.",
            LastKnownGoodCommitSha: "unknown",
            RollbackRecommended: false);

    // -----------------------------------------------------------------------
    // Failure patterns. Order matters — the first match wins.
    // -----------------------------------------------------------------------

    private const int MaxLogBytes = 256 * 1024; // 256 KB per log file

    private static readonly (Regex Pattern, string RootCause)[] FailurePatterns =
    [
        (new Regex(@"command\s+timeout\s+expired|execution\s+timeout\s+expired",
            RegexOptions.IgnoreCase | RegexOptions.Compiled),
         "Schema migration timeout. A SQL command exceeded the configured " +
         "timeout — typically an ALTER TABLE on a large production table " +
         "that should have been batched."),

        (new Regex(@"out\s+of\s+memory|outofmemoryexception",
            RegexOptions.IgnoreCase | RegexOptions.Compiled),
         "Out of memory during build or deployment. Check the build agent's " +
         "memory allocation or the application's memory footprint."),

        (new Regex(@"connection\s+(refused|timed?\s+out)",
            RegexOptions.IgnoreCase | RegexOptions.Compiled),
         "Network connectivity failure. A dependency the pipeline relies on " +
         "was unreachable — check firewall rules and service health."),

        (new Regex(@"(401|403)\s+(unauthorized|forbidden)|access\s+denied",
            RegexOptions.IgnoreCase | RegexOptions.Compiled),
         "Authorization failure. The pipeline's identity lacks permissions " +
         "for a resource it tried to access."),

        (new Regex(@"tests?\s+failed|test\s+run\s+aborted",
            RegexOptions.IgnoreCase | RegexOptions.Compiled),
         "Unit or integration tests failed. Fix the failing tests or revert " +
         "the change that broke them — rollback is not the right action here."),

        (new Regex(@"the\s+build\s+failed|##\[error\]",
            RegexOptions.IgnoreCase | RegexOptions.Compiled),
         "Generic build failure. Manual review of the log is recommended " +
         "to identify the specific failing step."),
    ];

    public async Task<RollbackPr> CreateRollbackPrAsync(
        string repo, string targetCommit, string reason,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repo);
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        ValidateSha(targetCommit);

        // 1. Create a rollback branch at targetCommit
        var shortSha = targetCommit[..7];
        var branchName = $"rollback/auto-{shortSha}";

        var refsBody = new[]
        {
            new
            {
                name = $"refs/heads/{branchName}",
                oldObjectId = "0000000000000000000000000000000000000000",
                newObjectId = targetCommit,
            }
        };

        var refsUrl =
            $"{_opts.OrgUrl}/{_opts.Project}/_apis/git/repositories/" +
            $"{repo}/refs?api-version=7.1";

        using (var refsResponse = await http.PostAsJsonAsync(
            refsUrl, refsBody, JsonOptions, cancellationToken))
        {
            refsResponse.EnsureSuccessStatusCode();
        }

        // 2. Open the PR
        var prBody = new
        {
            sourceRefName = $"refs/heads/{branchName}",
            targetRefName = "refs/heads/main",
            title = $"Rollback {repo} to {shortSha} — automated by agent",
            description = reason,
        };

        var prUrl =
            $"{_opts.OrgUrl}/{_opts.Project}/_apis/git/repositories/" +
            $"{repo}/pullrequests?api-version=7.1";

        using var prResponse = await http.PostAsJsonAsync(
            prUrl, prBody, JsonOptions, cancellationToken);
        prResponse.EnsureSuccessStatusCode();

        var created = await prResponse.Content.ReadFromJsonAsync<PullRequestResponse>(
            JsonOptions, cancellationToken)
            ?? throw new InvalidOperationException("Empty PR response.");

        return new RollbackPr(
            PullRequestId: created.PullRequestId,
            Url: $"{_opts.OrgUrl}/{_opts.Project}/_git/{repo}/" +
                 $"pullrequest/{created.PullRequestId}",
            Title: created.Title,
            SourceBranch: branchName,
            TargetBranch: "main");
    }

    private static void ValidateSha(string sha) =>
        Validation.EnsureSha(sha, nameof(sha));

    private async Task<string> ResolveRepoIdAsync(
        string repo, CancellationToken cancellationToken)
    {
        if (_repoIdCache.TryGetValue(repo, out var cached))
        {
            return cached;
        }

        await _repoIdLock.WaitAsync(cancellationToken);
        try
        {
            if (_repoIdCache.TryGetValue(repo, out cached))
            {
                return cached;
            }

            var url =
                $"{_opts.OrgUrl}/{_opts.Project}/_apis/git/repositories/" +
                $"{Uri.EscapeDataString(repo)}?api-version=7.1";

            var info = await http.GetFromJsonAsync<GitRepoInfo>(
                url, JsonOptions, cancellationToken);

            if (info is null || string.IsNullOrEmpty(info.Id))
            {
                throw new InvalidOperationException(
                    $"Could not resolve repository id for '{repo}'.");
            }

            _repoIdCache[repo] = info.Id;
            return info.Id;
        }
        finally
        {
            _repoIdLock.Release();
        }
    }

    private static string MapStatus(string? result, string? status) =>
        (result?.ToLowerInvariant(), status?.ToLowerInvariant()) switch
        {
            ("succeeded", _) => "succeeded",
            ("failed", _) => "failed",
            ("canceled", _) => "canceled",
            (_, "inprogress") => "running",
            _ => "unknown",
        };

    // --- Lightweight DTOs for Azure DevOps REST responses ---
    private sealed record BuildsResponse(BuildDto[] Value);

    private sealed record BuildDto(
        int Id,
        string? Result,
        string? Status,
        string? SourceVersion,
        DateTimeOffset? StartTime,
        DateTimeOffset? FinishTime);

    private sealed record BuildDetail(
        int Id,
        string? Result,
        string? Status,
        string SourceVersion,
        string SourceBranch,
        BuildRepositoryRef Repository);

    private sealed record BuildRepositoryRef(string Id, string? Name);

    private sealed record BuildLogsResponse(BuildLogDto[] Value);

    private sealed record BuildLogDto(
        int Id,
        string Type,
        string Url);

    private sealed record PullRequestResponse(int PullRequestId, string Title);

    private sealed record GitRepoInfo(string Id, string Name);
}
