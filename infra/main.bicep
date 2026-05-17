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

@description('Foundry project resource ID. Function MI gets reader on this.')
param foundryProjectId string = ''

// Predictable, unique-ish suffix so names don't collide across deploys
var suffix = uniqueString(resourceGroup().id, environmentName)
var resourceToken = toLower('${namePrefix}${suffix}')

// =============================================================================
// Identity
// =============================================================================

resource functionIdentity 'Microsoft.ManagedIdentity/userAssignedIdentities@2023-01-31' = {
  name: 'id-${resourceToken}'
  location: location
}

// =============================================================================
// Storage (Functions runtime backing)
// =============================================================================

resource storage 'Microsoft.Storage/storageAccounts@2023-05-01' = {
  name: 'st${resourceToken}'
  location: location
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
  properties: {
    sku: { name: 'PerGB2018' }
    retentionInDays: 30
  }
}

resource appInsights 'Microsoft.Insights/components@2020-02-02' = {
  name: 'ai-${resourceToken}'
  location: location
  kind: 'web'
  properties: {
    Application_Type: 'web'
    WorkspaceResourceId: logWorkspace.id
  }
}

// =============================================================================
// Function App on Flex Consumption
// =============================================================================

resource flexPlan 'Microsoft.Web/serverfarms@2023-12-01' = {
  name: 'plan-${resourceToken}'
  location: location
  sku: { name: 'FC1', tier: 'FlexConsumption' }
  kind: 'functionapp'
  properties: { reserved: true }
}

resource functionApp 'Microsoft.Web/sites@2023-12-01' = {
  name: 'func-${resourceToken}'
  location: location
  kind: 'functionapp,linux'
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
          value: 'false'
        }
        {
          name: 'AzureDevOps__OrgUrl'
          value: 'https://dev.azure.com/<your-org>'
        }
        {
          name: 'AzureDevOps__Project'
          value: '<your-project>'
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

// Optional: give the Function identity reader access to your Foundry project,
// so calls from the Function to Foundry don't need any keys.
resource foundryReaderAssignment 'Microsoft.Authorization/roleAssignments@2022-04-01' =
  if (!empty(foundryProjectId)) {
    name: guid(foundryProjectId, functionIdentity.id, 'AzureAIUser')
    properties: {
      principalId: functionIdentity.properties.principalId
      principalType: 'ServicePrincipal'
      // Azure AI User (built-in role for Foundry)
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
output MCP_ENDPOINT string              =
  'https://${functionApp.properties.defaultHostName}/runtime/webhooks/mcp'
