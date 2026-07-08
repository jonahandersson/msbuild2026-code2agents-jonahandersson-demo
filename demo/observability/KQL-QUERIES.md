# App Insights queries for the demo

> **Setup:** open the Application Insights resource in the Azure Portal,
> click **Logs**, and paste each query below into a tab. Save each as a *pinned
> query* (the bookmark icon) so you can switch between them without retyping.
> Running each once first also avoids the 4-5 second "first query is slow" tax.

## Why this matters

Azure Functions is a strong host for production MCP servers, and App Insights is
the proof: **every** MCP tool call, **every** outgoing call to Azure DevOps, and
**every** model interaction is automatically wired into the same observability
stack you already use for .NET apps.

The queries below let you trace an agent run end to end — from the model
interaction, through each MCP tool call, down to the Azure DevOps request.

## The four queries

Start with the first two — they give the clearest picture. The others are
useful for deeper investigation.

---

### 1. Tool calls timeline

Start here. It's the clearest proof that every MCP invocation becomes a
structured event in App Insights.

```kql
traces
| where timestamp > ago(15m)
// Category is the ILogger category name. Our tools log under
// 'DeploymentMcp.Tools.DeploymentTools' so 'DeploymentMcp' is the prefix.
| where customDimensions["Category"] startswith "DeploymentMcp"
| where message has "MCP tool"
// 'Tool' is set via logger.BeginScope(...) in DeploymentTools.cs.
// Depending on the Application Insights worker version the scope key may be
// flattened as customDimensions["Tool"] OR nested inside a 'Scope' JSON
// array. Coalesce both shapes so this query is robust either way.
| extend tool = coalesce(
    tostring(customDimensions["Tool"]),
    tostring(parse_json(tostring(customDimensions["Scope"]))[0].Tool))
| project
    timestamp,
    tool,
    message,
    operation_Id
| order by timestamp desc
| take 50
```

**What you see:** the three tool calls the agent just made, in order,
with the prompts the agent passed in. The `operation_Id` column is the gold
thread — every entry from one agent turn shares the same ID.

---

### 2. End-to-end transaction — the trace tree

This is the *"oh, it's all connected"* moment. Pick any `operation_Id` from
query 1 and run this:

```kql
let opId = "<paste-operation-id-here>";
union requests, dependencies, traces
| where timestamp > ago(1h)
| where operation_Id == opId
| project
    timestamp,
    itemType,
    name = coalesce(name, message),
    duration,
    success,
    target
| order by timestamp asc
```

**What you see:** one ordered list — agent.Turn → MCP tool call →
HTTP call to Azure DevOps → response back. The whole chain in one screen.

This is the agent's audit trail: every decision, every tool call, every external
HTTP request. If a regulator asks "what did your agent do," this is the answer.

---

### 3. Tool latency percentiles

To answer *"how do you know it's fast enough?"*, use this:

```kql
requests
| where timestamp > ago(24h)
// Azure Functions sets operation_Name to the [Function(nameof(X))] method
// name, NOT to the trigger type. These are the three MCP-triggered methods
// in DeploymentTools.cs.
| where operation_Name in ("GetRecentDeployments", "DiagnoseDeployment", "CreateRollbackPr")
| summarize
    p50 = percentile(duration, 50),
    p95 = percentile(duration, 95),
    p99 = percentile(duration, 99),
    count = count()
    by operation_Name
| order by count desc
```

**What you see:** real percentile latency per MCP tool. The
honest answer to *"is it production-ready?"*

---

### 4. Failed tool calls — the "what could go wrong" answer

Pre-warm this so if anyone asks about reliability, you can show real failure
data instead of waving hands:

```kql
requests
| where timestamp > ago(7d)
// See query 3 — operation_Name is the function method name.
| where operation_Name in ("GetRecentDeployments", "DiagnoseDeployment", "CreateRollbackPr")
| where success == false
| project
    timestamp,
    tool = operation_Name,
    duration,
    resultCode,
    operation_Id
| order by timestamp desc
| take 20
```

---

## Pre-warming script

Run this from `az` first. It triggers each query so the App Insights engine has
a hot path ready and you avoid the first-query latency.

```bash
APPINSIGHTS_ID="<your-app-insights-resource-id>"

az monitor app-insights query \
  --ids "$APPINSIGHTS_ID" \
  --analytics-query "traces | take 1" \
  > /dev/null

az monitor app-insights query \
  --ids "$APPINSIGHTS_ID" \
  --analytics-query "requests | take 1" \
  > /dev/null

echo "App Insights pre-warmed."
```

## Save-as-workbook trick

If your connection to the Azure Portal is slow, save query #1 and #2 as a single
Azure Workbook (Workbooks → New → Add Query → save). The Workbook caches the
results visually, so it renders immediately while the live query runs in the
background.

A pre-saved Workbook is a good safety net for the App Insights view — just like
the cached MCP responses are a safety net for the agent run.
