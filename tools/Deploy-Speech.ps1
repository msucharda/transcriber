#requires -Version 5.1
<#
.SYNOPSIS
Provisions a dedicated, keyless Azure AI Services account for Tiny Transcriber.
.DESCRIPTION
Uses Azure CLI and the sibling infra\main.bicep. Creates the requested resource
group only if missing, then deploys an S0 account and an account-scoped Cognitive
Services User assignment. Never changes the active subscription or environment
settings. See docs\azure-setup.md for costs, permissions, and Azure what-if.
.PARAMETER SubscriptionId
Explicit Azure subscription GUID. Every subscription-scoped operation uses it.
.PARAMETER ResourceGroupName
Resource group to create or reuse. Existing group metadata is not modified.
.PARAMETER AccountName
Globally unique lowercase account name, also used as the custom subdomain.
.PARAMETER Location
Speech processing region. Defaults to northeurope; verify MAI region availability.
.PARAMETER ResourceGroupLocation
Metadata region for a new resource group. Defaults to Location; ignored if the
group already exists. This does not change the Speech processing region.
.PARAMETER UserPrincipalObjectId
Entra user object ID in the subscription's tenant. Defaults to the signed-in CLI
user, but only when the CLI's active tenant matches the selected subscription.
.EXAMPLE
.\tools\Deploy-Speech.ps1 -SubscriptionId '<subscription-guid>' `
    -ResourceGroupName 'rg-tiny-transcriber' -AccountName '<unique-account-name>'
.EXAMPLE
.\tools\Deploy-Speech.ps1 -SubscriptionId '<subscription-guid>' `
    -ResourceGroupName 'rg-tiny-transcriber' -AccountName '<unique-account-name>' -WhatIf
#>
[CmdletBinding(SupportsShouldProcess = $true)]
param(
    [Parameter(Mandatory = $true)]
    [ValidatePattern('^[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}$')]
    [string]$SubscriptionId,

    [Parameter(Mandatory = $true)]
    [ValidateLength(1, 90)]
    [ValidatePattern('^[a-zA-Z0-9](?:[a-zA-Z0-9_.()-]*[a-zA-Z0-9_()-])?$')]
    [string]$ResourceGroupName,

    [Parameter(Mandatory = $true)]
    [ValidatePattern('^[a-z0-9][a-z0-9-]{0,62}[a-z0-9]$')]
    [string]$AccountName,

    [ValidatePattern('^[a-z][a-z0-9]+$')]
    [string]$Location = 'northeurope',

    [ValidatePattern('^[a-z][a-z0-9]+$')]
    [string]$ResourceGroupLocation,

    [ValidatePattern('^[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}$')]
    [string]$UserPrincipalObjectId
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
# Check exit codes explicitly on both Windows PowerShell and PowerShell 7.
$PSNativeCommandUseErrorActionPreference = $false

function Invoke-AzureCli {
    param([Parameter(Mandatory = $true)][string[]]$Arguments)

    $output = & $script:AzureCliCommand @Arguments --only-show-errors
    $exitCode = $LASTEXITCODE
    if ($exitCode -ne 0) {
        throw "Azure CLI failed with exit code ${exitCode}: az $($Arguments[0]) $($Arguments[1]). See the CLI error above; no further operations were attempted."
    }
    return ($output -join [Environment]::NewLine).Trim()
}

$templatePath = Join-Path (Split-Path -Parent $PSScriptRoot) 'infra\main.bicep'
if (-not (Test-Path -LiteralPath $templatePath -PathType Leaf)) {
    throw "Bicep template not found: $templatePath. Keep tools and infra as sibling directories when extracting the release ZIP."
}
if ([string]::IsNullOrWhiteSpace($ResourceGroupLocation)) {
    $ResourceGroupLocation = $Location
}

$target = "subscription $SubscriptionId, resource group $ResourceGroupName, account $AccountName in $Location"
$operation = 'Create the resource group if missing, deploy a billable S0 AIServices account with local authentication disabled, and grant its Cognitive Services User role to the selected user'
if (-not $PSCmdlet.ShouldProcess($target, $operation)) {
    return
}

$script:AzureCliCommand = Get-Command az -ErrorAction Stop
[void](Invoke-AzureCli -Arguments @('bicep', 'version'))
[void](Invoke-AzureCli -Arguments @('bicep', 'build', '--file', $templatePath, '--stdout'))

$tenantId = Invoke-AzureCli -Arguments @(
    'account', 'show', '--subscription', $SubscriptionId, '--query', 'tenantId', '--output', 'tsv'
)
if ([string]::IsNullOrWhiteSpace($tenantId)) {
    throw 'The selected subscription did not return a tenant ID. Run az login for its tenant and retry.'
}

$principalObjectId = $UserPrincipalObjectId
if ([string]::IsNullOrWhiteSpace($principalObjectId)) {
    # az ad signed-in-user show is tenant-scoped and does not accept --subscription.
    $activeTenantId = Invoke-AzureCli -Arguments @('account', 'show', '--query', 'tenantId', '--output', 'tsv')
    if ($activeTenantId -ne $tenantId) {
        throw "The active CLI tenant differs from the selected subscription's tenant ($tenantId). Sign in to that tenant with az login --tenant $tenantId, or supply -UserPrincipalObjectId from that tenant."
    }
    $principalObjectId = Invoke-AzureCli -Arguments @('ad', 'signed-in-user', 'show', '--query', 'id', '--output', 'tsv')
}

$parsedObjectId = [guid]::Empty
if (-not [guid]::TryParseExact($principalObjectId, 'D', [ref]$parsedObjectId) -or $parsedObjectId -eq [guid]::Empty) {
    throw 'Expected a nonempty Entra user object ID (GUID). Supply -UserPrincipalObjectId from the selected subscription tenant.'
}
$principalObjectId = $parsedObjectId.ToString()

$providerState = Invoke-AzureCli -Arguments @(
    'provider', 'show', '--subscription', $SubscriptionId,
    '--namespace', 'Microsoft.CognitiveServices', '--query', 'registrationState', '--output', 'tsv'
)
if ($providerState -ne 'Registered') {
    throw 'Microsoft.CognitiveServices is not registered in the selected subscription. Ask your subscription administrator to register it through your approved process, then retry. The script does not register providers.'
}

$groupExists = Invoke-AzureCli -Arguments @(
    'group', 'exists', '--subscription', $SubscriptionId, '--name', $ResourceGroupName, '--output', 'tsv'
)
if ($groupExists -notin @('true', 'false')) {
    throw "Unexpected resource-group existence response: '$groupExists'. No deployment was attempted."
}
if ($groupExists -eq 'false') {
    [void](Invoke-AzureCli -Arguments @(
        'group', 'create', '--subscription', $SubscriptionId,
        '--name', $ResourceGroupName, '--location', $ResourceGroupLocation, '--output', 'none'
    ))
}

$endpoint = Invoke-AzureCli -Arguments @(
    'deployment', 'group', 'create', '--subscription', $SubscriptionId,
    '--resource-group', $ResourceGroupName, '--name', $AccountName,
    '--template-file', $templatePath, '--mode', 'Incremental',
    '--parameters', "accountName=$AccountName", "location=$Location", "userPrincipalObjectId=$principalObjectId",
    '--query', 'properties.outputs.speechEndpoint.value', '--output', 'tsv'
)

$endpointUri = $null
if (-not [uri]::TryCreate($endpoint, [UriKind]::Absolute, [ref]$endpointUri) -or
    $endpointUri.Scheme -ne 'https' -or
    $endpointUri.Host -ne "$AccountName.cognitiveservices.azure.com" -or
    -not $endpointUri.IsDefaultPort -or
    $endpointUri.UserInfo -ne '' -or $endpointUri.Query -ne '' -or $endpointUri.Fragment -ne '' -or
    $endpointUri.AbsolutePath -ne '/') {
    throw 'Deployment returned no valid resource-specific Speech HTTPS endpoint. Inspect the deployment outputs; do not use a regional endpoint, API path, or credential as AZURE_SPEECH_ENDPOINT.'
}
$endpoint = $endpointUri.AbsoluteUri

Write-Host "Speech endpoint: $endpoint"
Write-Host 'No environment settings were changed. To persist the endpoint for your Windows user, run:'
Write-Host "[Environment]::SetEnvironmentVariable('AZURE_SPEECH_ENDPOINT', '$endpoint', 'User')"
Write-Host 'Then restart Tiny Transcriber and its launching terminal. Use az login as the user granted access, and leave AZURE_SPEECH_KEY unset.'
Write-Output $endpoint
