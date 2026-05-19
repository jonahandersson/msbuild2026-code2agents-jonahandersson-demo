# Build 2026 — 15-Minute Live Code Demo: Dry-Run Checklist

**Demo:** Code-to-Agents with MCP on Azure Functions
**Owner:** Jonah Andersson
**Target runtime:** 15:00 (±1:30)
**Print this page and tick boxes as you go.**

---

## 1. Pre-flight setup (do 5 min before going on stage)

- [ ] Laptop on power, Wi-Fi confirmed, hotspot ready as backup
- [ ] Display mirroring tested at 1080p+; VS Code zoom level 2 (Ctrl+`=` twice)
- [ ] Notifications silenced (Focus Assist ON), Slack/Teams/email closed
- [ ] Terminal font ≥ 16pt, theme high-contrast
- [ ] Timer visible on phone, set to 15:00 countdown
- [ ] Bottle of water within reach

### Pre-warm services
- [ ] **Foundry agent** pre-warmed once: `cd src/DevOpsAgent; dotnet run -- "ping"` then Ctrl-C
- [ ] **Storefront** — deployed on App Service (`azd env get-value SHOP_WEB_URL`) returns 200. Local fallback: `dotnet run --project demo/shop-api-seed/src/ShopWeb/ShopWeb.csproj` → http://localhost:5050
- [ ] **AzDO `shop-web-CD` pipeline** last run = `succeeded` (deploys storefront on every `src/ShopWeb/*` push to `main`)
- [ ] **Azurite** running locally (needed by Functions host)
- [ ] **Function App** in Azure restarted once (avoid cold start): `az functionapp restart -g <rg> -n <app>`
- [ ] MCP endpoint key cached: `$mcpKey = az functionapp keys list -g <rg> -n <app> --query systemKeys.mcp_extension -o tsv`

### Browser tabs (open in this cmd-tab order)
- [ ] Tab A — Storefront (deployed: `SHOP_WEB_URL`, or http://localhost:5050)
- [ ] Tab B — AzDO PRs https://dev.azure.com/jonahanderssonazuredemos/msbuild2026eshopdemo/_git/shop-api/pullrequests
- [ ] Tab C — AzDO Pipelines (`shop-api-CI` last failed run pinned, `shop-web-CD` last succeeded run visible)
- [ ] Tab D — Foundry agent page (already authenticated)

- [ ] Tab E — App Insights live metrics (Azure Portal → App Insights resource → Live Metrics, Logs)

# (Cloud Aspire Dashboard step removed — not deployed in this environment)

### VS Code state
- [ ] Open at repo root `c:\dev\build2026-mcp-azure-functions`
- [ ] Terminal split into 2 panes (Pane 1 = `func start`, Pane 2 = `dotnet run` for agent)
- [ ] `DeploymentTools.cs`, `AzureDevOpsClient.cs`, `DevOpsAgent/Program.cs` pinned in editor

---

## 2. The 15-minute flow

| ✓ | T+ | Beat | Action | Verify |
|---|----|------|--------|--------|
| ☐ | 00:00 | 1. Title + problem | Speak 30 s, no slide change | Mic working |
| ☐ | 00:30 | 2. Show MCP tools | Open `DeploymentTools.cs`, point at 3 `[McpToolTrigger]` methods | Audience can read |
| ☐ | 01:30 | 3. Start MCP server | Pane 1: `func start` | 3 tool registrations log within 10 s |
| ☐ | 03:00 | 4. Verify SSE endpoint | `curl.exe -N http://localhost:7071/runtime/webhooks/mcp/sse` | 200, `text/event-stream` (Ctrl+C to close — no REST `/tools/list`) |
| ☐ | 04:00 | 5. **What's at stake** | Cmd-Tab A (storefront), 10 s only | Catalog visible |
| ☐ | 04:30 | 6. Auth story | Show `AzureDevOpsClient.cs` + `DefaultAzureCredential` | "No PAT, no secrets" |
| ☐ | 05:30 | 7. Observability | Tab E App Insights live metrics (skip Tab F cloud Aspire Dashboard), 30 s total | Recent traces visible in App Insights |
| ☐ | 06:30 | 8. Show agent code | `DevOpsAgent/Program.cs`, hosted-MCP block | Single screen |
| ☐ | 08:00 | 9. Run the agent | Pane 2: `dotnet run`, paste rollback prompt | Stream begins |
| ☐ | 09:30 | 10. Watch tools fire | Cmd-Tab to Pane 1 logs | `get_recent_deployments` → `diagnose_deployment` → `create_rollback_pr` |
| ☐ | 11:30 | 11. PR appears | Tab B, refresh | New PR with agent reasoning in description |
| ☐ | 12:30 | 12. Site survived | Tab A again | Storefront still loads |
| ☐ | 13:00 | 13. 3 takeaways | Speak from memory | Timer ≤ 14:00 |
| ☐ | 14:00 | 14. Buffer / Q&A | Breathe | Stop at 15:00 |

---

## 3. Pass/fail scorecard (after each run)

- [ ] Hit 15:00 ±1:30
- [ ] No "uh, let me find that…" moments
- [ ] All 3 MCP tools fired in correct order
- [ ] PR appeared in AzDO with non-empty reasoning text
- [ ] Storefront stayed responsive throughout
- [ ] You never apologized
- [ ] You looked at the script ≤ 2 times

**Score:** ___ / 7 — Ship-ready at **7/7 on a cold-boot run #3**.

---

## 4. Failure modes & on-stage recovery

| Symptom | Recovery |
|---------|----------|
| Foundry first call slow | Pre-warm twice in setup, not once |
| `func start` port clash | Storefront=5050, func=7071 — check `local.settings.json` |
| Agent runs, no PR appears | MCP server URL needs `?code=$mcpKey` — Foundry sends no headers |
| PR body empty | Prompt must include a *reason* — the tool param is required |
| Storefront slow first hit | Hit it once during setup |
| `az devops` CLI auth fails on stage | Use the AzDO web UI tab (B) only — never invoke CLI live |
| Function App cold start mid-demo | Restart Function App once during setup |

---

## 5. Rehearsal cadence

- [ ] **Run #1** — clock yourself, expect 18–20 min, mark dead air
- [ ] **Run #2** — same day, target ≤ 16 min, cut beats 5/7/12 if needed
- [ ] **Run #3** — cold-boot fresh terminal, target ≤ 15 min, all 7 checkboxes green

Stop rehearsing past run #3. Fresh delivery beats canned.

---

## 6. The on-stage cheat sheet (one-liners)

```pwsh
# Storefront (deployed)
start (azd env get-value SHOP_WEB_URL)

# Storefront (local fallback only)
dotnet run --project demo/shop-api-seed/src/ShopWeb/ShopWeb.csproj

# MCP server (Functions)
cd src/DeploymentMcp ; func start

# Verify MCP SSE (no REST `/tools/list` endpoint exists — v1.0.0 is SSE+JSON-RPC)
curl.exe -N http://localhost:7071/runtime/webhooks/mcp/sse

# Agent (Foundry → hosted MCP)
cd src/DevOpsAgent ; dotnet run

# Function App MCP key (for Foundry tool URL)
az functionapp keys list -g <rg> -n <app> --query systemKeys.mcp_extension -o tsv

# Restart Function App (avoid cold start)
az functionapp restart -g <rg> -n <app>

# Cloud Aspire Dashboard URL + browser token
azd env get-value ASPIRE_DASHBOARD_URL
az containerapp logs show -g <rg> -n ca-aspire-<token> --tail 50 ^| Select-String 'Login to the dashboard'
```

---

_Printable. Tick boxes during each rehearsal. Keep this page next to your laptop on stage._
