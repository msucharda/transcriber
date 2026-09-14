targetScope = 'resourceGroup'

@description('Globally unique, lowercase account name and custom subdomain (2-64 letters, digits, or hyphens; start and end with a letter or digit). Use a new, dedicated account.')
@minLength(2)
@maxLength(64)
param accountName string

@description('Speech processing region. Verify current MAI-Transcribe availability and your organization\'s approved regions before deployment.')
param location string = 'northeurope'

@description('Object ID of the Entra user in this subscription\'s tenant, not an application/client ID.')
@minLength(36)
@maxLength(36)
param userPrincipalObjectId string

var cognitiveServicesUserRoleId = 'a97b65f3-24c7-4388-baec-2e87135dc908'

resource speechAccount 'Microsoft.CognitiveServices/accounts@2025-06-01' = {
  name: accountName
  location: location
  kind: 'AIServices'
  sku: {
    name: 'S0'
  }
  properties: {
    customSubDomainName: accountName
    disableLocalAuth: true
    publicNetworkAccess: 'Enabled'
  }
}

resource speechUser 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(toLower(speechAccount.id), toLower(userPrincipalObjectId), cognitiveServicesUserRoleId)
  scope: speechAccount
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', cognitiveServicesUserRoleId)
    principalId: toLower(userPrincipalObjectId)
    principalType: 'User'
  }
}

@description('Resource-specific HTTPS endpoint for AZURE_SPEECH_ENDPOINT. No credentials are returned.')
output speechEndpoint string = speechAccount.properties.endpoint
