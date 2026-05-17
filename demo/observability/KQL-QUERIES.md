# App Insights queries for the demo

> **Speaker prep:** open the Application Insights resource in the Azure Portal,
> click **Logs**, and paste each query below into a tab. Save each as a *pinned
> query* (the bookmark icon) so during the talk you can switch tabs without
> typing. Pre-warming the queries also avoids the 4-5 second "first query is
> slow" tax.

## Why this matters for the talk

The talk's third takeaway is *"Azure Functions is the right host for production
MCP."* App Insights is the receipt for that claim. When you flip to the portal
at minute 10, the audience sees that **every** MCP tool call, **every** outgoing
call to Azure DevOps, and **every** model interaction is already wired into the
same observability stack they already use for .NET apps.

You don't need to explain the queries. You need to **scroll through results**
that match the story the agent just told.

## The four queries to pin

Pick **two** to actually show on stage — that's all you have time for. The
others are backup.

---

### 1. Tool calls timeline — the visual hero

This is the one to show first. It's the visual proof that every MCP invocation
becomes a structured event in App Insights.

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

**What the audience sees:** the three tool calls the agent just made, in order,
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

**What the audience sees:** one ordered list — agent.Turn → MCP tool call →
HTTP call to Azure DevOps → response back. The whole chain in one screen.

**Speaker line for this:** *"This is the agent's audit trail. Every decision,
every tool call, every external HTTP request. If a regulator asks 'what did
your agent do,' this is the answer."*

---

### 3. Tool latency percentiles — for the cost-conscious audience member

If a senior engineer in the front row asks *"how do you know it's fast enough?",*
flip to this:

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

**What the audience sees:** real percentile latency per MCP tool. The
honest answer when someone asks *"is it production-ready?"*

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

Run this from `az` before going on stage. Triggers each query so the App
Insights engine has a hot path ready for the live demo.

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

If your conference Wi-Fi to the Azure Portal is slow, save query #1 and #2
as a single Azure Workbook (Workbooks → New → Add Query → save). The Workbook
caches the results visually. When you click into it on stage, you see the
results immediately while the live query runs in the background.

A pre-saved Workbook is the right safety net for the App Insights beat —
just like the cached MCP responses are the safety net for the agent beat.
