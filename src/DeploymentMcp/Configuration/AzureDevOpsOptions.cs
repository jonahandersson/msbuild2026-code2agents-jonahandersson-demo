using System.ComponentModel.DataAnnotations;

namespace DeploymentMcp.Configuration;

/// <summary>
/// Strongly-typed Azure DevOps config. Validated at startup when DemoMode=false.
/// When DemoMode=true, validation is skipped in Program.cs so the Function still
/// boots with empty values (the FakeDeploymentService doesn't need them).
/// Better to fail in CI than in front of 500 people.
/// </summary>
public sealed class AzureDevOpsOptions
{
    public const string SectionName = "AzureDevOps";

    [Required]
    [Url]
    public string OrgUrl { get; init; } = string.Empty;
        // e.g. "https://dev.azure.com/contoso"

    [Required]
    [MinLength(1)]
    public string Project { get; init; } = string.Empty;
        // e.g. "Shop"
}
