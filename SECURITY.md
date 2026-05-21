# Security

This document covers how we keep the AI agent + MCP server secure, end-to-end,
from the LLM all the way down to the Azure resources.

## Threat model in one paragraph

The LLM agent is **untrusted input**. Anything it can put on the wire — tool
arguments, prompts, even retried calls — has to pass the same input validation
we'd apply to a public-facing HTTP API. Beyond the agent, the MCP server runs
on Azure with **no shared secrets** (managed identity to AzDO, MI to Foundry,
MI to storage, system key on the MCP endpoint).

## Layers and what protects them

| Layer | Control | Where |
|---|---|---|
| LLM → tool args | Regex allow-lists for `repo`, `branch`, `sha`; positive-int check on `deploymentId`; 2000-char cap on free-text `reason` | [Validation.cs](src/DeploymentMcp/Services/Validation.cs), [DeploymentTools.cs](src/DeploymentMcp/Tools/DeploymentTools.cs) |
| Destructive tools | `AGENT_APPROVAL_MODE=prod` → `AlwaysRequireApproval` on `create_rollback_pr` | [Program.cs](src/DevOpsAgent/Program.cs) |
| Agent → MCP transport | System key (`?code=…`) on `/runtime/webhooks/mcp/sse`; HTTPS-only | infra |
| MCP → AzDO | `DefaultAzureCredential` + bearer token, 5-min refresh skew, no PATs | [AzureDevOpsAuthHandler.cs](src/DeploymentMcp/Services/AzureDevOpsAuthHandler.cs) |
| MCP → Foundry | Managed identity, `disableLocalAuth: true` | [infra/main.bicep](infra/main.bicep) |
| MCP → Storage | Managed identity, `allowSharedKeyAccess: false`, HTTPS-only | infra |
| Secrets in app settings | Key Vault references planned (TODO in bicep) for non-demo settings | infra |
| Infra-as-code | PSRule for Azure (WAF Security baseline) in CI | [ps-rule.yaml](ps-rule.yaml) |
| Dependencies | NuGet central package management + `NU1902` surfaced as warnings | [Directory.Packages.props](Directory.Packages.props) |

## Tests that pin the security rules

Run them locally:

```pwsh
dotnet test tests/DeploymentMcp.Tests/DeploymentMcp.Tests.csproj
```

Two test classes, both run in CI:

- [ValidationTests](tests/DeploymentMcp.Tests/ValidationTests.cs) — regex
  allow-lists for SHA, branch, repo. Includes prompt-injection / shell-injection
  / path-traversal / CRLF / overlong-input cases.
- [DeploymentToolsSecurityTests](tests/DeploymentMcp.Tests/DeploymentToolsSecurityTests.cs) —
  every MCP tool rejects bad inputs **before** reaching the
  `IDeploymentService`. Includes the destructive `create_rollback_pr`.

Numbers at the time of writing: **62 tests, all green.**

## CI / CD

GitHub Actions runs three security gates on every PR
([.github/workflows/ci.yml](.github/workflows/ci.yml)):

1. **Build** every project in the solution.
2. **Test** both test projects (seed + new security tests).
3. **PSRule for Azure** scans `infra/*.bicep` against the WAF baseline and
   fails on errors.

A PR cannot merge if any of those fail.

## Reporting a vulnerability

This is a conference-demo repo. For real-world reports, fork the project and
follow the standard `SECURITY.md` template for your organisation.
