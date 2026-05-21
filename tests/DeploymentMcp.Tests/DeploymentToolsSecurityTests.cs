using DeploymentMcp.Services;
using DeploymentMcp.Tools;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace DeploymentMcp.Tests;

/// <summary>
/// Security-focused tests for the MCP tool surface. The agent (LLM) is
/// treated as UNTRUSTED, so anything it can pass as a tool argument must
/// be rejected before it reaches the AzDO REST API.
///
/// We construct <see cref="DeploymentTools"/> with the safe
/// <see cref="FakeDeploymentService"/> so the test never makes a real call;
/// validation must throw before the service is invoked.
/// </summary>
public class DeploymentToolsSecurityTests
{
    private static DeploymentTools CreateSut() =>
        new(
            new FakeDeploymentService(NullLogger<FakeDeploymentService>.Instance),
            NullLogger<DeploymentTools>.Instance);

    // ---- get_recent_deployments ----------------------------------------

    [Theory]
    [InlineData(null, "main")]
    [InlineData("", "main")]
    [InlineData("shop-api", null)]
    [InlineData("shop-api", "")]
    [InlineData("shop/api", "main")]               // illegal repo char
    [InlineData("shop-api", "../main")]            // path traversal in branch
    [InlineData("shop-api", "main; rm -rf /")]     // command injection
    [InlineData("shop-api", "main\r\nLog injected")]
    public async Task GetRecentDeployments_rejects_bad_inputs(string? repo, string? branch)
    {
        var sut = CreateSut();

        var act = async () =>
            await sut.GetRecentDeployments(context: null!, repo!, branch!, CancellationToken.None);

        await act.Should().ThrowAsync<ArgumentException>();
    }

    // ---- diagnose_deployment -------------------------------------------

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not-an-int")]
    [InlineData("-1")]
    [InlineData("0")]
    [InlineData("1; DROP TABLE Builds")]
    public async Task DiagnoseDeployment_rejects_bad_inputs(string? deploymentId)
    {
        var sut = CreateSut();

        var act = async () =>
            await sut.DiagnoseDeployment(context: null!, deploymentId!, CancellationToken.None);

        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task DiagnoseDeployment_accepts_positive_integer()
    {
        var sut = CreateSut();

        var result = await sut.DiagnoseDeployment(
            context: null!, deploymentId: "2891", CancellationToken.None);

        result.Should().NotBeNull();
    }

    // ---- create_rollback_pr (destructive: most-scrutinised) ------------

    [Theory]
    [InlineData(null, "a4f9c12e", "valid reason")]
    [InlineData("shop-api", null, "valid reason")]
    [InlineData("shop-api", "a4f9c12e", null)]
    [InlineData("shop-api", "a4f9c12e", "")]
    [InlineData("shop-api", "not-hex!!", "valid reason")]
    [InlineData("shop-api", "abc", "valid reason")]                  // < 7 chars
    [InlineData("shop-api", "a4f9c12e; rm -rf /", "valid reason")]
    [InlineData("../other", "a4f9c12e", "valid reason")]
    public async Task CreateRollbackPr_rejects_bad_inputs(
        string? repo, string? targetCommit, string? reason)
    {
        var sut = CreateSut();

        var act = async () =>
            await sut.CreateRollbackPr(
                context: null!, repo!, targetCommit!, reason!, CancellationToken.None);

        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task CreateRollbackPr_rejects_overlong_reason()
    {
        var sut = CreateSut();
        var giantReason = new string('x', 2001);

        var act = async () =>
            await sut.CreateRollbackPr(
                context: null!, "shop-api", "a4f9c12e", giantReason, CancellationToken.None);

        await act.Should().ThrowAsync<ArgumentException>()
            .WithMessage("*2000*");
    }

    [Fact]
    public async Task CreateRollbackPr_accepts_valid_input()
    {
        var sut = CreateSut();

        var pr = await sut.CreateRollbackPr(
            context: null!,
            repo: "shop-api",
            targetCommit: "b1e3d847",
            reason: "Rolling back migration 20260515 due to ALTER TABLE timeout.",
            cancellationToken: CancellationToken.None);

        pr.Should().NotBeNull();
        pr.PullRequestId.Should().BeGreaterThan(0);
    }
}
