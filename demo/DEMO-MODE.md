# DemoMode=true — what it is and what it can do

> Companion to [MCP-INSPECTOR.md](MCP-INSPECTOR.md) and the dry-run plan below.

## TL;DR

The deployed Function App runs with the app setting **`DemoMode=true`**. In this
mode the MCP server returns **deterministic, canned data** from
[`FakeDeploymentService`](../src/DeploymentMcp/Services/FakeDeploymentService.cs)
instead of calling Azure DevOps. Everything in the demo works end-to-end
without a real AzDO project, repo, pipeline, or managed-identity permissions.

## How the switch works

[`src/DeploymentMcp/Program.cs`](../src/DeploymentMcp/Program.cs) reads the
`DemoMode` setting and wires DI accordingly:

| DemoMode | `IDeploymentService` impl | Needs AzDO? | Needs MI→AzDO perms? |
|----------|---------------------------|-------------|----------------------|
| `true`   | `FakeDeploymentService`   | No          | No                   |
| `false`  | `AzureDevOpsClient`       | Yes         | Yes (build read, code write) |

Option validation for `AzureDevOpsOptions` is **skipped** in DemoMode, so the
app starts even with no AzDO config at all.

## What works in DemoMode=true

All three MCP tools defined in
[`Tools/DeploymentTools.cs`](../src/DeploymentMcp/Tools/DeploymentTools.cs) are
fully functional via canned data:

| Tool | Demo behaviour |
|------|----------------|
| `get_recent_deployments` | Returns N fake pipeline runs — last 3 fail on `main`, prior 2 succeed on `lastGoodSha` |
| `diagnose_deployment` | Returns a realistic failure summary (stage, error, suspect commits) |
| `create_rollback_pr` | Returns a fake PR URL pointing at the AzDO project |

Cached reference payloads live in [`cached-responses/`](cached-responses/) and
match what `FakeDeploymentService` produces, so they double as a recording-day
fallback.

## What the demo path looks like

```
User prompt
  → DevOpsAgent (Microsoft Agent Framework)
    → Foundry hosted MCP tool (ResponseTool.CreateMcpTool)
      → Function App MCP endpoint  (SSE + JSON-RPC, system-key auth)
        → DeploymentTools class    (MCP triggers)
          → FakeDeploymentService  ← DemoMode=true stops here
                                   ← (DemoMode=false would call AzureDevOpsClient → AzDO REST)
```

Every hop is real **except** the final AzDO call. That makes the demo:
- ✅ deterministic on stage (no flaky network / no rate limits)
- ✅ safe to record offline
- ✅ free of secrets in screenshots
- ✅ identical wire protocol to production

## Visual backdrop (Phase D, optional)

The AzDO project `msbuild2026eshopdemo` is **only** for ALT-Tabbing to during
the talk so the audience sees realistic-looking pipeline runs, commits, and a
PR. The Function App never queries it. Seeded via
[`demo/shop-api-seed/seed-azdo.ps1`](shop-api-seed/seed-azdo.ps1).

## When you would flip DemoMode=false

Not for this talk. It would require:
1. Granting the Function's user-assigned MI access to the AzDO org
2. Implementing the log-scrape inside `AzureDevOpsClient.DiagnoseAsync`
3. Real pipeline runs with parseable failures

All explicitly out of scope per the dry-run plan.

---

# Project planning summary (as of 2026-05-19)

## Talk
- **Title**: From Code to Agents: Build Production MCP Servers on Azure Functions
- **Length**: 20 min, Microsoft Build 2026
- **Presenter**: Jonah Andersson
- **Repo / branch**: this repo, branch `step-3-agent-azdo`

## Architecture (deployed)
- **Region**: Sweden Central — **env**: `jonahdemo-dryrun1` — **RG**: `rg-jonahdemo-dryrun1`
- **Function App**: `func-mcpdemork5lpkrhjgtl6` (Flex Consumption, dotnet-isolated, .NET 10)
- **MCP extension**: `Microsoft.Azure.Functions.Worker.Extensions.Mcp` 1.0.0 (SSE + JSON-RPC)
- **Identity**: user-assigned MI `id-mcpdemork5lpkrhjgtl6` — no PATs, no secrets
- **Foundry**: account `aif-mcpdemork5lpkrhjgtl6`, project `proj-mcpdemork5lpkrhjgtl6`, model `gpt-4.1-mini`
- **Agent**: `DevOpsAgent` (Microsoft Agent Framework) calls Foundry → hosted MCP tool → Function

## Decisions locked in
- ✅ Keep `DemoMode=true` end-to-end (choice **b**)
- ✅ AzDO project name **`msbuild2026eshopdemo`** (already created in portal)
- ✅ Inner repo stays **`shop-api`** (matches all scripts and code)
- ✅ Pipeline name **`shop-api-CI`** (seed default)
- ✅ AzDO org: https://dev.azure.com/jonahanderssonazuredemos/

## Phase plan & status

| Phase | What | Status |
|-------|------|--------|
| A | Local fake-mode smoke (tooling, Azurite, build) | ✅ Tooling verified — local `func start` blocked by known .NET 10 issue, workaround = deployed Function |
| B1a | SSE probe deployed MCP endpoint | ✅ Pass (HTTP 200, `text/event-stream`, `event: endpoint`) |
| B1b | MCP Inspector — list + invoke 3 tools | 🟡 In progress (Inspector running locally) |
| B2 | DevOpsAgent in REMOTE mode → deployed Function | ⏳ Next |
| B3 | Tail Function logs during B2, confirm 3 tool calls | ⏳ |
| D1 | Verify AzDO org Entra connection + project exists | ⏳ |
| D2 | Run `seed-azdo.ps1 -ProjectName msbuild2026eshopdemo` | ⏳ |
| D3 | Manually trigger pipeline 3× on `main` (fail) + 2× on `lastGoodSha` (pass) | ⏳ |
| G1 | Beat-by-beat rehearsal, target < 18 min | ⏳ |
| G2 | Record `demo/recording.mp4` as fallback | ⏳ |

## Explicitly NOT doing
- ❌ `azd up` / `azd deploy` (nothing changed in infra/code)
- ❌ Flip `DemoMode=false` on Function App
- ❌ Add MI as AzDO user / grant repo perms
- ❌ Implement real `DiagnoseAsync` log-scrape
- ❌ Fix `ASPIRE002` warning (fallback: `dotnet run --project src/DevOpsAgent` directly)

## Known blockers — do not retry
1. **Local `func start`** fails on .NET 10 + Core Tools 4.10 + MCP extension 1.0
   (`System.Threading.Channels 8.0.0.0` load error). Documented in
   [`src/DeploymentMcp/DeploymentMcp.csproj`](../src/DeploymentMcp/DeploymentMcp.csproj).
   Workaround: use deployed Function via Inspector / REMOTE-mode agent.
2. **AppHost `ASPIRE002`** — missing `Aspire.Hosting.AppHost` package ref.
   May break `dotnet run --project src/AppHost`. Fallback: run agent directly.

## PowerShell gotcha (recorded)
PS 7's ternary parser eats `?` in interpolated URL strings. Always build URLs
with format strings:

```powershell
$sseUrl = '{0}/sse?code={1}' -f $base, $key   # ✅
$sseUrl = "$base/sse?code=$key"                # ❌ silently drops the ?
```
