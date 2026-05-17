# From Code to Agents: Build Production MCP Servers on Azure Functions

> **Microsoft Build 2026 — 20-minute demo talk**
> Speaker: Jonah Andersson
> Stack: C# / .NET 10 · Azure Functions (isolated worker) · MCP Extension · Microsoft Agent Framework 1.0 · Microsoft Foundry · Azure DevOps

This repo is the live-demo companion for the talk. It walks from *"a Function with one
attribute"* to *"an agent that diagnoses a failed deployment and opens a rollback PR in
Azure DevOps,"* using the same patterns you'd ship in production.

---

## Why this talk

Agents are stuck in pilot for one reason: they can't talk to your tools without fragile,
custom-built integrations. **MCP (Model Context Protocol)** gives agents a standard way to
discover and invoke tools at runtime. Azure Functions, with the **MCP Extension** (GA at
Ignite 2025), is the right host for production MCP servers:

- Scale to zero, scale to thousands.
- Entra-backed auth at the `/runtime/webhooks/mcp` endpoint.
- Your existing CI/CD and observability story already works.
- Managed Identity end-to-end — no PATs, no connection strings in code.

---

## The scenario

A production deployment of `shop-api` to `main` is failing. The DevOps agent:

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

The MCP server has a `FakeDeploymentService` so you can run the whole demo against
in-memory data — useful for rehearsing and for the Wi-Fi fallback.

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

## Demo-day setup (with real Azure DevOps)

If you don't have an Azure DevOps project yet, follow
**[demo/shop-api-seed/SETUP-AZDO.md](./demo/shop-api-seed/SETUP-AZDO.md)** —
it's the full 2-3 hour walkthrough that gets you from "no AzDO" to "agent
creates real rollback PRs."

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

The Azure DevOps role assignment is a manual step because it spans services — see
[STEPS.md](./STEPS.md) for the one-liner.

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
│   ├── shop-api-seed/     The Azure DevOps target repo + setup script
│   ├── observability/     KQL queries for the App Insights demo moment
│   ├── cached-responses/  Wi-Fi fallback payloads
│   └── DEMO-SCRIPT.md     Beat-by-beat speaker notes with timing
├── slides/                Slide notes (cobalt blue brand)
├── azure.yaml             azd entry point
├── Directory.Packages.props  Central package management
├── global.json            Pins SDK version
└── STEPS.md               How to branch this into step-by-step demo branches
```

---

## ⚠️ Two things to verify before stage

The MCP Extension for Functions and Microsoft Agent Framework 1.0 are evolving fast.
Before your talk, verify two things against the latest samples:

1. **The `[McpToolTrigger]` and `[McpToolProperty]` attribute signatures** in
   `Microsoft.Azure.Functions.Worker.Extensions.Mcp`.
   Sample: <https://github.com/Azure-Samples/remote-mcp-functions-dotnet>

2. **The Agent Framework MCP client class name and constructor** in
   `Microsoft.Agents.AI` / `Microsoft.Agents.AI.Mcp`.
   Sample: <https://github.com/microsoft/agent-framework>

The code in this repo follows the documented public API at time of writing. If a name
shifted between versions, the fix is usually a one-line rename — but you want to know
*before* you're on stage, not during.

---

## Fallback path (when Wi-Fi fights back)

Set `DEMO_MODE=cached` when running the agent. The MCP server's `FakeDeploymentService`
will serve canned responses from `demo/cached-responses/`. The agent's reasoning is still
real (the LLM call runs); the *data* is canned. The audience can't tell — and you finish
the demo on time.

Absolute last resort: a 90-second screen recording lives in `demo/recording.mp4` (you'll
create this yourself during rehearsal — see [DEMO-SCRIPT.md](./demo/DEMO-SCRIPT.md)).

---

## License

MIT — see [LICENSE](./LICENSE). Use it, fork it, present it. Just don't ship the
`FakeDeploymentService` to prod 😉
