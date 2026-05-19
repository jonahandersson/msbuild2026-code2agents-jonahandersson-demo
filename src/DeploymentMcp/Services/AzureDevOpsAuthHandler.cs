using System.Net.Http.Headers;
using Azure.Core;
using Azure.Identity;

namespace DeploymentMcp.Services;

/// <summary>
/// Per-request bearer-token injector for <see cref="AzureDevOpsClient"/>.
///
/// Why a DelegatingHandler instead of stamping <c>DefaultRequestHeaders</c>?
/// <list type="bullet">
///   <item>Thread-safe: concurrent MCP tool invocations share the typed
///         <see cref="HttpClient"/>, so mutating <c>DefaultRequestHeaders</c>
///         races and can leak/swap tokens between requests.</item>
///   <item>Token cache: we hold the most recent <see cref="AccessToken"/>
///         and only refresh it inside a 5-minute safety window before expiry.
///         Cuts Entra token requests from one-per-call to one-per-hour-ish.</item>
/// </list>
/// </summary>
internal sealed class AzureDevOpsAuthHandler : DelegatingHandler
{
    // Azure DevOps resource ID — same value in every Entra tenant.
    private const string AzureDevOpsResourceId =
        "499b84ac-1321-427f-aa17-267ca6975798";

    private static readonly string[] Scopes =
        [$"{AzureDevOpsResourceId}/.default"];

    // Refresh the cached token when it's within this window of expiring.
    private static readonly TimeSpan RefreshSkew = TimeSpan.FromMinutes(5);

    private readonly TokenCredential _credential;
    private readonly SemaphoreSlim _refreshLock = new(1, 1);
    private AccessToken _cachedToken;

    public AzureDevOpsAuthHandler() : this(new DefaultAzureCredential()) { }

    public AzureDevOpsAuthHandler(TokenCredential credential)
    {
        _credential = credential;
    }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var token = await GetTokenAsync(cancellationToken).ConfigureAwait(false);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
    }

    private async Task<string> GetTokenAsync(CancellationToken cancellationToken)
    {
        if (IsTokenFresh(_cachedToken))
        {
            return _cachedToken.Token;
        }

        await _refreshLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (IsTokenFresh(_cachedToken))
            {
                return _cachedToken.Token;
            }

            _cachedToken = await _credential.GetTokenAsync(
                new TokenRequestContext(Scopes), cancellationToken)
                .ConfigureAwait(false);
            return _cachedToken.Token;
        }
        finally
        {
            _refreshLock.Release();
        }
    }

    private static bool IsTokenFresh(AccessToken token) =>
        token.Token is { Length: > 0 } &&
        token.ExpiresOn > DateTimeOffset.UtcNow.Add(RefreshSkew);
}
