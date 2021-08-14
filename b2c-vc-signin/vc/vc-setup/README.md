# Verifiable Credentials Sample Issuer Deployment script

This is an ARM Template for deploying Azure resources that are needed to start issuing your own Verifiable Credentials from an Azure AD tenant. It will not register the application you need in your Azure AD B2C tenant.

In the screenshots below uses the name `ContosoEmployee`. For your B2C deployment you should consider using a name like `ContosoMembershipClub`, etc. 

## Make sure you have an Azure AD Premium tenant
First, you need to make sure that you are using Azure Active Directory P1 or P2. If you don't have a P1/P2, Verifiable Credentials will not work. You can find out if you have a P1/P2 in the overview section of the Acure Active Directory blade in portal.azure.com

## Verify you can see Enterprise Application Verifiable Credentials Issuer Service
Second, you need to make sure you see the application named `Verifiable Credentials Issuer Service` in the `Enterprise Application` section in the AAD blade. Switch to Type = `Microsoft Application` and search for it.
 
![Verifiable Credentials Issuer Service](/media/admin-screenshot-search-apps.png)

## Run the deployment script
In order to run this script, you need to have access to an Azure subscription that uses the intended Azure Active Directory as its authorative directory. If the subscription you intend to use is protected by another directory, you will not be able to issue credentials.

You need the [Azure Az](https://docs.microsoft.com/en-us/powershell/azure/new-azureps-module-az?view=azps-5.6.0) powershell module installed and a user account with permissions to both create the Azure Key Vault + Storage Accounts and register the sample VC Issuer application.

You should make sure that you can connect to your subscription via running

```powershell
Connect-AzAccount -SubscriptionId <your-subscription-guid>
```

Then run the below command after replacing the respective parameters with the names of your choice.

```powershell
.\DID-deploy-issuer.ps1 -ResourceGroupName "did-rg" -Location "West Europe" `
         -KeyVaultName "did-kv" -StorageAccountName "didstg" `
         -IssuerAppName "VC Issuer Sample" -VCType "ContosoEmployee" -GenerateConfigFiles 
```

**PLEASE NOTE** you need to change `KeyVaultName` and the `StorageAccountName` to be globally unique.

The script will:
- Register an app in Azure AD for your VC Issuer sample (the -IssuerAppName parameter). 
- Create the Resource Group, if it not already exists
- Deploy the ARM template which will create:
    - An Azure Key Vault instance and create Access Policies for you as an admin and for the `Verifiable Credentials Issuer Service` app
    - An Azure Storage Account with one private container and one public. The private will later store your Rules & Display file while the public will host your VC card's logo image.
- Assign the permission `Storage Blob Data Reader` to the `Verifiable Credentials Issuer Service` service principal for the storage account so that it can read the Rules & Display files.
- Generate template json files:
    - didconfig.json - used by the VC Issuer sample (you need to update this file after completing the below steps)
    - Rules file - A file that binds your VC Issuar Sample app to your Azure AD tenant. The generated Rules file contains some extra claims to illustrate how that can be done
    - Display file - The visual design of your VC card that controls how it is displayed in the Microsoft Authenticator app.

## Register your organization for Verifiable Credentials and set your KeyVault

Next step is to complete the Verifiable Credentials configuration in portal.azure.com in the [VC blade](https://portal.azure.com/?Microsoft_AAD_DecentralizedIdentity=preview#blade/Microsoft_AAD_DecentralizedIdentity/InitialMenuBlade/cardsListBlade).

![Getting Started](/media/admin-screenshot-vc-getting-started.png)

In the `Getting Started` section, you define
- Organization name
- Domain
- Key Vault - here you need to select the Key Vault instance that was created above. Since the ARM Template gave you and the `Verifiable Credentials Issuer Service` app the permissions needed, this should work. If it doesn't, it is a permission problem and you need to check the Access Policies on Key Vault.

In the `Credentials` section, you configure the Verifiable Credential you want to issue. The name you give to the credential should match the `-VCType` parameter you used when you invoked the powershell script above.

![Getting Started](/media/admin-screenshot-create-credential.png)

## Design and Upload your VC Rules and Display files to your Azure Storage Account

Open the Rules & Display json files and make any changes you see needed. This may include adding or removing any claims from your Azure AD tenant that you would like to include in the VC. In the display file, you might also want to specify your color and logo, etc.

When you are done, go to the `Properties` part in the `VC Blade` in portal.azure.com and upload the respective files. If you later make changes, you can upload them again, but if you do, it will take a few minutes for the changes to be picked up. You can use the `Issuer Credential URL` and open it in a browser to see if your added claims are there.

## Clone the Issuer sample, change config and run it

Finally, it is time to git clone the [VC Issuer sample](https://github.com/Azure-Samples/active-directory-verifiable-credentials) and follow the instructions for how to get that up and running. The `didconfig.json` template file that the deployment script generated should replace the file in the [issuer/didconfig.json](https://github.com/Azure-Samples/active-directory-verifiable-credentials/blob/main/issuer/didconfig.json) file. 

![Getting Started](/media/admin-screenshot-issuer-details.png)

There are three additional changes you need to do:
- kvSigninKeyId - see explanation below
- kvRemoteSigningKeyId - You find this in the Azure Key Vault instance you created under the `Keys` section. After you connected your VC instance to KeyVault, it creates three keys. The one to copy has a name that starts with `issuerSigningKeyIon-...`.
- did - You will find the value to use here in the Overview section of your Credential in the `VC Blade` labeled as `Issuer Identifier`. It is a long string that starts with `did:ion:`

To get your `kvSigninKeyId`, you can do the following
- Copy the `Issuer Identifier` (long string that starts with did:ion:...)
- Open the browser and navigate to `https://beta.discover.did.microsoft.com/1.0/identifiers/did:ion:....`
- Find the `verificationMethod` attribute and pick the next id, which looks like `#sig_123abc45`. Copy that value without the `#` char and use that