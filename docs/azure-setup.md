# Provision your own Azure Speech service

Tiny Transcriber sends recorded audio to **your own Azure AI Services account**.
This guide uses **Azure CLI + Bicep**, with the same Microsoft Entra authentication
flow as the app. It does not create or distribute a shared service or credentials.

## What is deployed

- One resource group, **only if the requested group does not exist**.
- One dedicated `Microsoft.CognitiveServices/accounts` resource, kind `AIServices`,
  SKU **S0**, using the supported stable management API `2025-06-01`.
- A custom subdomain matching the account name, so the resource-specific endpoint
  supports Entra authentication.
- `disableLocalAuth: true`, with no switch to enable API keys.
- One **Cognitive Services User** assignment
  (`a97b65f3-24c7-4388-baec-2e87135dc908`) for your Entra **user object ID**, scoped
  to **this account only**, not the resource group or subscription.

The Bicep deployment is resource-group-scoped. Its only output is the HTTPS
endpoint; neither the template nor the script retrieves or outputs keys or tokens.
The role assignment name is deterministic for the account, user, and role.

The app requests a token through `DefaultAzureCredential` for
`https://cognitiveservices.azure.com/.default`. For a local installation, sign in
with `az login` as the user granted the role. Credentials from an earlier source
in the default credential chain, such as configured environment credentials or
an IDE login, can take precedence; make sure they represent the intended identity.

The app calls
`POST /speechtotext/transcriptions:transcribe?api-version=2025-10-15`, selecting
**MAI-Transcribe-2** through `enhancedMode` with the `clean` transcription style.
It omits `locales` for automatic Czech/English language detection. **No separate
model deployment, Foundry project, or text-editing model is needed.**

> MAI-Transcribe-2 is currently documented as **public preview**, without an SLA
> and not recommended by Microsoft for production workloads. A public app
> release does not change that service status. Check the current
> [MAI-Transcribe documentation](https://learn.microsoft.com/azure/ai-services/speech-service/mai-transcribe)
> and preview terms before relying on it.

## Costs, region, and privacy

- **S0 is a paid tier**, not an F0/free-tier deployment. Transcription usage is
  billed to the subscription you supply; account provisioning does not grant
  unlimited or free transcription. Review current
  [Speech pricing](https://azure.microsoft.com/pricing/details/cognitive-services/speech-services/)
  for MAI/enhanced transcription and your agreement before deployment. Configure
  budget alerts and monitor usage; alerts are not a hard spending cap.
- The default Speech location is `northeurope`. Microsoft's
  [MAI region table](https://learn.microsoft.com/azure/ai-services/speech-service/regions?tabs=llmspeech)
  lists it as supported (checked **2026-09-14**). Availability, subscription
  eligibility, quotas, and organizational policies can still restrict deployment.
  Check that specific table, not just general Speech or Azure AI availability,
  before choosing another region.
- Microsoft documents Speech processing/storage within the Speech resource's
  region. Your audio leaves the device and is processed by Azure; choose an
  approved region and obtain any required consent. Review the
  [Speech privacy guidance](https://learn.microsoft.com/azure/foundry/responsible-ai/speech-service/speech-to-text/data-privacy-security)
  and preview terms for your data. The resource group's metadata region is
  separate and **does not select the audio-processing region**.
- This minimal template uses a **publicly reachable HTTPS endpoint with Entra
  authorization**, not anonymous access. Do not publish your personal endpoint,
  tenant/user identifiers, audio, transcripts, access tokens, or API keys in
  issues, screenshots, release ZIPs, or configuration committed to Git.
  An endpoint is not a secret credential, but it is still resource information.
  Every user should provision their own account.
- If your organization requires private endpoints, network restrictions,
  customer-managed encryption, or mandatory tags, have your administrator adapt
  and approve the infrastructure first. These extras are not provisioned here.
  Do not disable policy, create exemptions, enable keys, or move data to an
  unapproved region to work around a denial.

## Prerequisites and permissions

1. Windows PowerShell **5.1+** or PowerShell **7**, plus the
   [Azure CLI](https://learn.microsoft.com/cli/azure/install-azure-cli-windows)
   on `PATH`. The app's Azure CLI credential also needs `az` available.
2. A working Bicep CLI managed by Azure CLI:

   ```powershell
   az version
   az bicep version
   ```

   Check each command's exit status. If the Bicep check reports that the compiler
   is missing, install it using your organization's approved process, for example
   `az bicep install`, and check again. The provisioning script **does not install
   or upgrade tools**; it checks Bicep and compiles before any Azure writes.
3. An Azure subscription and an Entra user in its tenant.
   `Microsoft.CognitiveServices` must already be registered in that subscription.
   The script checks registration but never registers providers itself.
4. Provisioning permissions:
   - Creating a new group requires
     `Microsoft.Resources/subscriptions/resourceGroups/write` at subscription
     scope. Have an administrator create the group if you should only manage
     resources inside it.
   - Resource deployment requires deployment and Cognitive Services account
     read/write permissions on the target group, normally **Contributor** on
     that group. The wrapper also reads the subscription's provider registration
     and resource-group existence.
   - Assigning the role requires
     `Microsoft.Authorization/roleAssignments/write` at the account or an
     ancestor scope, normally **Role Based Access Control Administrator** or
     **User Access Administrator** in addition to Contributor, or **Owner**.
     Any assignment conditions must allow this role and user. Request only the
     minimum scope your administrator approves.
   - **Contributor alone cannot grant roles. Cognitive Services User alone
     cannot provision this infrastructure.** The app user only needs the
     account-scoped inference role after provisioning; it does not need Owner.
   - Automatic user lookup uses Microsoft Graph through
     `az ad signed-in-user show`. If that lookup is unavailable, or the deploying
     identity is a service principal, pass the target user's object ID explicitly.

The built-in Cognitive Services User role includes Cognitive Services data
permissions and some control-plane reads, including key listing in its role
definition. It is not a custom transcription-only role. Its assignment is limited
to the dedicated account; local/key authentication remains disabled, and this
workflow never invokes a key-list operation.

## Sign in and choose your parameters

From either the repository root or the extracted release ZIP root:

```powershell
az login --tenant '<your-tenant-guid>'
if ($LASTEXITCODE -ne 0) { throw 'Azure login failed.' }

$subscriptionId = '<your-subscription-guid>'
$resourceGroup = 'rg-tiny-transcriber'
$accountName = '<your-globally-unique-account-name>'
$location = 'northeurope'
```

Replace every placeholder. Account names must be 2-64 lowercase letters, digits,
or hyphens, starting and ending with a letter or digit. The name is also the
globally unique custom subdomain. Use a **new, dedicated account**, not a shared
existing resource: this template manages its authentication and networking.

`-SubscriptionId`, `-ResourceGroupName`, and `-AccountName` are required.
`-Location` defaults to `northeurope`. Optional `-ResourceGroupLocation` selects
the metadata region for a **new** resource group; existing group metadata is
left alone. Both locations must comply with policy.

By default the role is assigned to the signed-in Azure CLI user. Because
`az ad signed-in-user show` does not accept `--subscription`, the wrapper first
checks that the active CLI tenant matches the explicitly selected subscription's
tenant. It stops on a mismatch rather than assigning an unrelated user's object
ID. It **never calls `az account set`**. Alternatively, pass
`-UserPrincipalObjectId '<user-object-guid-in-the-subscription-tenant>'`. This is
the user's **object ID**, not a client/application ID. For a guest, use the guest
user's object ID in the resource tenant, not their home-tenant object ID.

## Preview before deploying

The script's built-in `-WhatIf` is a **local plan only**: no Azure CLI commands,
resource changes, identity lookup, or compilation are performed. It does not
validate cloud permissions, region availability, or Azure Policy.

```powershell
.\tools\Deploy-Speech.ps1 -SubscriptionId $subscriptionId `
    -ResourceGroupName $resourceGroup -AccountName $accountName `
    -Location $location -WhatIf
```

For an **Azure-evaluated Bicep preview**, use the following against an
**already-existing, approved resource group**. Creating that group is a real
write and is not part of this preview. Supply the target user object ID:

```powershell
$userObjectId = '<user-object-guid-in-the-subscription-tenant>'
az deployment group what-if --subscription $subscriptionId `
    --resource-group $resourceGroup --name $accountName `
    --template-file .\infra\main.bicep --mode Incremental `
    --parameters "accountName=$accountName" "location=$location" "userPrincipalObjectId=$userObjectId"
if ($LASTEXITCODE -ne 0) { throw 'Azure what-if failed; no deployment was requested.' }
```

Azure what-if reports predicted changes; **it is not a deployment**, a guarantee
of success, or an end-to-end transcription test. It requires deployment
permissions and can report unresolved expressions. Inspect all proposed changes.

## Deploy and configure the app

The following **creates billable Azure resources and grants the described role**:

```powershell
.\tools\Deploy-Speech.ps1 -SubscriptionId $subscriptionId `
    -ResourceGroupName $resourceGroup -AccountName $accountName `
    -Location $location
```

If an administrator is provisioning for you, add
`-UserPrincipalObjectId '<your-user-object-guid>'`. If your approved group
metadata region differs, add `-ResourceGroupLocation '<approved-region>'`.
Keep `tools` and `infra` as sibling folders; the script resolves the template
relative to itself, not your current directory.

The script stops on **every failed Azure CLI command**. It does not automatically
roll back or delete resources: a failure can leave a group or account created
before a later step failed. Inspect the deployment before retrying. Reusing the
same account, user, and group reuses the deterministic role assignment. Changing
the user creates another assignment; incremental deployments **do not remove**
the old user's role. Have an administrator review intentional access changes.

After successful deployment, copy the printed endpoint. **The script does not
modify any environment setting.** To save it for your Windows user, explicitly
run the printed command, or substitute your endpoint here:

```powershell
[Environment]::SetEnvironmentVariable(
    'AZURE_SPEECH_ENDPOINT',
    'https://<your-account-name>.cognitiveservices.azure.com/',
    'User')
```

Completely exit and restart Tiny Transcriber and any terminal or launcher used
to start it so they inherit the new environment. For immediate use in the
current PowerShell window, set `$env:AZURE_SPEECH_ENDPOINT` to the same endpoint
and launch the app from that window. The value must be an absolute **HTTPS base
endpoint**, not a regional endpoint or the transcription API path.

Sign in with `az login --tenant '<your-tenant-guid>'` **on the app user's machine
as the user granted access**, and check that login succeeds. Leave
`AZURE_SPEECH_KEY` unset. The app retains API-key precedence for compatibility,
so a stale key setting can override Entra authentication and fail against this
key-disabled account. Remove stale key settings through your normal environment
configuration process; never enable keys to work around this.

Allow time for RBAC propagation, then test with a short, non-sensitive recording.
That test sends audio to Azure and may incur usage charges.

## Troubleshooting

| Symptom | Checks and next steps |
| --- | --- |
| `az` or Bicep missing | Install the prerequisite through your approved process, reopen the terminal, and check `az version` / `az bicep version`. No tool installation is performed by the script. |
| Tenant mismatch or user lookup failure | Sign in to the subscription's tenant; check your CLI identity. For Graph restrictions or deployment by an administrator/service principal, provide the intended user's tenant-local object ID explicitly. |
| Provisioning `AuthorizationFailed` or role-assignment 403 | Verify the explicit subscription, deployment permissions, role-assignment write permission, and any PIM activation or role-assignment conditions. Contributor does not include role assignment. Do not grant the app subscription-wide access. |
| `RequestDisallowedByPolicy`, denied RG location, tags, or networking | Read the failing policy/assignment details from the Azure error. Ask the administrator for compliant settings/infrastructure. Group metadata and Speech account location are independently evaluated. No policy exemptions or arbitrary fallback locations are created. |
| Account/custom subdomain already used, or `RoleAssignmentExists` | Choose a genuinely new account name, or have an administrator review the existing resource/assignment. An assignment created elsewhere with a different GUID can conflict; do not automatically delete or broaden access. |
| Region, SKU, quota, or model-unavailable error | Check the current **MAI-Transcribe** region table, S0 eligibility, and subscription quota. Generic Speech availability does not guarantee MAI support. Select another region only if approved for your data; the script never falls back automatically. |
| App 401 | Check `az login`, tenant, token identity, and the HTTPS custom-subdomain endpoint. Check for a stale `AZURE_SPEECH_KEY` or another credential taking precedence. The token scope is `https://cognitiveservices.azure.com/.default`; never paste a token into the endpoint setting. |
| App 403 | Check Cognitive Services User on the exact account for the identity actually used by the app, allow several minutes for propagation, and check network/firewall or tenant access restrictions. A management-plane role alone may not grant inference. |
| App still reports missing endpoint | Restart the app and its launching process, or set the process environment explicitly before launching. Saving a user-level variable does not change an already-running process. |
| Deployment failed after creating resources | Review the named deployment in the requested group, correct the approved settings/permissions, and rerun the same parameters. Nothing is automatically deleted or rolled back. |

Stop using the app when it is no longer needed and review Azure usage and
resources with your administrator. Delete only resources you own through your
approved cleanup process; do not delete a reused group containing other workloads.

## Official references

- [MAI-Transcribe-2 configuration and preview status](https://learn.microsoft.com/azure/ai-services/speech-service/mai-transcribe)
- [Speech regional availability and data location](https://learn.microsoft.com/azure/ai-services/speech-service/regions?tabs=llmspeech)
- [LLM Speech and Entra authentication](https://learn.microsoft.com/azure/ai-services/speech-service/llm-speech)
- [Accounts Bicep schema, API 2025-06-01](https://learn.microsoft.com/azure/templates/microsoft.cognitiveservices/2025-06-01/accounts)
- [Cognitive Services User role definition](https://learn.microsoft.com/azure/role-based-access-control/built-in-roles/ai-machine-learning#cognitive-services-user)
- [Role-assignment prerequisites](https://learn.microsoft.com/azure/role-based-access-control/role-assignments-steps)
- [Bicep what-if](https://learn.microsoft.com/azure/azure-resource-manager/bicep/deploy-what-if)
