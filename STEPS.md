# How to branch this into the demo steps

The `main` branch holds the **finished** version of the demo. For the talk you want three branches that reveal the story progressively. Here's the recipe.

## One-time setup

```bash
git checkout main
git tag baseline-complete       # safety net so you can always get back
git push origin --tags
```

## Branch 1 — `step-1-mcp-tool`

The minimal MCP tool on a Function. **The audience sees this first; it has to look small.**

What to strip from main:

- Remove `AzureDevOpsClient.cs` (keep just the interface + the fake)
- Remove the `AzureDevOpsOptions` registration in `Program.cs`
- Remove the resilience handler from `Program.cs`
- Remove the App Insights setup from `Program.cs` (keep `host.json` minimal)
- In `DeploymentTools.cs`, **keep all three tools** — that's the point of beat 4

```bash
git checkout -b step-1-mcp-tool main

# Surgical edits (see above)
git add -A
git commit -m "Step 1: MCP tools on Azure Functions"
git tag step-1
git push origin step-1-mcp-tool --tags
```

The diff vs `step-0-baseline` should be **one new file** (`DeploymentTools.cs`) — that's the visual moment.

## Branch 2 — `step-2-production`

Add back what you stripped. The audience sees the *diff* — that's the lesson.

```bash
git checkout -b step-2-production step-1-mcp-tool

# Add back:
#   - AzureDevOpsClient.cs with DefaultAzureCredential
#   - Options validation in Program.cs
#   - Resilience handler
#   - App Insights worker service
#   - The conditional DemoMode swap
git add -A
git commit -m "Step 2: Production hardening — MI, telemetry, validation"
git tag step-2
git push origin step-2-production --tags
```

## Branch 3 — `step-3-agent-azdo`

Adds the `DevOpsAgent` console app and the Bicep role assignment for Azure DevOps.

```bash
git checkout -b step-3-agent-azdo step-2-production
# Add src/DevOpsAgent/* (already on main)
git add -A
git commit -m "Step 3: The agent (Microsoft Agent Framework) + AzDO role assignment"
git tag step-3
git push origin step-3-agent-azdo --tags
```

## Optional — `step-0-baseline`

If you want a "look how empty this is" opener for beat 4:

```bash
git checkout -b step-0-baseline main

# Reduce to:
#   - empty src/DeploymentMcp/ Function with just a Program.cs + .csproj
#   - host.json without the MCP extensions block
#   - README pointing to the start
git add -A
git commit -m "Step 0: Empty scaffold"
git tag step-0
git push origin step-0-baseline --tags
```

## Manual extras for the live demo

These are **not in code** — you do them once in the Azure portal so the live demo works:

1. **Service connection: Function → Azure DevOps**
   In the Azure DevOps org, create a Service Connection of type *Azure Resource Manager* using the Function App's user-assigned identity. Grant it `Code (Read & Write)` and `Build (Read)` scopes.

   Or, simpler: give the Function App's MI direct membership in an AzDO group with project-level Contributor on the target repo.

2. **Pre-create a failing deployment in your demo repo**
   The talk story requires a "shop-api on main is failing" history. The easiest way: push a commit that breaks the build pipeline of your demo repo, let it fail, then push a fixing commit (which will be the "last known good"). Now you have realistic history.

3. **Foundry model deployment**
   Pre-deploy `gpt-4o-mini` (or `gpt-5.4-mini`) in your Foundry project. Note the project endpoint URL. Set it in `local.settings.json` and as a CI variable.

## Speaking points to add as commit messages

Use the commit message as the speaker's reminder:

```
Step 1: MCP tools on Azure Functions

This is the moment to say:
"Three things to notice. (1) Regular Azure Function. No special hosting.
 (2) McpToolTrigger is the contract with the agent — the name and description
     here are what the model sees when it decides whether to call.
 (3) The parameters are typed. Not strings-with-prayers."
```

Then `git log -1` is your cue card during rehearsal.
