using System.ComponentModel.DataAnnotations;

namespace DeploymentMcp.Configuration;

/// <summary>
/// Strongly-typed Azure DevOps config. Validated at startup —
/// if these are missing or malformed, the Function won't even boot.
/// That's intentional. Better to fail in CI than in front of 500 people.
/// </summary>
public sealed class AzureDevOpsOptions
{
    public const string SectionName = "AzureDevOps";

    [Required]
    [Url]
    public required string OrgUrl { get; init; }
        // e.g. "https://dev.azure.com/contoso"

    [Required]
    [MinLength(1)]
    public required string Project { get; init; }
        // e.g. "Shop"
}
