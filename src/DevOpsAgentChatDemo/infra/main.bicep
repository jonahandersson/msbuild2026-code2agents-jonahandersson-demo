
@description('App Service plan name')
param planName string = 'plan-devopsagentchat'

@description('Web App name')
param webAppName string = 'app-devopsagentchat'

@description('Azure region')
param location string = resourceGroup().location

@description('Foundry project endpoint for the DevOps agent')
param foundryProjectEndpoint string = 'https://aif-mcpdemork5lpkrhjgtl6.services.ai.azure.com/api/projects/proj-mcpdemork5lpkrhjgtl6'

@description('Foundry model deployment name')
param foundryModel string = 'gpt-4.1-mini'

@description('MCP server URL (Azure Function MCP endpoint, including the extension key)')
param mcpServerUrl string = 'https://func-mcpdemork5lpkrhjgtl6.azurewebsites.net/runtime/webhooks/mcp?code=w7mYjvEpo6YSSLWsx5oQpCV6MCRdhMJh_R16X-S2HNNYAzFuOxeTow=='

resource plan 'Microsoft.Web/serverfarms@2023-12-01' = {
  name: planName
  location: location
  sku: {
    name: 'B1'
    tier: 'Basic'
  }
  kind: 'linux'
  properties: {
    reserved: true
  }
}

resource web 'Microsoft.Web/sites@2023-12-01' = {
  name: webAppName
  location: location
  kind: 'app,linux'
  identity: {
    type: 'SystemAssigned'
  }
  tags: {
    'azd-service-name': 'web'
  }
  properties: {
    serverFarmId: plan.id
    httpsOnly: true
    siteConfig: {
      linuxFxVersion: 'DOTNETCORE|10.0'
      alwaysOn: true
      ftpsState: 'Disabled'
      minTlsVersion: '1.2'
      http20Enabled: true
      healthCheckPath: '/'
      appCommandLine: 'dotnet DevOpsAgentChat.dll'
      appSettings: [
        {
          name: 'ASPNETCORE_ENVIRONMENT'
          value: 'Production'
        }
        {
          name: 'THEME_COLOR'
          value: '#0078D4'
        }
        {
          name: 'FOUNDRY_PROJECT_ENDPOINT'
          value: foundryProjectEndpoint
        }
        {
          name: 'FOUNDRY_MODEL'
          value: foundryModel
        }
        {
          name: 'MCP_SERVER_URL'
          value: mcpServerUrl
        }
      ]
    }
  }
}

output webAppUrl string = web.properties.defaultHostName

@description('Principal ID of the web app system-assigned identity. Grant this access to the Foundry project (e.g. Azure AI Developer / Cognitive Services User) so DefaultAzureCredential can authenticate.')
output webAppPrincipalId string = web.identity.principalId
