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
    [HttpsUrl]
    public string OrgUrl { get; init; } = string.Empty;
        // e.g. "https://dev.azure.com/contoso"

    [Required]
    [MinLength(1)]
    public string Project { get; init; } = string.Empty;
        // e.g. "Shop"
}

/// <summary>
/// Stricter than the built-in <see cref="UrlAttribute"/>: requires an
/// absolute https:// URL. ADO calls go over the public internet and the
/// access tokens we mint are bearer tokens \u2014 plain HTTP would leak them.
/// </summary>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field,
    AllowMultiple = false)]
internal sealed class HttpsUrlAttribute : ValidationAttribute
{
    protected override ValidationResult? IsValid(
        object? value, ValidationContext validationContext)
    {
        if (value is not string s || string.IsNullOrWhiteSpace(s))
        {
            return new ValidationResult(
                $"{validationContext.DisplayName} is required.");
        }

        if (!Uri.TryCreate(s, UriKind.Absolute, out var uri) ||
            uri.Scheme != Uri.UriSchemeHttps)
        {
            return new ValidationResult(
                $"{validationContext.DisplayName} must be an absolute " +
                "https:// URL.");
        }

        return ValidationResult.Success;
    }
}
