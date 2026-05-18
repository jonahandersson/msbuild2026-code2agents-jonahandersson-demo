# Dry-Run Checklist

End-to-end rehearsal sequence for the **Build 2026 — From Code to Agents** talk.
Run top to bottom. Each beat has a copy-pasteable command block and the exact output to look for. If a beat doesn't match, stop and fix before moving on.

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

# One-time per machine: set Foundry endpoint + model in user-secrets
dotnet user-secrets --project src\AppHost set "Parameters:foundry-endpoint" "https://<your-foundry>.services.ai.azure.com/api/projects/<project>"
dotnet user-secrets --project src\AppHost set "Parameters:foundry-model"    "gpt-4o-mini"

# Pre-warm the agent so the first prompt isn't cold
dotnet run --project src\DevOpsAgent -- "ping"

# Boot everything
dotnet run --project src\AppHost
```

**Expect:** Aspire dashboard opens at `https://localhost:17081`. Both `mcp-server` and `devops-agent` go green. Tail logs in dashboard.

**Prompt to send** (in the agent terminal, when ready):

> *The latest deployment of shop-api to main is failing. Investigate and roll back if needed.*

**Expect three tool calls** in this order, visible in the Aspire traces view:

1. `get_recent_deployments`
2. `diagnose_deployment`
3. `create_rollback_pr` → returns PR URL

If `DemoMode=true` (default for local), the PR URL is fake. If you've wired AzDO, check the real PR list.

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

**Expect:** provision + deploy both green; output shows `MCP_ENDPOINT = https://<func>.azurewebsites.net/runtime/webhooks/mcp`. Note: first cold start may need one `az functionapp restart` for Key Vault refs to resolve (known issue, documented in user memory).

**Smoke test the deployed Function:**

```powershell
$endpoint = azd env get-values | Select-String '^MCP_ENDPOINT' | ForEach-Object { ($_ -split '=')[1].Trim('"') }
curl "$endpoint/tools/list"
curl -X POST "$endpoint/tools/call" -H "Content-Type: application/json" `
  -d '{ "name": "get_recent_deployments", "arguments": { "repo": "shop-api", "branch": "main" } }'
```

**Expect:** same 3-tool list as local; the call returns fake or real data depending on `DemoMode` setting.

---

### Beat 6 — Point the local agent at the deployed MCP

```powershell
$env:MCP_SERVER_URL          = "$endpoint"
$env:FOUNDRY_PROJECT_ENDPOINT = "https://<your-foundry>.services.ai.azure.com/api/projects/<project>"
$env:FOUNDRY_MODEL            = "gpt-4o-mini"
dotnet run --project src\DevOpsAgent
```

Send the same failing-deployment prompt. **Expect:** same 3 tool calls, but now traces appear in the **deployed** App Insights, not local. Open the workspace and run the queries in [demo/observability/KQL-QUERIES.md](demo/observability/KQL-QUERIES.md) — every query should return ≥1 row.

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
| 4 — `azd provision --preview` | ☐ |  |
| 5 — `azd up` | ☐ |  |
| 6 — agent vs deployed MCP + KQL | ☐ |  |
| 7 — GitHub Actions OIDC deploy | ☐ |  |

**Stage-ready criterion:** all 8 boxes ticked in a single rehearsal, end-to-end under 18 minutes.
