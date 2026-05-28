// =============================================================================
// MCP server on Azure Functions — sample infrastructure
//
// This file is deliberately kept as a SINGLE file with no modules so it reads
// top-to-bottom on one screen. In a real production estate, factor this into
// modules.
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
// Aspire Dashboard on Azure Container Apps
//
// Standalone dashboard image (mcr.microsoft.com/dotnet/aspire-dashboard:9.0).
// Acts as an OTLP sink so the deployed Function App ships traces here too —
// you get the same dashboard UX in the cloud that Aspire gives you locally.
//
// Two ports exposed:
//   18888 — web UI (BrowserToken auth, https)
//   18889 — OTLP gRPC ingest (ApiKey auth, http2)
// =============================================================================

// Deterministic-but-opaque key for OTLP ingest. Rotate by changing a tag.
var otlpApiKey = uniqueString(resourceGroup().id, 'otlp', environmentName)

resource cae 'Microsoft.App/managedEnvironments@2024-03-01' = {
  name: 'cae-${resourceToken}'
  location: location
  tags: tags
  properties: {
    appLogsConfiguration: {
      destination: 'log-analytics'
      logAnalyticsConfiguration: {
        customerId: logWorkspace.properties.customerId
        sharedKey: logWorkspace.listKeys().primarySharedKey
      }
    }
  }
}

resource aspireDashboard 'Microsoft.App/containerApps@2024-03-01' = {
  name: 'ca-aspire-${resourceToken}'
  location: location
  tags: tags
  properties: {
    environmentId: cae.id
    configuration: {
      ingress: {
        external: true
        targetPort: 18888
        transport: 'http2'
        additionalPortMappings: [
          {
            // OTLP gRPC ingest endpoint (Function App ships traces here).
            external: true
            targetPort: 18889
            exposedPort: 18889
          }
        ]
      }
      secrets: [
        {
          name: 'otlp-apikey'
          value: otlpApiKey
        }
      ]
    }
    template: {
      containers: [
        {
          name: 'aspire-dashboard'
          image: 'mcr.microsoft.com/dotnet/aspire-dashboard:9.0'
          resources: {
            cpu: json('0.5')
            memory: '1Gi'
          }
          env: [
            // UI auth — visitors need the dashboard's printed browser token.
            // Switch to 'Unsecured' only if you'll demo in a private network.
            { name: 'Dashboard__Frontend__AuthMode', value: 'BrowserToken' }
            // OTLP auth — Functions identity sends this key in headers.
            { name: 'Dashboard__Otlp__AuthMode', value: 'ApiKey' }
            {
              name: 'Dashboard__Otlp__PrimaryApiKey'
              secretRef: 'otlp-apikey'
            }
            // Bind to all interfaces so ACA can route into the container.
            { name: 'ASPNETCORE_URLS', value: 'http://+:18888' }
            { name: 'DOTNET_DASHBOARD_OTLP_ENDPOINT_URL', value: 'http://+:18889' }
          ]
        }
      ]
      scale: {
        minReplicas: 1
        maxReplicas: 1
      }
    }
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
    displayName: 'DevOps Rollback Agent'
    description: 'Project for the rollback agent sample.'
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
          // TODO (prod): Move AzureDevOps__OrgUrl and AzureDevOps__Project into
          // Key Vault and inject via @Microsoft.KeyVault(VaultName=...;SecretName=...)
          // references once these point at a real org. The Function App's managed
          // identity already has Storage Blob Data Owner; add Key Vault Secrets User.
          // See: https://learn.microsoft.com/azure/app-service/app-service-key-vault-references
          //
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
        // --- OTLP export to the Aspire Dashboard ACA ---
        // ServiceDefaults registers the OTLP exporter when this is set, in
        // addition to the Azure Monitor exporter. Traces fan out to both.
        {
          name: 'OTEL_EXPORTER_OTLP_ENDPOINT'
          value: 'https://${aspireDashboard.properties.configuration.ingress.fqdn}:18889'
        }
        {
          name: 'OTEL_EXPORTER_OTLP_PROTOCOL'
          value: 'grpc'
        }
        {
          name: 'OTEL_EXPORTER_OTLP_HEADERS'
          value: 'x-otlp-api-key=${otlpApiKey}'
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

// =============================================================================
// ShopWeb — Blazor Server storefront on App Service Linux
//
// Visual prop for the demo: "this is the system the rollback agent protects."
// Stateless Blazor Server + in-memory EF, B1 plan, single instance — plenty
// for the talk and ~$13/mo. Independent of the MCP server and the failed
// pipeline narrative on purpose.
// =============================================================================

resource shopWebPlan 'Microsoft.Web/serverfarms@2023-12-01' = {
  name: 'plan-shopweb-${resourceToken}'
  location: location
  tags: tags
  sku: { name: 'B1', tier: 'Basic' }
  kind: 'linux'
  properties: { reserved: true }
}

resource shopWeb 'Microsoft.Web/sites@2023-12-01' = {
  name: 'app-shopweb-${resourceToken}'
  location: location
  kind: 'app,linux'
  // azd matches this tag against the service name in azure.yaml.
  tags: union(tags, {
    'azd-service-name': 'shop-web'
  })
  properties: {
    serverFarmId: shopWebPlan.id
    httpsOnly: true
    siteConfig: {
      linuxFxVersion: 'DOTNETCORE|10.0'
      alwaysOn: true
      ftpsState: 'Disabled'
      minTlsVersion: '1.2'
      http20Enabled: true
      healthCheckPath: '/'
      appCommandLine: 'dotnet ShopWeb.dll'
      appSettings: [
        {
          name: 'ASPNETCORE_ENVIRONMENT'
          value: 'Production'
        }
        {
          // Forwarded-Proto so Blazor builds correct https:// URLs.
          name: 'ASPNETCORE_FORWARDEDHEADERS_ENABLED'
          value: 'true'
        }
        {
          // Ship ShopWeb traces into the same App Insights + Aspire dashboard.
          name: 'APPLICATIONINSIGHTS_CONNECTION_STRING'
          value: appInsights.properties.ConnectionString
        }
      ]
    }
  }
}

resource shopWebLogs 'Microsoft.Web/sites/config@2023-12-01' = {
  parent: shopWeb
  name: 'logs'
  properties: {
    applicationLogs: { fileSystem: { level: 'Information' } }
    httpLogs:        { fileSystem: { enabled: true, retentionInDays: 3, retentionInMb: 35 } }
    detailedErrorMessages: { enabled: true }
    failedRequestsTracing: { enabled: true }
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
// Foundry hosted MCP tool fetches /sse server-side and needs the function-key
// query string (the systemKeys.mcp_extension key is generated at first start, so
// the deploy script appends ?code=<key> to this URL when registering the agent).
output MCP_SSE_ENDPOINT string          = 'https://${functionApp.properties.defaultHostName}/runtime/webhooks/mcp/sse'
output FOUNDRY_ACCOUNT_NAME string      = foundryAccount.name
output FOUNDRY_PROJECT_NAME string      = foundryProject.name
output FOUNDRY_PROJECT_ENDPOINT string  = foundryProject.properties.endpoints['AI Foundry API']
output FOUNDRY_MODEL string             = foundryModelName
output ASPIRE_DASHBOARD_URL string      = 'https://${aspireDashboard.properties.configuration.ingress.fqdn}'
output SHOP_WEB_APP_NAME string         = shopWeb.name
output SHOP_WEB_URL string              = 'https://${shopWeb.properties.defaultHostName}'
output SHOP_WEB_RESOURCE_GROUP string   = resourceGroup().name
output ASPIRE_DASHBOARD_OTLP string     = 'https://${aspireDashboard.properties.configuration.ingress.fqdn}:18889'

