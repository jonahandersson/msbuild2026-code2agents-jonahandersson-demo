
# Dry-Run Checklist (15-minute demo)

End-to-end rehearsal sequence for the **Build 2026 — From Code to Agents** talk. Target time: **15 minutes**.
Run top to bottom. Each beat has a copy-pasteable command block and the exact output to look for. If a beat doesn't match, stop and fix before moving on.

## Pre-checklist (before starting demo)

- [ ] All code committed, working tree clean
- [ ] Azurite running on ports 10000/10001/10002
- [ ] Azure resources deployed (Function App, ShopWeb, App Insights, etc.)
- [ ] AppHost user-secrets set (see Beat 3 for workaround)
- [ ] AzDO pipelines green (shop-web-CD succeeded, shop-api-CI failed by design)
- [ ] MCP endpoint and Foundry model set in azd env
- [ ] App Insights resource accessible in Azure Portal
- [ ] Demo browser tabs open: ShopWeb, AzDO PRs, AzDO Pipelines, Foundry, App Insights
- [ ] Terminals split: func start, dotnet run for agent

> **Repo state assumed:** `main` and all four `step-*` branches in sync with `origin`, tags `baseline-complete` / `step-0` … `step-3` present, `dotnet build -c Release` clean (verified `d168ff6`).

---

## Pre-flight (5 min, once per rehearsal day)

```powershell
cd c:\dev\build2026-mcp-azure-functions
git fetch origin --prune
git status -sb                                  # expect: clean, ## main...origin/main
dotnet --version                                # expect: 10.0.x (global.json `latestFeature` accepts 10.0.108+; 10.0.300 is fine)
func --version                                  # expect: 4.x
azd version                                     # expect: 1.x
az account show --query name -o tsv             # expect: your demo subscription

# Storage emulator is REQUIRED for `func start` (UseDevelopmentStorage=true). Verify:
Get-Command azurite                             # expect: installed (npm i -g azurite if not)
```

**Start Azurite before any `func start` beat** (leave it running for the whole rehearsal):

```powershell
mkdir $env:TEMP\azurite-dryrun -Force | Out-Null
Start-Process -WindowStyle Hidden azurite -ArgumentList '--silent','--location',"$env:TEMP\azurite-dryrun"
# verify
Get-NetTCPConnection -LocalPort 10000 -ErrorAction SilentlyContinue | Select-Object -First 1
```

If any of these miss, do not start the dry-run — fix tooling first.

> **Branch hygiene between beats:** `bin/output/.azurefunctions` carries extension DLLs across checkouts. Before `func start` on a different branch, run `Remove-Item -Recurse -Force src\DeploymentMcp\bin, src\DeploymentMcp\obj` or the host will load stale extensions from the previous branch and the demo narrative breaks.

---


## The 15-minute flow

| Beat | T+    | On screen                                         | One-liner                                                                                       |
|------|-------|---------------------------------------------------|-------------------------------------------------------------------------------------------------|
| 1    | 00:00 | **Title slide** (cobalt blue)                     | "Hi! Hej! Mabuhay! I'm Jonah. 15 minutes, zero slides after this one, lots of code."            |
| 2    | 00:15 | **The problem** slide                             | "Every team has agents in pilot. Almost none in production. Tools are why."                     |
| 3    | 01:00 | **Architecture** slide (cobalt blue diagram)      | "Function = MCP server. Agent = MCP client. The protocol is the contract."                      |
| 4    | 02:00 | `git checkout step-1-mcp-tool` → `DeploymentTools.cs` | "Three tools, three attributes. That's the whole MCP API."                                  |
| 5    | 04:00 | Run `func start` in terminal pane                 | "It's just an Azure Function. Same hosting, same CI/CD, same observability."                    |
| 6    | 05:00 | Open MCP Inspector against `/runtime/webhooks/mcp/sse` | "The extension exposed our tools as standard MCP. Any MCP-compatible agent can call them now."  |
| 7    | 07:30 | `git checkout step-2-production` → `AzureDevOpsClient.cs` | "Production hardening. Watch the diff."                                                  |
| 8    | 08:30 | Highlight `DefaultAzureCredential` + Bicep        | "No PAT. No connection string. Managed Identity, same code, dev and prod."                      |
| 9    | 10:00 | Show App Insights traces (live)                   | "Every tool call shows up here. That's a real audit trail for what your agents are doing. (Cloud Aspire dashboard is not deployed in this environment, so use App Insights for observability.)"      |
| 10   | 11:30 | `git checkout step-3-agent-azdo` → `DevOpsAgent/Program.cs` | "Now the *client* side. Microsoft Agent Framework, GA in April."                       |
| 11   | 13:00 | Run `dotnet run`, paste the rollback prompt       | "Watch the agent decide. I'm not telling it which tools to call."                               |
| 12   | 14:00 | Function logs in pane 2 light up tool by tool     | "get_recent_deployments... diagnose_deployment... create_rollback_pr..."                        |
| 13   | 15:00 | **Switch to Azure DevOps tab, refresh**           | *(pause for 3 seconds — let them see it)* "There's the PR. The reasoning is in the description."|
| 14   | 15:00 | Show the PR description and reviewers             | "Auditable. Reviewable. Your existing PR policies still apply. The agent didn't go rogue."      |
| 15   | 15:00 | **Takeaways slide** (cobalt blue)                 | Read the three takeaways verbatim.                                                              |
| 16   | 15:00 | **QR code** to this repo                          | "Repo's on the QR code. Come find me at the speaker lounge."                                    |

---

## Local track — code → MCP → agent (the live demo path)

### Beat 0 — Empty scaffold (`step-0-baseline`)

```powershell
git checkout step-0-baseline
Remove-Item -Recurse -Force src\DeploymentMcp\bin, src\DeploymentMcp\obj -ErrorAction SilentlyContinue
Get-ChildItem src\DeploymentMcp -File          # expect: 4 files (csproj, Program.cs, host.json, local.settings.sample.json) + local.settings.json if you created one
Get-Content src\DeploymentMcp\Program.cs       # expect: 13 lines, no MCP references
cd src\DeploymentMcp
func start
```

**Expect:** host logs `No job functions found` then `Job host started`. Only `Loaded extension 'Startup'` — **no** `Loaded extension 'Mcp'`. `GET http://localhost:7071/runtime/webhooks/mcp/tools/list` returns 404. Stop with `Ctrl+C`.

**Speaker line:** *"Stock Azure Function, 13 lines. Watch what one NuGet package gets you."*

---

### Beat 1 — Add MCP (`step-1-mcp-tool`)

```powershell
cd c:\dev\build2026-mcp-azure-functions
git checkout step-1-mcp-tool
Remove-Item -Recurse -Force src\DeploymentMcp\bin, src\DeploymentMcp\obj -ErrorAction SilentlyContinue
git diff step-0-baseline --stat                 # expect: csproj +1 pkg, host.json +extensions.mcp, Tools/ + Services/ added
cd src\DeploymentMcp
func start
```

**Verify in the host startup logs** (the v1.0.0 MCP transport is SSE+JSON-RPC, *not* REST — you can't curl `/tools/list`):

```text
        CreateRollbackPr:     mcpToolTrigger
        DiagnoseDeployment:   mcpToolTrigger
        GetRecentDeployments: mcpToolTrigger
```

For a live tools/list against the SSE surface, use MCP Inspector (recommended for stage):

```powershell
npx -y @modelcontextprotocol/inspector
# In the inspector UI, connect to http://localhost:7071/runtime/webhooks/mcp/sse
# Expect: 3 tools listed; invoke `get_recent_deployments` with {"repo":"shop-api","branch":"main"}
```

The deeper tool-call validation happens in **Beat 3** when the agent calls them end-to-end — if you're short on time, skip Inspector here and let the agent prove it.

Stop func (`Ctrl+C`).

---

### Beat 2 — Production posture (`step-2-production`)

```powershell
cd c:\dev\build2026-mcp-azure-functions
git checkout step-2-production
git diff step-1-mcp-tool --stat                 # expect: AzureDevOpsClient, resilience handler, App Insights, conditional options
dotnet build -c Release --nologo -v q | Select-String 'Error\(s\)'
```

**Expect:** `0 Error(s)`. Highlight `Program.cs` showing `DemoMode`-gated DI and `Polly`/`HttpClient` resilience handler.

(No `func start` here — same surface as beat 1; the value is the diff.)

---

### Beat 3 — Agent end-to-end (`step-3-agent-azdo`, via Aspire)

```powershell
cd c:\dev\build2026-mcp-azure-functions
git checkout step-3-agent-azdo

# One-time per machine: set Foundry endpoint + model in user-secrets.
# On .NET 10.0.300 the `dotnet user-secrets` CLI may fail with
# "Could not load the MSBuild project" against an Aspire AppHost — if that
# happens, write the secrets file directly (UserSecretsId = build2026-mcp-apphost):
#
#   $dir = "$env:APPDATA\Microsoft\UserSecrets\build2026-mcp-apphost"
#   New-Item -ItemType Directory -Force -Path $dir | Out-Null
#   @{
#     'Parameters:foundry-endpoint' = (azd env get-value FOUNDRY_PROJECT_ENDPOINT);
#     'Parameters:foundry-model'    = (azd env get-value FOUNDRY_MODEL)
#   } | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $dir 'secrets.json') -Encoding UTF8
#
# Or, when the CLI works:
dotnet user-secrets --project src\AppHost set "Parameters:foundry-endpoint" "https://<your-foundry>.services.ai.azure.com/api/projects/<project>"
dotnet user-secrets --project src\AppHost set "Parameters:foundry-model"    "gpt-4o-mini"

# Pre-warm the agent so the first prompt isn't cold
dotnet run --project src\DevOpsAgent -- "ping"

# Boot everything
dotnet run --project src\AppHost
```


**Expect:** Aspire dashboard opens at `https://localhost:17081`. Both `mcp-server` and `devops-agent` go green. Tail logs in dashboard.

**Optional:** For production-style observability, open the deployed App Insights resource in Azure Portal (Tab E in the checklist). Use Live Metrics and Logs (KQL) to view traces from the deployed MCP server and agent. See [demo/observability/KQL-QUERIES.md](demo/observability/KQL-QUERIES.md) for ready-to-run queries.
**Prompt to send** (in the agent terminal, when ready):

> *The latest deployment of shop-api to main is failing. Investigate and roll back if needed.*


**Expect three tool calls** in this order, visible in the Aspire dashboard traces (local) and in App Insights (cloud):

1. `get_recent_deployments`
2. `diagnose_deployment`
3. `create_rollback_pr` → returns PR URL

If `DemoMode=true` (default for local), the PR URL is fake. If you've wired AzDO, check the real PR list.

---

## Beat 3b — Live rollback simulation (end-to-end, real AzDO)


This is the **rehearsal scenario** you run when you want to prove the full loop with a real failure that you just caused yourself. The agent is **reactive, not autonomous** — it does *not* poll AzDO or subscribe to build events. You trigger it by typing the prompt. (If you want autonomous detection, add an AzDO Service Hook → HTTP-trigger Function → call agent, or an App Insights alert webhook. Out of scope for the talk.)

**Observability:**
- For local runs, use the Aspire dashboard at `https://localhost:17081` to view traces.
- For cloud runs, open the App Insights resource in Azure Portal. Use Live Metrics and Logs (KQL) to verify agent and tool activity. See [demo/observability/KQL-QUERIES.md](demo/observability/KQL-QUERIES.md).

**State assumed before this beat:**

- ShopWeb returns 200 (`(Invoke-WebRequest (azd env get-value SHOP_WEB_URL)).StatusCode`)
- `shop-web-CD` AzDO pipeline last run = `succeeded`
- Function App MCP endpoint responds (`(Invoke-WebRequest "$(azd env get-value MCP_ENDPOINT)/sse?code=$mcpKey").StatusCode` → 200 over SSE)
- No active rollback PRs from previous rehearsals (abandon them first — see Cleanup at end of this beat)

### Step 1 — Reset to a clean baseline

```powershell
$org='jonahanderssonazuredemos'; $proj='msbuild2026eshopdemo'
$repoId='5f86c1fb-4553-4a61-90c8-4a227b87f857'
$tok = az account get-access-token --resource 499b84ac-1321-427f-aa17-267ca6975798 --query accessToken -o tsv
$h=@{Authorization="Bearer $tok"}
# Abandon stale rollback PRs from prior rehearsals
$prs = (Invoke-RestMethod -Headers $h -Uri "https://dev.azure.com/$org/$proj/_apis/git/repositories/$repoId/pullrequests?searchCriteria.status=active&api-version=7.1").value
foreach($pr in $prs | Where-Object { $_.title -like 'Rollback*' }) {
    Invoke-RestMethod -Method PATCH -Headers (@{Authorization="Bearer $tok";'Content-Type'='application/json'}) `
      -Uri "https://dev.azure.com/$org/$proj/_apis/git/repositories/$repoId/pullrequests/$($pr.pullRequestId)?api-version=7.1" `
      -Body (@{status='abandoned'}|ConvertTo-Json) | Out-Null
    Write-Host "Abandoned PR #$($pr.pullRequestId)"
}
```

### Step 2 — "Push a bug" (simulate a bad deploy)

Introduce a one-line, build-breaking change on `main` in the AzDO `shop-api` repo. This is a *real* push that triggers `shop-api-CI` and makes it fail — the agent will diagnose this exact build.

```powershell
# Read current Program.cs from AzDO and inject a syntax error
$tip = (Invoke-RestMethod -Headers $h -Uri "https://dev.azure.com/$org/$proj/_apis/git/repositories/$repoId/refs?filter=heads/main&api-version=7.1").value[0].objectId
$content = (Invoke-RestMethod -Headers $h -Uri "https://dev.azure.com/$org/$proj/_apis/git/repositories/$repoId/items?path=/src/ShopApi/Program.cs&versionDescriptor.version=main&api-version=7.1")
$broken  = $content + "`n// !! intentional bug for dry-run`nthis_will_not_compile;"
$body = @{
  refUpdates=@(@{name='refs/heads/main'; oldObjectId=$tip})
  commits=@(@{ comment='chore: simulate prod bug for dry-run'; changes=@(@{
    changeType='edit'; item=@{path='/src/ShopApi/Program.cs'}
    newContent=@{content=$broken; contentType='rawtext'} }) })
} | ConvertTo-Json -Depth 10
$push = Invoke-RestMethod -Method POST -Headers (@{Authorization="Bearer $tok";'Content-Type'='application/json'}) `
  -Uri "https://dev.azure.com/$org/$proj/_apis/git/repositories/$repoId/pushes?api-version=7.1" -Body $body
Write-Host "Pushed bad commit: $($push.commits[0].commitId.Substring(0,8))"
# Wait ~60s for shop-api-CI to fail
```

### Step 3 — Run the agent (prompt-driven)

```powershell
$env:MCP_SERVER_URL = (azd env get-value MCP_ENDPOINT)   # already includes ?code=...
dotnet run --project src\AppHost
# In the agent terminal:
#   The latest deployment of shop-api to main is failing. Investigate and roll back if needed.
```

**Expect — visible in the Aspire dashboard traces:**

1. `get_recent_deployments(repo='shop-api', branch='main')` → returns the freshly-failed build
2. `diagnose_deployment(deploymentId=<id>)` → returns `last_known_good_commit_sha`
3. `create_rollback_pr(repo='shop-api', targetCommit=<lkg>, reason=<agent's reasoning>)` → returns AzDO PR URL

### Step 4 — Verify

- Open the PR URL the agent printed. Confirm: source = `rollback/auto-<sha>`, target = `main`, body contains the agent's reasoning, created by `id-mcpdemork5lpkrhjgtl6`.
- Confirm ShopWeb still 200 — the storefront is on a separate pipeline (`shop-web-CD`) so the shop-api bug doesn't take it down.

### Step 5 — Cleanup (after rehearsal)

```powershell
# Abandon the agent's PR and revert main back to the last-known-good
# (Step 1's loop above does this automatically on the *next* run)
```

> **Q: Can the agent do this without me typing a prompt?**
> Not out of the box. The MCP server only exposes the *tools*; nothing triggers the agent. Add one of these for autonomous mode: (a) AzDO **Service Hook** on `Build completed → result=failed` posting to a new HTTP-triggered Function that calls `agent.RunAsync(...)`; (b) a **TimerTrigger** Function that polls `get_recent_deployments` and invokes the agent when it sees a failure; (c) an **App Insights alert** webhook on a `requests | failed` rule. All three are 30–50 lines of code on top of what's already here.

---

## Deployment track — Azure (run only when local track is green)

### Beat 4 — Bicep preview against a throwaway env

```powershell
cd c:\dev\build2026-mcp-azure-functions
$env:AZURE_ENV_NAME    = "dryrun-$(Get-Random -Maximum 9999)"
$env:AZURE_LOCATION    = "swedencentral"
$env:AZURE_NAME_PREFIX = "mcpdry"
azd provision --preview
```

**Expect:** what-if output lists 6–8 resources to create (Function App, plan, storage, MI, AI workspace, App Insights, role assignment) and **0 deletes / 0 modifies**. No `<your-org>` / `<your-project>` placeholders in the diff.

---

### Beat 5 — `azd up` into a fresh RG

```powershell
azd up --no-prompt
```

**Expect:** provision + deploy both green; outputs show `MCP_ENDPOINT`, `SHOP_WEB_URL`, `ASPIRE_DASHBOARD_URL`. Note: first cold start may need one `az functionapp restart` for Key Vault refs to resolve (known issue, documented in user memory).

**Smoke test the deployed Function (MCP transport is SSE + JSON-RPC — there is no REST `/tools/list`):**

```powershell
$rg   = azd env get-value AZURE_RESOURCE_GROUP
$func = azd env get-value FUNCTION_APP_NAME           # or read MCP_ENDPOINT and parse
$mcpKey = az functionapp keys list -g $rg -n $func --query systemKeys.mcp_extension -o tsv
$endpoint = azd env get-value MCP_ENDPOINT
# 1) SSE stream opens (200, text/event-stream) — Ctrl+C after a few seconds:
curl.exe -N "$endpoint/sse?code=$mcpKey"
# 2) Full tools/list + tool call via MCP Inspector:
npx -y @modelcontextprotocol/inspector
#    Connect to: $endpoint/sse?code=$mcpKey  → expect 3 tools, invoke get_recent_deployments
```

**Expect:** same 3-tool list as local; the call returns fake or real data depending on `DemoMode` setting.

**Smoke test the deployed ShopWeb storefront** (deployed via the `shop-web-CD` AzDO pipeline on each push to `src/ShopWeb/*` on `main`):

```powershell
$shopWeb = azd env get-value SHOP_WEB_URL
(Invoke-WebRequest -UseBasicParsing $shopWeb -TimeoutSec 30).StatusCode    # expect 200
```

---

### Beat 6 — Point the local agent at the deployed MCP

```powershell
$env:MCP_SERVER_URL          = "$endpoint"
$env:FOUNDRY_PROJECT_ENDPOINT = "https://<your-foundry>.services.ai.azure.com/api/projects/<project>"
$env:FOUNDRY_MODEL            = "gpt-4o-mini"
dotnet run --project src\DevOpsAgent
```


Send the same failing-deployment prompt. **Expect:** same 3 tool calls, but now traces appear in the **deployed** App Insights, not local. Open the workspace and run the queries in [demo/observability/KQL-QUERIES.md](demo/observability/KQL-QUERIES.md) — every query should return ≥1 row. The cloud Aspire dashboard step is skipped (not deployed).

---

### Beat 7 — CI / OIDC deploy from GitHub

Either:

- **Real run:** push a no-op commit to `main` → watch [.github/workflows/deploy.yml](.github/workflows/deploy.yml) run. Expect `azure/login@v2` step to succeed without a client secret (federation), `azd up --no-prompt` to be idempotent against the existing RG.
- **Local dry-run with [act](https://github.com/nektos/act):** `act -j build` validates the `ci.yml` matrix without touching Azure.

---

## Tear-down (after rehearsal)

```powershell
azd down --purge --force                        # removes the throwaway RG
git checkout main                               # leave the worktree on main
```

Keep the AzDO project around between rehearsals — re-seeding takes ~20 min.

---

## Pass/fail summary card

Fill this in after each rehearsal to track stability:

| Beat | Pass? | Notes |
|------|-------|-------|
| Pre-flight | ☐ |  |
| 0 — empty scaffold | ☐ |  |
| 1 — MCP tools list + call | ☐ |  |
| 2 — production build | ☐ |  |
| 3 — agent local E2E | ☐ |  |
| 3b — live rollback simulation (real bad push → agent → real PR) | ☐ | Step 1 cleanup abandons stale PRs |
| 4 — `azd provision --preview` | ☐ |  |
| 5 — `azd up` | ☐ |  |
| 6 — agent vs deployed MCP + KQL | ☐ |  |
| 7 — GitHub Actions OIDC deploy | ☐ |  |
| 7b — AzDO `shop-web-CD` pipeline green | ☐ | First run requires authorizing the SC (`sc-azure-shopweb`) AND the `shop-web-production` environment for pipeline 2 |

**Stage-ready criterion:** all boxes ticked in a single rehearsal, end-to-end under 18 minutes.
