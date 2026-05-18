// =============================================================================
// Build 2026 demo — MCP server on Azure Functions
//
// This file is deliberately kept as a SINGLE file with no modules, so when you
// flip to it in the demo it reads top-to-bottom on one screen.
// In a real production estate, factor this into modules.
// =============================================================================

targetScope = 'resourceGroup'

@description('Environment name used by azd (e.g. dev, demo).')
param environmentName string

@description('Azure region.')
param location string = resourceGroup().location

@description('Name prefix for resources. Lowercase, 3-11 chars.')
@minLength(3)
@maxLength(11)
param namePrefix string = 'mcpdemo'

@description('Foundry model deployment name. Must be available in the target region.')
param foundryModelName string = 'gpt-4o-mini'

@description('Foundry model version. Verify availability with: az cognitiveservices model list --location <region>.')
param foundryModelVersion string = '2024-07-18'

@description('Foundry model deployment capacity in thousands of tokens-per-minute.')
@minValue(1)
param foundryModelCapacity int = 30

@description('When true, the Function uses the in-memory FakeDeploymentService and AzureDevOps* settings are not required. String to play nice with azd env-var substitution.')
@allowed([ 'true', 'false' ])
param demoMode string = 'true'

@description('Azure DevOps organization URL (only used when demoMode is false).')
param azureDevOpsOrgUrl string = ''

@description('Azure DevOps project name (only used when demoMode is false).')
param azureDevOpsProject string = ''

// Predictable, unique-ish suffix so names don't collide across deploys
var suffix = uniqueString(resourceGroup().id, environmentName)
var resourceToken = toLower('${namePrefix}${suffix}')

var tags = {
  'azd-env-name': environmentName
}

// =============================================================================
// Identity
// =============================================================================

resource functionIdentity 'Microsoft.ManagedIdentity/userAssignedIdentities@2023-01-31' = {
  name: 'id-${resourceToken}'
  location: location
  tags: tags
}

// =============================================================================
// Storage (Functions runtime backing)
// =============================================================================

resource storage 'Microsoft.Storage/storageAccounts@2023-05-01' = {
  // Storage account names: lowercase alphanumeric, 3-24 chars.
  name: take('st${resourceToken}', 24)
  location: location
  tags: tags
  sku: { name: 'Standard_LRS' }
  kind: 'StorageV2'
  properties: {
    allowBlobPublicAccess: false
    minimumTlsVersion: 'TLS1_2'
    supportsHttpsTrafficOnly: true
    allowSharedKeyAccess: false   // Managed Identity all the way down
  }
}

// =============================================================================
// Observability
// =============================================================================

resource logWorkspace 'Microsoft.OperationalInsights/workspaces@2023-09-01' = {
  name: 'log-${resourceToken}'
  location: location
  tags: tags
  properties: {
    sku: { name: 'PerGB2018' }
    retentionInDays: 30
  }
}

resource appInsights 'Microsoft.Insights/components@2020-02-02' = {
  name: 'ai-${resourceToken}'
  location: location
  tags: tags
  kind: 'web'
  properties: {
    Application_Type: 'web'
    WorkspaceResourceId: logWorkspace.id
  }
}

// =============================================================================
// Microsoft Foundry — account + project + model deployment
//
// One-shot AI Foundry: the account holds the model deployments, the project
// is what the agent connects to. Entra-only (no keys) to match the talk's
// "managed identity all the way down" narrative.
// =============================================================================

resource foundryAccount 'Microsoft.CognitiveServices/accounts@2025-06-01' = {
  name: 'aif-${resourceToken}'
  location: location
  tags: tags
  kind: 'AIServices'
  sku: { name: 'S0' }
  identity: { type: 'SystemAssigned' }
  properties: {
    customSubDomainName: 'aif-${resourceToken}'
    publicNetworkAccess: 'Enabled'
    disableLocalAuth: true
    allowProjectManagement: true
  }
}

resource foundryProject 'Microsoft.CognitiveServices/accounts/projects@2025-06-01' = {
  parent: foundryAccount
  name: 'proj-${resourceToken}'
  location: location
  tags: tags
  identity: { type: 'SystemAssigned' }
  properties: {
    displayName: 'Build 2026 DevOps Agent'
    description: 'Project for the rollback agent demo.'
  }
}

resource foundryModelDeployment 'Microsoft.CognitiveServices/accounts/deployments@2025-06-01' = {
  parent: foundryAccount
  name: foundryModelName
  sku: {
    name: 'GlobalStandard'
    capacity: foundryModelCapacity
  }
  properties: {
    model: {
      format: 'OpenAI'
      name: foundryModelName
      version: foundryModelVersion
    }
    raiPolicyName: 'Microsoft.DefaultV2'
  }
}

// =============================================================================
// Function App on Flex Consumption
// =============================================================================

resource flexPlan 'Microsoft.Web/serverfarms@2023-12-01' = {
  name: 'plan-${resourceToken}'
  location: location
  tags: tags
  sku: { name: 'FC1', tier: 'FlexConsumption' }
  kind: 'functionapp'
  properties: { reserved: true }
}

resource functionApp 'Microsoft.Web/sites@2023-12-01' = {
  name: 'func-${resourceToken}'
  location: location
  kind: 'functionapp,linux'
  // azd uses this tag to know which Function App to push the 'deployment-mcp' service to.
  tags: union(tags, {
    'azd-service-name': 'deployment-mcp'
  })
  identity: {
    type: 'UserAssigned'
    userAssignedIdentities: {
      '${functionIdentity.id}': {}
    }
  }
  properties: {
    serverFarmId: flexPlan.id
    httpsOnly: true
    functionAppConfig: {
      deployment: {
        storage: {
          type: 'blobContainer'
          value: '${storage.properties.primaryEndpoints.blob}app-package'
          authentication: {
            type: 'UserAssignedIdentity'
            userAssignedIdentityResourceId: functionIdentity.id
          }
        }
      }
      runtime: {
        name: 'dotnet-isolated'
        version: '10.0'
      }
      scaleAndConcurrency: {
        instanceMemoryMB: 2048
        maximumInstanceCount: 100
      }
    }
    siteConfig: {
      appSettings: [
        {
          name: 'AzureWebJobsStorage__accountName'
          value: storage.name
        }
        {
          name: 'AzureWebJobsStorage__credential'
          value: 'managedidentity'
        }
        {
          name: 'AzureWebJobsStorage__clientId'
          value: functionIdentity.properties.clientId
        }
        {
          name: 'APPLICATIONINSIGHTS_CONNECTION_STRING'
          value: appInsights.properties.ConnectionString
        }
        {
          name: 'DemoMode'
          value: demoMode
        }
        {
          // Only emitted when not in demo mode; otherwise AzureDevOpsOptions
          // validation is skipped in Program.cs so the Function still boots.
          name: 'AzureDevOps__OrgUrl'
          value: demoMode == 'true' ? '' : azureDevOpsOrgUrl
        }
        {
          name: 'AzureDevOps__Project'
          value: demoMode == 'true' ? '' : azureDevOpsProject
        }
        {
          name: 'FOUNDRY_PROJECT_ENDPOINT'
          value: foundryProject.properties.endpoints['AI Foundry API']
        }
        {
          name: 'FOUNDRY_MODEL'
          value: foundryModelName
        }
      ]
    }
  }
}

// =============================================================================
// Role assignments
// =============================================================================

// Storage Blob Data Owner so the Function can use its identity for the runtime
resource storageRoleAssignment 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(storage.id, functionIdentity.id, 'StorageBlobDataOwner')
  scope: storage
  properties: {
    principalId: functionIdentity.properties.principalId
    principalType: 'ServicePrincipal'
    // Storage Blob Data Owner
    roleDefinitionId: subscriptionResourceId(
      'Microsoft.Authorization/roleDefinitions',
      'b7e6dc6d-f1e8-4753-8033-0f276bb0955b')
  }
}

// Function MI -> Cognitive Services User on the Foundry account.
// Lets the identity discover models + read deployments.
resource foundryCognitiveServicesUser 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(foundryAccount.id, functionIdentity.id, 'CognitiveServicesUser')
  scope: foundryAccount
  properties: {
    principalId: functionIdentity.properties.principalId
    principalType: 'ServicePrincipal'
    // Cognitive Services User
    roleDefinitionId: subscriptionResourceId(
      'Microsoft.Authorization/roleDefinitions',
      'a97b65f3-24c7-4388-baec-2e87135dc908')
  }
}

// Function MI -> Azure AI User on the Foundry account.
// Lets the identity create/run agents inside any project under the account.
resource foundryAzureAiUser 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(foundryAccount.id, functionIdentity.id, 'AzureAIUser')
  scope: foundryAccount
  properties: {
    principalId: functionIdentity.properties.principalId
    principalType: 'ServicePrincipal'
    // Azure AI User
    roleDefinitionId: subscriptionResourceId(
      'Microsoft.Authorization/roleDefinitions',
      '53ca6127-db72-4b80-b1b0-d745d6d5456d')
  }
}

// =============================================================================
// Outputs — azd uses these
// =============================================================================

output RESOURCE_GROUP_ID string         = resourceGroup().id
output FUNCTION_APP_NAME string         = functionApp.name
output FUNCTION_APP_HOSTNAME string     = functionApp.properties.defaultHostName
output FUNCTION_IDENTITY_CLIENT_ID string = functionIdentity.properties.clientId
output APPLICATION_INSIGHTS_NAME string  = appInsights.name
output MCP_ENDPOINT string              = 'https://${functionApp.properties.defaultHostName}/runtime/webhooks/mcp'
output FOUNDRY_ACCOUNT_NAME string      = foundryAccount.name
output FOUNDRY_PROJECT_NAME string      = foundryProject.name
output FOUNDRY_PROJECT_ENDPOINT string  = foundryProject.properties.endpoints['AI Foundry API']
output FOUNDRY_MODEL string             = foundryModelName
