// Scoped role assignment so the Function App's managed identity can call the
// Foundry project. Lives in its own module so the assignment lands in the
// Foundry project's resource group, not the Function's.

@description('Name of the Foundry project (last segment of its resource ID).')
param foundryProjectName string

@description('Principal (object) ID of the Function App user-assigned MI.')
param principalId string

@description('Built-in role ID. Defaults to Azure AI User.')
param roleDefinitionId string = '53ca6127-db72-4b80-b1b0-d745d6d5456d'

// Foundry projects are typed Microsoft.CognitiveServices/accounts/projects
// in the current GA shape. Reference as 'existing' so we don't try to manage it.
resource foundryProject 'Microsoft.CognitiveServices/accounts/projects@2025-04-01-preview' existing = {
  name: foundryProjectName
}

resource roleAssignment 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(foundryProject.id, principalId, roleDefinitionId)
  scope: foundryProject
  properties: {
    principalId: principalId
    principalType: 'ServicePrincipal'
    roleDefinitionId: subscriptionResourceId(
      'Microsoft.Authorization/roleDefinitions',
      roleDefinitionId)
  }
}
