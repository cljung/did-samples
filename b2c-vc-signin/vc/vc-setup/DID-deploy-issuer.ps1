param (
    [Parameter(Mandatory=$true)][Alias('r')][string]$ResourceGroupName = "",        # RG - either existing or the name to create
    [Parameter(Mandatory=$true)][Alias('l')][string]$Location = "",                 # "West Europe" etc
    [Parameter(Mandatory=$true)][Alias('k')][string]$KeyVaultName = "",             # name of your KV resource
    [Parameter(Mandatory=$true)][Alias('s')][string]$StorageAccountName = "",       # name of your storage account - Nothing but aplhanumeric chars
    [Parameter(Mandatory=$false)][Alias('c')][string]$StorageAccountContainerName = "didstg",
    [Parameter(Mandatory=$true)][Alias('a')][string]$IssuerAppName = "",            # "VC Issuer"
    [Parameter(Mandatory=$false)][switch]$GenerateConfigFiles = $False,             # if to generate the sample json files
    [Parameter(Mandatory=$false)][Alias('v')][string]$VCType = ""                   # Only if you GenerateConfigFiles
    )

$ctx = Get-AzContext
if ( $null -eq $ctx ) {
    Connect-AzAccount 
    $ctx = Get-AzContext
}

# the user/admin who runs this script
$userObjectId = $ctx.Account.ExtendedProperties.HomeAccountId.Split(".")[0]
$userDomain = $ctx.Account.Id.Split("@")[1]

# Get the Enterprise App for VC's. The ApplicationId will be the same across all AAD tenants, the ObjectID will be unique for this tenant
$nameVCIS = "Verifiable Credentials Issuer Service"
$spVCIS = Get-AzADServicePrincipal -SearchString $nameVCIS 
if ( $null -eq $spVCIS ) {
    write-host "Enterprise Application missing in tenant : '$nameVCIS'`n`n" `
                "1) Make sure you are using an Azure AD P1/P2 tenant`n" `
                "2) Make sure you can see '$nameVCIS' as an Enterprise Application`n"
    exit 1
}
# create the VC Issuer app
$appSecret = [Convert]::ToBase64String( [System.Text.Encoding]::Unicode.GetBytes( (New-Guid).Guid ) ) # create an app secret
$SecureStringPassword = ConvertTo-SecureString -AsPlainText -Force -String $appSecret 
$uriName=$IssuerAppName.ToLower().Replace(" ", "")
$app = New-AzADApplication -DisplayName $IssuerAppName -IdentifierUris "https://$uriName" -ReplyUrls @("https://jwt.ms","vcclient://openid") -Password $SecureStringPassword
($app | New-AzADServicePrincipal) | out-null

$client_id = $app.ApplicationId
write-host "Application $IssuerAppName`nObjectID:`t$($app.ObjectId)`nAppID:`t`t$client_id`nSecret:`t`t$appSecret"

# your custom Issuer App
$spIssuer = Get-AzADServicePrincipal -SearchString $IssuerAppName

# create an Azure resource group
$rg = Get-AzResourceGroup -ResourceGroupName $ResourceGroupName -ErrorAction SilentlyContinue
if ( $null -eq $rg ) {
    $rg = New-AzResourceGroup -Name $ResourceGroupName -Location $Location
}

# deploy the ARM template (KeyVault + Storage Account)
$params = @{
    KeyVaultName = $KeyVaultName
    skuName = "Standard"
    AADTenantId = $ctx.Tenant.Id
    AdminUserObjectId = $userObjectId
    VerifiableCredentialsIssuerServicePrincipalObjectId = $spVCIS.Id
    IssuerAppServicePrincipalObjectId = $spIssuer.Id
    StorageAccountName = $StorageAccountName
    StorageAccountContainerName = $StorageAccountContainerName
}

$file = "$((get-location).Path)\DID-ARM-Template-Issuer.json"
New-AzResourceGroupDeployment -ResourceGroupName $ResourceGroupName -TemplateFile $file -TemplateParameterObject $params

# set the appropriate reader permission for the VS service principal on the storage account so that it can 
# read the Rules & Display json files
$scope = "/subscriptions/$($ctx.Subscription.Id)/resourcegroups/$ResourceGroupName/providers/Microsoft.Storage/storageAccounts/$StorageAccountName"
$roles = Get-AzRoleAssignment -Scope $scope
$roleName = "Storage Blob Data Reader"

if (!($roles | where {$_.DisplayName -eq $roleName})) {
    New-AzRoleAssignment -ObjectId $spVCIS.Id -RoleDefinitionName $roleName -Scope $scope     # Verifiable Credentials Issuer app
    New-AzRoleAssignment -ObjectId $userObjectId -RoleDefinitionName $roleName -Scope $scope  # you, the admin
}

if ( $GenerateConfigFiles) 
{
$didconfigJson = @"
{
    "azTenantId": "$($ctx.Tenant.Id)",
    "azClientId": "$client_id",
    "azClientSecret": "$appSecret",
    "kvVaultUri": "https://$KeyVaultName.$($ctx.Environment.AzureKeyVaultDnsSuffix)/",
    "kvSigningKeyId": "..update this...",
    "kvRemoteSigningKeyId" : "issuerSigningKeyIon- ... something in your KeyVault keys",
    "did": "did:ion: ... Issuer Identifier DID in the VC blade in portal.azure.com",
    "manifest": "https://beta.did.msidentity.com/v1.0/$($ctx.Tenant.Id)/verifiableCredential/contracts/$VCType"
}
"@
    Set-Content -Path "$((get-location).Path)\didconfig.json" -Value $didconfigJson

$rulesJson = @"
{
  "vc": {
    "type": [ "$VCType" ]
  },
  "validityInterval": 2592000,
  "attestations": {
    "idTokens": [
      {
        "mapping": {
          "displayName": { "claim": "name" },
          "oid": { "claim": "oid" },
          "tid": { "claim": "tid" },
          "username": { "claim": "preferred_username" },
          "lastName": { "claim": "family_name" },
          "firstName": { "claim": "given_name" },
          "country": { "claim": "ctry" }
        },
        "configuration": "https://login.microsoftonline.com/$($ctx.Tenant.Id)/v2.0/.well-known/openid-configuration",
        "client_id": "$client_id",
        "scope": "openid profile email",
        "redirect_uri": "vcclient://openid"
      }
    ]
  }
}
"@    
    Set-Content -Path "$((get-location).Path)\$($VCType)RulesFile.json" -Value $rulesJson

$displayJson = @"
{
  "default": {
    "locale": "en-US",
    "card": {
      "title": "$VCType",
      "issuedBy": "$userDomain",
      "backgroundColor": "#C0C0C0",
      "textColor": "#ffffff",
      "logo": {
        "uri": "https://$StorageAccountName.blob.$($ctx.Environment.StorageEndpointSuffix)/$($StorageAccountContainerName)public/logo.png",
        "description": "$VCType Logo"
      },
      "description": "Use your verified credential card to prove you are an $VCType empoyee."
    },
    "consent": {
      "title": "Do you want to get your $VCType Verified Credential card?",
      "instructions": "Sign in with your account to get your card."
    },
    "claims": {
      "vc.credentialSubject.firstName": {
        "type": "String",
        "label": "First name"
      },
      "vc.credentialSubject.lastName": {
        "type": "String",
        "label": "Last name"
      },
      "vc.credentialSubject.country": {
        "type": "String",
        "label": "Country"
      },
      "vc.credentialSubject.displayName": {
        "type": "String",
        "label": "displayName"
      },
      "vc.credentialSubject.username": {
        "type": "String",
        "label": "username"
      }
    }
  }
}
"@
    Set-Content -Path "$((get-location).Path)\$($VCType)DisplayFile.json" -Value $displayJson

}
