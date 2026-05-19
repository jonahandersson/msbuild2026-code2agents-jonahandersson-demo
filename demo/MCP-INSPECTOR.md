What to do in the UI
Left panel — Transport Type: select SSE
URL: paste:
Leave Headers empty, click Connect → status should turn green
Click the Tools tab → click List Tools
Expected result
You should see 3 tools matching what's in DeploymentTools.cs:

Tool	What it does
get_recent_deployments	Lists last N pipeline runs for a repo
diagnose_deployment	Returns failure summary for a given run
create_rollback_pr	Opens a PR reverting to last-good SHA
Each will show its description + JSON schema for parameters.