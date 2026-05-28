# From Code to Agents: Build Production MCP Servers on Azure Functions

> Stack: C# / .NET 10 · Azure Functions (isolated worker) · MCP Extension · Microsoft Agent Framework 1.0 · Microsoft Foundry · Azure DevOps

Production-grade reference for hosting a **Model Context Protocol (MCP) server** on Azure
Functions and driving it from an agent built with the **Microsoft Agent Framework** on
**Microsoft Foundry**. Goes end-to-end from `[McpToolTrigger]` attribute on a Function
to an agent that diagnoses a failed deployment and opens a rollback PR in Azure DevOps —
using the same patterns you'd ship in production.

---

## Why MCP on Azure Functions

Agents need a standard way to discover and invoke tools at runtime.
**MCP (Model Context Protocol)** is that standard. Azure Functions, with the
**MCP Extension** (GA at Ignite 2025), is a strong host for production MCP servers:

- Scale to zero, scale to thousands.
- Entra-backed auth at the `/runtime/webhooks/mcp` endpoint.
- Existing CI/CD and observability stories already work.
- Managed Identity end-to-end — no PATs, no connection strings in code.

---

## What it does

The sample scenario: a deployment of `shop-api` to `main` is failing. The DevOps agent:

1. Lists the most recent deployments for the repo and branch.
2. Diagnoses the latest failed deployment.
3. Identifies the last known-good commit.
4. **Creates a rollback PR in Azure DevOps**, with the agent's reasoning in the description.

The MCP server (this repo's Function app) exposes three tools:

| Tool name                  | What it does                                                  |
|----------------------------|---------------------------------------------------------------|
| `get_recent_deployments`   | Lists recent builds for a repo + branch                       |
| `diagnose_deployment`      | Returns likely root cause + the last known-good commit        |
| `create_rollback_pr`       | Creates a PR rolling back to a target commit, with a reason   |

---

## Prerequisites

| Thing                  | Version / notes                                              |
|------------------------|--------------------------------------------------------------|
| .NET SDK               | **10.0** (preview). Falls back to 9.0 — see "Fallback" below.|
| Azure Functions Core Tools | v4.x                                                    |
| Azure CLI              | 2.60+                                                        |
| Azure subscription     | Contributor on a resource group                              |
| Microsoft Foundry project | With a model deployment (e.g. `gpt-5.4-mini` or `gpt-4o-mini`) |
| Azure DevOps org       | With a project, a repo, and at least one build pipeline      |
| GitHub                 | For repo + Actions (OIDC federated to Azure)                 |

> **Devcontainer is recommended.** Open this repo in a GitHub Codespace and everything
> above is pre-installed except the cloud accounts.

---

## Quick start (local, no cloud needed)

The MCP server has a `FakeDeploymentService` so you can run the whole flow against
in-memory data — useful for local development without Azure DevOps access.

**Option A — .NET Aspire (recommended for local dev):**

```bash
dotnet user-secrets --project src/AppHost set "Parameters:foundry-endpoint" \
  "https://<your-foundry>.services.ai.azure.com/api/projects/<project>"

dotnet run --project src/AppHost
```

Opens the Aspire dashboard at <https://localhost:17081> with the MCP server,
the agent, and live traces/logs/metrics flowing through both.

**Option B — Manual (two terminals, no Aspire):**

```bash
# Terminal 1 — MCP server
cd src/DeploymentMcp
cp local.settings.sample.json local.settings.json
func start

# Terminal 2 — Agent
cd src/DevOpsAgent
export FOUNDRY_PROJECT_ENDPOINT="https://<your-foundry>.services.ai.azure.com/api/projects/<project>"
export FOUNDRY_MODEL="gpt-4o-mini"
export MCP_SERVER_URL="http://localhost:7071/runtime/webhooks/mcp"
dotnet run
```

Try the prompt: *"The latest deployment of shop-api to main is failing. Investigate and roll back if needed."*

---

## Connecting to a real Azure DevOps project

If you don't have an Azure DevOps project yet, follow
**[demo/shop-api-seed/SETUP-AZDO.md](./demo/shop-api-seed/SETUP-AZDO.md)** —
the full walkthrough that gets you from "no AzDO" to "agent creates real rollback PRs."

Short version once you have an org + project + repo:

1. Set `DemoMode = false` in `local.settings.json` (or in App Settings on Azure).
2. Configure `AzureDevOps:OrgUrl` and `AzureDevOps:Project`.
3. Add the Function App's Managed Identity as a user in Azure DevOps with
   Project Contributors permissions (see Step 5 of SETUP-AZDO.md).

---

## Deploying to Azure

```bash
azd auth login
azd up
```

`infra/main.bicep` provisions:

- Function App on **Flex Consumption** plan
- User-assigned Managed Identity
- Storage account (for the Function runtime)
- Application Insights + Log Analytics workspace
- Role assignment so the Function can read your Foundry project

The Azure DevOps role assignment is a manual step because it spans services. See
[demo/shop-api-seed/SETUP-AZDO.md](./demo/shop-api-seed/SETUP-AZDO.md) for the
required AzDO scopes on the Function App's Managed Identity.

### Deploying from GitHub Actions (OIDC federation)

The [.github/workflows/deploy.yml](./.github/workflows/deploy.yml) workflow runs
`azd up` on every push to `main`. It authenticates to Azure with **OpenID
Connect federation** — no client secrets stored in GitHub, no PATs.

**One-time setup:**

1. **Create an Entra app registration with a federated credential.**
   The fastest path is `azd pipeline config` from a local clone:

   ```bash
   azd auth login
   azd pipeline config --provider github
   ```

   This creates the app registration, sets up federated credentials for the
   `main` branch + pull requests, grants `Contributor` + `User Access
   Administrator` on the target subscription, and writes the GitHub secrets
   below for you. If you prefer to do it by hand, the equivalent `az` flow is
   in the [azd docs](https://learn.microsoft.com/azure/developer/azure-developer-cli/configure-devops-pipeline).

2. **Confirm the GitHub secrets and variables exist** under
   *Settings → Secrets and variables → Actions*:

   | Scope    | Name                     | Example                                |
   |----------|--------------------------|----------------------------------------|
   | Secret   | `AZURE_CLIENT_ID`        | GUID of the app registration           |
   | Secret   | `AZURE_TENANT_ID`        | Your Entra tenant GUID                 |
   | Secret   | `AZURE_SUBSCRIPTION_ID`  | Target subscription GUID               |
   | Variable | `AZURE_ENV_NAME`         | `demo`                                 |
   | Variable | `AZURE_LOCATION`         | `swedencentral`                        |
   | Variable | `AZURE_NAME_PREFIX`      | `mcpdemo` (3–11 lowercase chars)       |

3. **Verify the federated credential subject** matches the workflow trigger.
   For pushes to `main`, the subject should be
   `repo:<owner>/<repo>:ref:refs/heads/main`. Mismatch is the #1 cause of
   `AADSTS70021` errors in the `azure/login` step.

No Azure DevOps PAT is ever stored — the Function App's *runtime* identity
(a user-assigned Managed Identity provisioned by Bicep) authenticates to
Azure DevOps separately. See [SETUP-AZDO.md](./demo/shop-api-seed/SETUP-AZDO.md)
for the required AzDO scopes on that identity.

---

## Repo walkthrough

```
build2026-mcp-azure-functions/
├── .devcontainer/         Codespace config (one-click dev env)
├── .github/workflows/     CI + OIDC-federated deploy
├── infra/                 Bicep IaC (azd-ready)
├── src/
│   ├── AppHost/           .NET Aspire orchestrator (local one-command startup)
│   ├── ServiceDefaults/   Shared OTel + App Insights + service discovery
│   ├── DeploymentMcp/     Azure Function = MCP server (the headliner)
│   └── DevOpsAgent/       Console app = MCP client (Microsoft Agent Framework)
├── demo/
│   ├── shop-api-seed/     Azure DevOps target repo + setup script
│   ├── observability/     KQL queries for the App Insights views
│   └── cached-responses/  Optional canned payloads for offline runs
├── azure.yaml             azd entry point
├── Directory.Packages.props  Central package management
└── global.json            Pins SDK version
```

---

## Verifying the SDK surface

The MCP Extension for Functions and Microsoft Agent Framework 1.0 are evolving fast.
When upgrading, verify two things against the latest samples:

1. **The `[McpToolTrigger]` and `[McpToolProperty]` attribute signatures** in
   `Microsoft.Azure.Functions.Worker.Extensions.Mcp`.
   Sample: <https://github.com/Azure-Samples/remote-mcp-functions-dotnet>

2. **The Agent Framework MCP client class name and constructor** in
   `Microsoft.Agents.AI` / `Microsoft.Agents.AI.Mcp`.
   Sample: <https://github.com/microsoft/agent-framework>

The code in this repo follows the documented public API at time of writing. If a name
shifted between versions, the fix is usually a one-line rename.
