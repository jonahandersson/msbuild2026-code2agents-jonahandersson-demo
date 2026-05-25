# Architecture — Code-to-Agents with MCP on Azure Functions

> **Demo:** Microsoft Build 2026 — *From Code to Agents: Build Production MCP Servers on Azure Functions*
> **Story in one line:** Two agents talk over MCP. One reasons, one acts. Together they detect a broken deployment and ship a rollback PR — observed end-to-end in a single trace.

> 🎨 **Slide-ready PNG exports** (blue & white, 4800 × 3600) live in [docs/architecture/](docs/architecture/README.md) — drop them straight into PowerPoint.

---

## 1. System overview — the agent-to-agent topology

![System overview](docs/architecture/01-system-overview.png)

```mermaid
flowchart LR
    user["👤 SRE<br/>(natural language prompt)"]

    subgraph agent1["🧠 Agent #1 — Reasoning agent"]
        direction TB
        foundry["Microsoft Foundry<br/><i>DevOpsRollbackAgent</i><br/>declarative + hosted MCP"]
        model["gpt-4o-mini / 4.1-mini"]
        foundry --- model
    end

    subgraph agent2["🛠️ Agent #2 — Tool agent"]
        direction TB
        funcs["Azure Functions<br/><i>Flex Consumption, .NET 10 isolated</i>"]
        mcp["Worker.Extensions.Mcp v1.0.0<br/>SSE + JSON-RPC"]
        tools["DeploymentTools.cs<br/>• get_recent_deployments<br/>• diagnose_deployment<br/>• create_rollback_pr"]
        funcs --- mcp --- tools
    end

    azdo["Azure DevOps<br/><i>shop-api repo + pipelines</i>"]
    gate["CI gates<br/><i>GitHub Actions + AzDO Pipelines</i><br/>62 security tests + PSRule"]
    storefront["Storefront<br/><i>App Service</i>"]
    obs["Observability<br/><i>Aspire dashboard (local)<br/>App Insights (cloud)</i>"]

    user -->|"1. prompt"| agent1
    agent1 <-->|"2. MCP handshake<br/>(SSE /sse + POST /message)"| agent2
    agent2 -->|"3. Managed Identity<br/>(no PAT, no shared key)"| azdo
    azdo -->|"4. PR opened"| gate
    gate -->|"5. green ✓"| azdo
    azdo -.->|"merge → deploy"| storefront

    agent1 -.->|"OTel"| obs
    agent2 -.->|"OTel"| obs

    classDef agentBox fill:#e1f5fe,stroke:#0277bd,stroke-width:2px,color:#000
    classDef azureBox fill:#fff3e0,stroke:#ef6c00,stroke-width:1.5px,color:#000
    classDef obsBox fill:#f3e5f5,stroke:#6a1b9a,stroke-width:1.5px,color:#000
    class agent1,agent2 agentBox
    class azdo,gate,storefront azureBox
    class obs obsBox
```

### Reading the diagram

| Step | What happens | Where in the code |
|------|--------------|-------------------|
| 1 | User asks Agent #1 in plain English | [src/DevOpsAgent/Program.cs](src/DevOpsAgent/Program.cs) |
| 2 | Foundry opens SSE channel to MCP server, pulls tool list, reasons, calls tools | [Worker.Extensions.Mcp](https://github.com/Azure/azure-functions-mcp-extension) handles the protocol |
| 3 | Each tool uses `DefaultAzureCredential` → MI bearer token → AzDO REST | [src/DeploymentMcp/Services/AzureDevOpsClient.cs](src/DeploymentMcp/Services/AzureDevOpsClient.cs) |
| 4 | `create_rollback_pr` opens a real PR via AzDO Git API | [src/DeploymentMcp/Tools/DeploymentTools.cs](src/DeploymentMcp/Tools/DeploymentTools.cs) |
| 5 | CI workflow runs the 62-test security suite + PSRule on the PR | [.github/workflows/ci.yml](.github/workflows/ci.yml) |

---

## 2. Runtime — the live conversation (sequence)

![Runtime sequence](docs/architecture/02-runtime-sequence.png)

```mermaid
sequenceDiagram
    autonumber
    actor SRE
    participant Agent1 as 🧠 Agent #1<br/>(Foundry)
    participant MCP as 🛠️ Agent #2<br/>(MCP server)
    participant AzDO as Azure DevOps
    participant Gate as CI Gate

    SRE->>Agent1: "Latest deployment of shop-api<br/>is failing. Roll back if needed."
    Agent1->>MCP: GET /runtime/webhooks/mcp/sse<br/>(open stream)
    MCP-->>Agent1: event: endpoint<br/>tool list (3 tools)
    Note over Agent1: reason: which tool first?

    Agent1->>MCP: POST /message  get_recent_deployments<br/>(repo=shop-api, branch=main)
    MCP->>AzDO: GET /_apis/build/builds<br/>(MI bearer)
    AzDO-->>MCP: build 2891 = failed
    MCP-->>Agent1: { failed: 2891, last_good_sha: b1e3d8... }

    Note over Agent1: reason: diagnose the failure
    Agent1->>MCP: POST /message  diagnose_deployment(id=2891)
    MCP->>AzDO: GET /_apis/build/builds/2891/logs<br/>(MI bearer)
    AzDO-->>MCP: failure pattern matched
    MCP-->>Agent1: { root_cause, rollback_recommended: true }

    Note over Agent1: reason: ship rollback
    Agent1->>MCP: POST /message  create_rollback_pr<br/>(sha=b1e3d8..., reason="...")
    MCP->>AzDO: POST /_apis/git/repositories/.../pullrequests<br/>(MI bearer)
    AzDO-->>MCP: PR #142 created
    MCP-->>Agent1: { pr_url, pr_number }

    Agent1-->>SRE: "Created PR #142 — rollback to b1e3d8...<br/>Reason: <agent reasoning>"

    AzDO->>Gate: PR webhook
    Gate->>Gate: 62 security tests + PSRule
    Gate-->>AzDO: ✓ green
```

### Why this is "agent-to-agent"

- **Agent #1** never sees a CLI command, a YAML file, or an SDK call. It receives English and decides.
- **Agent #2** never sees the user. It exposes a tool surface; it doesn't know who's calling.
- The **MCP protocol** is the only contract between them. Either side can be swapped — Claude Desktop, VS Code MCP client, or another Foundry agent could call Agent #2 unchanged.

---

## 3. Deployment topology (what `azd up` provisions)

![Deployment topology](docs/architecture/03-deployment-topology.png)

```mermaid
flowchart TB
    subgraph rg["📦 Resource Group: rg-jonahdemo-dryrun1 (Sweden Central)"]
        direction TB

        subgraph identity["🔐 Identity layer"]
            mi["User-assigned MI<br/>id-mcpdemork5lpkrhjgtl6"]
        end

        subgraph compute["⚡ Compute"]
            funcapp["Function App (Flex Consumption FC1)<br/>func-mcpdemork5lpkrhjgtl6<br/>.NET 10 isolated"]
        end

        subgraph ai["🧠 AI"]
            foundry["Foundry account<br/>aif-mcpdemork5lpkrhjgtl6<br/><b>disableLocalAuth: true</b>"]
            proj["Project: proj-mcpdemork5lpkrhjgtl6"]
            foundry --- proj
        end

        subgraph data["💾 Data & monitoring"]
            stor["Storage account<br/><b>allowSharedKeyAccess: false</b><br/>(deployment + AzureWebJobs)"]
            ai_logs["App Insights<br/>OTel traces, metrics, logs"]
        end

        funcapp -->|"FederatedTokenCredential"| mi
        mi -->|"RBAC: Storage Blob Data Owner"| stor
        mi -->|"RBAC: Azure AI User"| foundry
        funcapp -->|"OTel exporter"| ai_logs
    end

    devops["Azure DevOps<br/><i>jonahanderssonazuredemos / msbuild2026eshopdemo</i>"]
    github["GitHub repo<br/><i>jonahandersson/msbuild2026-code2agents-...</i><br/>Actions: build, tests, PSRule"]

    mi -->|"Azure DevOps user<br/>(MI added as org member)"| devops
    funcapp -.->|"MCP SSE<br/>?code=mcp_extension"| ext["Foundry hosted MCP tool<br/>(server-side fetch)"]
    ext -.- foundry

    github -.->|"OIDC federated identity<br/>(no PAT)"| funcapp

    classDef azureBox fill:#fff3e0,stroke:#ef6c00,color:#000
    classDef secBox fill:#ffebee,stroke:#c62828,color:#000
    class rg azureBox
    class identity,mi secBox
```

### Identity & secrets — what's **not** in this picture

- ❌ No PATs (AzDO, GitHub, or Foundry)
- ❌ No connection strings (storage uses MI)
- ❌ No API keys hardcoded (Foundry: MI; MCP key: pulled at runtime)
- ❌ No client secrets in `local.settings.json` (only `UseDevelopmentStorage=true` for Azurite)

The **only** secret used at runtime is the Function App `mcp_extension` system key — appended as `?code=<key>` to the MCP URL when registering the hosted MCP tool with Foundry. It's pulled fresh each deploy with `az functionapp keys list`.

---

## 4. Local development loop (Aspire orchestration)

![Local dev loop](docs/architecture/04-local-dev.png)

```mermaid
flowchart LR
    dev["👨‍💻 Developer"]
    cmd["dotnet run --project src/AppHost"]
    dev --> cmd

    subgraph aspire["🎻 Aspire AppHost"]
        direction TB
        dash["Aspire dashboard<br/>https://localhost:17081<br/>traces · logs · metrics"]
        mcp_local["mcp-server resource<br/><i>func start (port 7071)</i>"]
        agent_local["devops-agent resource<br/><i>dotnet run</i>"]
        dash -.- mcp_local
        dash -.- agent_local
    end

    cmd --> aspire

    azurite["🪣 Azurite<br/>:10000 blob · :10001 queue · :10002 table"]
    mcp_local --> azurite

    foundry_cloud["☁️ Microsoft Foundry<br/>(reasoning agent + model)"]
    agent_local -->|"DefaultAzureCredential<br/>(your developer login)"| foundry_cloud

    agent_local <-->|"MCP SSE (localhost:7071)<br/>OR remote: $env:MCP_SERVER_URL"| mcp_local

    classDef localBox fill:#e8f5e9,stroke:#2e7d32,color:#000
    classDef cloudBox fill:#e3f2fd,stroke:#1565c0,color:#000
    class aspire,azurite,mcp_local,agent_local,dash localBox
    class foundry_cloud cloudBox
```

### Two run modes from the same `AppHost`

| Mode | Trigger | Used for |
|------|---------|----------|
| **LOCAL** (default) | `dotnet run --project src/AppHost` | Demo + dev — spawns `func start` |
| **REMOTE** | `$env:MCP_SERVER_URL = (azd env get-value MCP_ENDPOINT)` first | Skip local Functions host — talk to the deployed Function App. Useful when `func` Core Tools breaks. |

See [src/AppHost/Program.cs](src/AppHost/Program.cs#L31-L65) for the switch logic.

---

## 5. Security model (the layered controls)

![Security layers](docs/architecture/05-security-layers.png)

```mermaid
flowchart TB
    input["LLM tool call<br/>(potentially malicious)"]

    subgraph l1["Layer 1 — Input validation"]
        v["Validation.cs<br/>regex allow-lists with \\A...\\z anchors<br/>SHA, branch, repo"]
    end
    subgraph l2["Layer 2 — Approval policy"]
        ap["McpToolCallApprovalPolicy<br/>AGENT_APPROVAL_MODE=prod →<br/>AlwaysRequireApproval"]
    end
    subgraph l3["Layer 3 — Transport"]
        key["mcp_extension system key<br/>?code=... on /sse endpoint"]
    end
    subgraph l4["Layer 4 — Identity"]
        mi2["Managed Identity<br/>scoped RBAC (no broad perms)"]
    end
    subgraph l5["Layer 5 — CI gate"]
        ci["GitHub Actions<br/>62 xUnit security tests<br/>PSRule Azure baseline"]
    end

    input --> l1 --> l2 --> l3 --> l4 --> l5
    l5 -->|"merge if green"| merged["✓ Rollback merged"]
    l5 -->|"any red → block"| blocked["✗ Blocked"]

    classDef ok fill:#c8e6c9,stroke:#2e7d32,color:#000
    classDef bad fill:#ffcdd2,stroke:#c62828,color:#000
    class merged ok
    class blocked bad
```

Every layer is independently testable. See [SECURITY.md](SECURITY.md) for the threat model and [tests/DeploymentMcp.Tests/](tests/DeploymentMcp.Tests/) for the 62 enforcing tests.

---

## 6. Repository map

```
build2026-mcp-azure-functions/
├── src/
│   ├── AppHost/                   .NET Aspire orchestrator (local dev)
│   ├── ServiceDefaults/           Shared OTel + UseAzureMonitor()
│   ├── DeploymentMcp/             🛠️ Agent #2 — MCP server
│   │   ├── Tools/
│   │   │   └── DeploymentTools.cs    3 × [McpToolTrigger] methods
│   │   ├── Services/
│   │   │   ├── AzureDevOpsClient.cs  MI → AzDO REST
│   │   │   ├── Validation.cs         security allow-lists
│   │   │   └── FakeDeploymentService.cs  demo fixtures
│   │   └── Program.cs
│   └── DevOpsAgent/               🧠 Agent #1 — Foundry client
│       └── Program.cs                ResponseTool.CreateMcpTool
├── tests/
│   └── DeploymentMcp.Tests/       62 security tests
├── infra/
│   └── main.bicep                 Flex Consumption + MI + RBAC + Foundry
├── .github/workflows/
│   ├── ci.yml                     build + tests + PSRule SARIF
│   └── deploy.yml                 azd deploy (OIDC)
├── demo/shop-api-seed/            AzDO repo + pipeline seed
└── SECURITY.md                    threat model + controls
```

---

## 7. Key technical choices (and why)

| Choice | Rationale |
|--------|-----------|
| **Flex Consumption** (not Premium / App Service) | Native MI federated credentials, no Kudu dependencies, scales to zero, fits MCP's bursty pattern |
| **Hosted MCP** (server-side from Foundry) | Foundry runs the agentic loop, retries, observes — caller never wires SDKs. URL needs `?code=$key` because Foundry fetches without client auth headers |
| **`Worker.Extensions.Mcp` v1.0.0** | Official Functions extension. **SSE + JSON-RPC, not REST** — there is no `/tools/list` endpoint, only `/sse` + `/message` |
| **Declarative Foundry agent** | Instructions live on the Foundry side, versioned via `CreateAgentVersionAsync`. Agent behaviour is config, not code |
| **`disableLocalAuth: true` on Foundry** | Kills the API-key escape hatch. Only MI tokens work |
| **`allowSharedKeyAccess: false` on Storage** | Kills the connection-string escape hatch. Only MI tokens work |
| **`UseAzureMonitor()` in ServiceDefaults** | One line gives you OTel traces, logs, metrics → App Insights + Aspire dashboard. Conflicts with `AddApplicationInsightsTelemetryWorkerService` — don't use both |
| **PSRule for Azure CI gate** | Bicep is policy-checked on every PR, SARIF uploaded to Code Scanning |

---

_For the live demo flow that brings this architecture to life, see [_private/dry-run/REHEARSAL.md](_private/dry-run/REHEARSAL.md)._
