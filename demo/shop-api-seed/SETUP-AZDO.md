# Setting up Azure DevOps for the demo

> **Time budget:** 2–3 hours, ideally split across two sessions.
> The first session creates the org + project + repo + pipeline (~90 min).
> The second session wires up Managed Identity auth and verifies end-to-end (~60 min).

This guide walks you through everything from "I have an Azure subscription" to "the agent can create real PRs in Azure DevOps via Managed Identity."

---

## Required permissions for the Function's Managed Identity

The Function App's user-assigned MI (provisioned by `infra/main.bicep`) needs
these scopes inside Azure DevOps. Step 5 and Step 6 below walk you through
granting them; this table is the at-a-glance reference for the talk Q&A and
for security review.

| Scope                              | Where to grant                                                | Why                                                |
|------------------------------------|---------------------------------------------------------------|----------------------------------------------------|
| **Org membership** (Basic license) | Organization settings → Users                                 | The MI must be a member of the AzDO org at all     |
| **Project membership**             | Add to **Shop** project during Step 5                          | The MI needs access to the demo project specifically |
| `Read` on **shop-api** repo        | Project settings → Repositories → shop-api → Security         | `get_recent_deployments` calls Git APIs            |
| `Contribute`                       | Same place                                                     | `create_rollback_pr` needs to push a revert branch |
| `Create branch`                    | Same place                                                     | The rollback branch is a new ref                   |
| `Contribute to pull requests`      | Same place                                                     | The MI opens the PR itself                         |
| `View builds` on the project       | Project settings → Pipelines → Security                       | `diagnose_deployment` reads build results          |

**Anti-scopes** (do NOT grant):

- `Force push`, `Bypass policies`, `Manage permissions` — the agent must respect
  branch policies and required reviewers. That's the whole "the agent is a junior
  dev that types fast" story.
- `Project Administrator` / `Project Collection Administrator` — way too broad.
  `Project Contributors` is the demo shortcut; the explicit scopes above are
  the right answer for a real estate.

Auth between Function and AzDO is a Bearer token from the MI for resource
`499b84ac-1321-427f-aa17-267ca6975798` (the AzDO Entra app ID) — no PAT,
no secret, no connection string.

---

## Before you start

Have these ready:

- An **Azure subscription** where you can create resources (Contributor on a resource group is enough)
- A **Microsoft Entra ID tenant** (your Azure subscription is already in one)
- **Azure CLI** 2.60+ installed (`az --version`)
- **PowerShell 7+** (`pwsh --version`)
- **Git** with a configured identity
- The Function app from this repo **already deployed** (so you have a Managed Identity to add to AzDO)

If you haven't deployed the Function app yet:

```bash
cd build2026-mcp-azure-functions
azd auth login
azd up
```

Note the user-assigned identity's `principalId` and `clientId` from the `azd` output — you'll need them in Step 9.

---

## Session 1: Create the AzDO project and repo

### Step 1 — Create the Azure DevOps organization (5 min)

This is the only step that has to be done in the portal. The CLI cannot create new organizations.

1. Go to <https://aex.dev.azure.com/>.
2. Sign in with the same account you use for Azure.
3. Click **Create new organization**.
4. Name it something memorable — `<your-name>-build2026` works fine.
5. Pick a region close to you.

**Critical:** Make sure the organization is **connected to your Microsoft Entra tenant**. The signup flow does this by default if you sign in with a work/school account. If you signed in with a personal Microsoft account, the org gets created with no Entra connection, and Managed Identity auth will not work. To verify or fix:

- Open **Organization settings → Microsoft Entra** (left rail, under "General").
- It should show "Connected to: `<your-tenant-name>`" with a green check.
- If not, click **Connect directory** and pick your tenant.

**Verify:** open `https://dev.azure.com/<your-org>`. You should land on the org home page.

### Step 2 — Sign in with the Azure CLI (2 min)

The rest of this guide uses the CLI for repeatability.

```bash
az login
az devops configure --defaults organization=https://dev.azure.com/<your-org>
az devops project list -o table
```

The list should be empty (or show only the default starter project AzDO creates).

If `az devops` isn't recognized, install the extension:

```bash
az extension add --name azure-devops
```

### Step 3 — Run the seed script (10 min)

The seed script (`demo/shop-api-seed/seed-azdo.ps1` in this repo) does steps 4–7 for you. From the repo root:

```bash
cd demo/shop-api-seed
pwsh ./seed-azdo.ps1 -OrgUrl https://dev.azure.com/<your-org>
```

What it does:

1. Creates a project called **Shop**
2. Creates a Git repo called **shop-api**
3. Pushes four commits to `main` to build realistic history:
   - Initial scaffold
   - Add the Web API (Customer + Order models)
   - Add tests + CI pipeline ← **this commit is the "last known good"**
   - Add the `CustomerLoyalty` migration with `simulateTimeout: true` ← **this is the "bad" commit**
4. Creates the pipeline `shop-api-CI`

The script prints two commit SHAs at the end — write them down. The agent will discover them on its own at demo time, but having them in front of you helps debugging.

**Verify:**

- `https://dev.azure.com/<your-org>/Shop/_git/shop-api` shows four commits on `main`
- `https://dev.azure.com/<your-org>/Shop/_build` shows the `shop-api-CI` pipeline (not yet run)

### Step 4 — Run the pipeline a few times (15 min)

You need realistic history: some successful builds and some recent failures.

Go to **Pipelines → shop-api-CI → Run pipeline**. Hit Run.

The first run will fail at the **DeployMigrations** stage because HEAD is the "bad" commit. That's what you want.

Run the pipeline **twice more** against the bad commit. You now have three failing builds — `diagnose_deployment` will pick the most recent one.

Now, to get successful builds in the history, **temporarily** check out the last-known-good commit and run the pipeline against it:

```bash
git clone https://dev.azure.com/<your-org>/Shop/_git/shop-api
cd shop-api
git log --oneline    # find the SHA of "Add tests and CI pipeline"
```

In the AzDO portal, **Pipelines → shop-api-CI → Run → click Variables → Branch/tag**: pick `commits/<sha>` (the AzDO UI lets you run a pipeline against any commit). Run it. It should succeed.

Run it once more against the same commit. Now you have a green build history.

Your final state should look like this in the **Pipelines → Runs** view:

```
shop-api-CI #5  ❌ failed     main  (bad commit)
shop-api-CI #4  ❌ failed     main  (bad commit)
shop-api-CI #3  ✅ succeeded  <last-good-sha>
shop-api-CI #2  ✅ succeeded  <last-good-sha>
shop-api-CI #1  ❌ failed     main  (bad commit) — first run
```

**Verify:** the agent will call `get_recent_deployments` and expect to see a mix. You should have at least 2 failed + 2 succeeded recent runs.

---

## Session 2: Wire up Managed Identity auth

This is where most demos go sideways. Read this section twice, then do it once.

### Step 5 — Add the Function's Managed Identity as a user in AzDO (15 min)

The Function App has a user-assigned Managed Identity. From AzDO's perspective, that identity is a **service principal** in your Entra tenant. You need to add it as a user in the AzDO org.

In the portal:

1. **Organization settings → Users → Add users.**
2. In the **Users or Service Principals** field, paste the Function App's identity name (something like `id-mcpdemoa3b7c2d`).
3. **Access level**: Basic.
4. **Add to projects**: select **Shop**.
5. **Azure DevOps Groups**: pick **Project Contributors** for now. (You can tighten this later — see Step 8.)
6. Click **Add**.

If the identity doesn't show up in the search box, the most common cause is that the AzDO org isn't connected to the same Entra tenant as the Function App's identity. Re-check Step 1.

The CLI alternative:

```bash
# Get the Managed Identity's object ID
MI_OBJECT_ID=$(az identity show \
    --name id-mcpdemoa3b7c2d \
    --resource-group <your-rg> \
    --query principalId -o tsv)

# Add it as a user
az devops user add \
    --email-id "$MI_OBJECT_ID" \
    --license-type express
```

(The `--email-id` parameter accepts the principal's object ID for service principals.)

### Step 6 — Grant the right repo and build permissions (10 min)

Project Contributors is broader than you want for a real deployment, but for a demo it's the path of least resistance. To tighten things up:

1. **Project settings → Repositories → shop-api → Security**.
2. Find the Managed Identity in the list.
3. Set the permissions you actually need:
   - `Contribute`: Allow
   - `Contribute to pull requests`: Allow
   - `Create branch`: Allow
   - `Read`: Allow
4. Click Save.

For the build read permission:

1. **Project settings → Pipelines → Settings** (or **Project Permissions** depending on the AzDO UI version).
2. Make sure the Managed Identity (via Project Contributors group, or directly) has `View builds` permission.

### Step 7 — Configure the Function App's settings (5 min)

Update the Function App's settings so it knows which AzDO org and project to talk to:

```bash
az functionapp config appsettings set \
    --name <your-function-app-name> \
    --resource-group <your-rg> \
    --settings \
        "DemoMode=false" \
        "AzureDevOps__OrgUrl=https://dev.azure.com/<your-org>" \
        "AzureDevOps__Project=Shop"
```

Restart the Function App:

```bash
az functionapp restart \
    --name <your-function-app-name> \
    --resource-group <your-rg>
```

### Step 8 — Manual verification with curl (10 min)

Before you involve the agent, verify the Function can talk to AzDO with its own identity. From your laptop:

```bash
# Get a token for AzDO using your local 'az' identity (sanity check)
TOKEN=$(az account get-access-token \
    --resource 499b84ac-1321-427f-aa17-267ca6975798 \
    --query accessToken -o tsv)

# Hit the AzDO REST API directly
curl -s -H "Authorization: Bearer $TOKEN" \
    "https://dev.azure.com/<your-org>/Shop/_apis/build/builds?api-version=7.1&\$top=3" \
    | jq '.value[] | {id, result, sourceVersion}'
```

You should see your three most recent builds. If you get a 401, your `az` session doesn't have AzDO access — sign in with the account that has access to the org.

Now do the same against the deployed Function:

```bash
# Trigger the MCP server's tool with a direct call.
# Replace <func-url> with your function app's hostname.
curl -X POST "https://<func-url>/runtime/webhooks/mcp/tools/call" \
    -H "Content-Type: application/json" \
    -d '{
        "name": "get_recent_deployments",
        "arguments": { "repo": "shop-api", "branch": "main" }
    }'
```

You should see your three most recent builds, returned by the Function (which used its Managed Identity to query AzDO). If this works, the auth chain is complete.

If you get an empty list or an error: check the Function App's logs in Application Insights. The most common issue is the MI's permission scope in AzDO — go back to Step 5/6.

### Step 9 — Run the agent end-to-end (15 min)

From the repo root:

```bash
cd src/DevOpsAgent
export FOUNDRY_PROJECT_ENDPOINT="https://<your-foundry>.services.ai.azure.com/api/projects/<project>"
export FOUNDRY_MODEL="gpt-4o-mini"
export MCP_SERVER_URL="https://<your-func>.azurewebsites.net/runtime/webhooks/mcp"
dotnet run
```

When the prompt appears, paste:

> *"The latest deployment of shop-api to main is failing. Investigate and roll back if needed."*

Watch the Function logs in another terminal:

```bash
az webapp log tail --name <your-function-app-name> --resource-group <your-rg>
```

You should see three tool calls fire in sequence — `get_recent_deployments`, `diagnose_deployment`, `create_rollback_pr`.

Then go to your AzDO org's PR list:

```
https://dev.azure.com/<your-org>/Shop/_git/shop-api/pullrequests
```

There should be a new PR with a title like *"Rollback shop-api to <sha> — automated by agent"*.

**If this works end-to-end, you're ready for the talk.**

---

## Common gotchas

These are the failure modes I've seen most often when teams set up MI auth to Azure DevOps.

### "The identity is not a member of this organization"

The MI was added to AzDO but isn't in the right project. Go back to Step 5 and double-check that **Shop** is selected under "Add to projects".

### "TF400813: The user is not authorized"

The MI is in the org but doesn't have the right permissions on the repo. Go back to Step 6. Make sure both `Contribute` and `Create branch` are explicitly Allow (not Inherit).

### "Insufficient privileges to complete the operation" on PR creation

The MI can read but not write. Check **Project settings → Repositories → shop-api → Security** — make sure "Contribute to pull requests" is **Allow**.

### Diagnose returns "unknown" for the last-known-good commit

The real `AzureDevOpsClient.DiagnoseAsync` doesn't have log-parsing logic implemented (it's marked as a TODO extension point). For the demo:

- **Option A (simpler):** keep `DemoMode=true` for the diagnose step only. You can't have it both ways in the current code, so see Option B.
- **Option B (do this):** extend `DiagnoseAsync` to actually parse the build log. Pseudo-code:

  ```csharp
  // Fetch logs, find the line containing "Command timeout expired",
  // then call GetRecentAsync again, find the most recent succeeded build,
  // and return its commitSha as LastKnownGoodCommitSha.
  ```

  This is a 30-line addition. For a 20-minute demo, it's worth the time so you don't have to fudge the story.

### The MI can authenticate but gets an empty builds list

You're querying with `repositoryId={repoName}` but AzDO's REST API actually expects the repo's GUID, not its name, in some endpoints. The seed script handles this by name, but if you're hand-rolling: get the GUID first.

```bash
az repos show --repository shop-api --query 'id' -o tsv
```

Then pass that GUID into the URL.

---

## Tear-down (after the talk)

```bash
# Delete the AzDO project (this removes the repo + pipeline + PRs)
az devops project delete --id $(az devops project show -p Shop --query id -o tsv) --yes

# Delete the Azure resources
azd down --purge --force
```

The Entra-side identity goes away with the resource group.
