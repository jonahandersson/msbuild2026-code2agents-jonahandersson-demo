# DevOpsAgentChat Azure Deployment

## Prerequisites
- Azure CLI installed
- Resource group (same as eShop) exists
- (Optional) Service principal or federated identity for CI/CD

## Deploy Infrastructure
1. Login to Azure:
   ```sh
   az login
   az account set --subscription <your-subscription-id>
   ```
2. Deploy infra (from the infra folder):
   ```sh
   az group deployment create -g <eshop-resource-group> --template-file main.bicep
   ```

## Deploy Blazor UI and Agent Backend
1. Publish the Blazor UI:
   ```sh
   dotnet publish -c Release
   az webapp deploy --resource-group <eshop-resource-group> --name <webAppName> --src-path ./bin/Release/net10.0/publish
   ```
2. Publish the Agent backend (repeat for your MCP backend):
   ```sh
   dotnet publish -c Release
   az webapp deploy --resource-group <eshop-resource-group> --name <agentAppName> --src-path ./bin/Release/net10.0/publish
   ```

## Configuration
- The Blazor UI will use the MCP_AGENT_ENDPOINT app setting to call the agent backend.
- Update the endpoint in Azure Portal if needed.

---

**Integrate these steps into your CI/CD pipeline for automation.**
