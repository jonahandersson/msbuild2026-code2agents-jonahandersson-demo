# Infrastructure as Code for DevOpsAgentChat

This folder contains Bicep templates to provision:
- Blazor Web App (devopsagentchat-web)
- DevOps Agent backend (devopsagent-backend)
- Shared App Service Plan

## Deploying to Azure

1. **Login to Azure:**
   ```sh
   az login
   az account set --subscription <your-subscription-id>
   ```
2. **Deploy using your existing resource group and App Service Plan:**
   ```sh
   az deployment group create \
     -g <your-existing-resource-group> \
     --template-file main.bicep \
     --parameters \
       webAppName=<your-web-app-name> \
       agentAppName=<your-agent-app-name> \
       existingPlanName=<your-app-service-plan-name> \
       existingPlanResourceGroup=<your-existing-resource-group> \
       azureAdClientId=<your-entra-client-id> \
       azureAdTenantId=<your-entra-tenant-id> \
       azureAdDomain=<your-entra-domain>
   ```
3. **Deploy your apps:**
   - Publish the Blazor UI and agent backend to their respective Web Apps.
   - The UI will use the MCP_AGENT_ENDPOINT and Entra ID app settings for SSO.

---

**All infra is deployed to your existing resource group and plan.**
