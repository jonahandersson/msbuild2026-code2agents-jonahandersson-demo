using System.Text.RegularExpressions;

namespace DeploymentMcp.Services;

/// <summary>
/// Input validation helpers shared by the MCP tool surface and the AzDO client.
///
/// Centralised here so the security unit tests (DeploymentMcp.Tests) can pin
/// the rules without reaching into a private field.
///
/// Threat model for the MCP server:
///   - The agent (LLM) is treated as UNTRUSTED input. A prompt-injected
///     tool argument must not reach the AzDO REST API or be written to logs
///     in a way that could be used for log injection.
///   - Repo / branch / SHA values are interpolated into REST URLs, so we
///     reject anything that could break out of the path segment.
/// </summary>
internal static class Validation
{
    // Git SHAs are hex, 7-40 chars. Anything else is rejected.
    // `\z` (not `$`) — `$` in .NET also matches before a trailing newline.
    private static readonly Regex HexShaPattern =
        new(@"\A[a-fA-F0-9]{7,40}\z", RegexOptions.Compiled);

    // Git ref names are restrictive: letters, digits, ., _, -, /. We forbid
    // path traversal, control chars, and URL meta characters.
    private static readonly Regex SafeRefPattern =
        new(@"\A[A-Za-z0-9._\-/]{1,200}\z", RegexOptions.Compiled);

    // Repo names follow the same conservative rule (AzDO allows more, but
    // the demo only needs the common subset).
    private static readonly Regex SafeRepoPattern =
        new(@"\A[A-Za-z0-9._\-]{1,100}\z", RegexOptions.Compiled);

    public static bool IsValidSha(string? sha) =>
        !string.IsNullOrWhiteSpace(sha) && HexShaPattern.IsMatch(sha);

    public static bool IsSafeBranch(string? branch) =>
        !string.IsNullOrWhiteSpace(branch)
        && !branch!.Contains("..", StringComparison.Ordinal)
        && SafeRefPattern.IsMatch(branch);

    public static bool IsSafeRepo(string? repo) =>
        !string.IsNullOrWhiteSpace(repo)
        && !repo!.Contains("..", StringComparison.Ordinal)
        && SafeRepoPattern.IsMatch(repo);

    public static void EnsureSha(string sha, string paramName)
    {
        if (!IsValidSha(sha))
        {
            throw new ArgumentException(
                "Value must be a hex SHA of 7-40 characters.", paramName);
        }
    }

    public static void EnsureSafeBranch(string branch, string paramName)
    {
        if (!IsSafeBranch(branch))
        {
            throw new ArgumentException(
                "Branch contains characters that are not allowed.", paramName);
        }
    }

    public static void EnsureSafeRepo(string repo, string paramName)
    {
        if (!IsSafeRepo(repo))
        {
            throw new ArgumentException(
                "Repo contains characters that are not allowed.", paramName);
        }
    }
}
