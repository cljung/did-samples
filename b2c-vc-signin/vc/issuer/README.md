# Sample Issuer

## Steps to deploy

You to have [node.js](https://nodejs.org/en/download/) installed locally on a development laptop.
The issuer needs to be accessable on a public endpoint as that is the way Microsoft Authenticator can talk to the service. An easy way to test this is via install [ngrok](https://ngrok.com/download) and let it proxy your localhost as a public endpoint.

1. git clone this repo
1. cd into vc\issuer
1. run `npm install`
1. Edit didconfig-b2c.json (see below)
1. Start ngrok in a separate command window `ngrok http 8081`
1. Start the sample issuer from a command window `node app.js` 

## Edit didconfig-b2c.json

The didconfig-b2c.json file looks like below.

```json
{
    "azTenantId": "The AAD tenantID that contains atleast one P1 license",
    "azClientId": "AppID of the app in that tenant that can access the KeyVault",
    "azClientSecret": "app key",
    "kvVaultUri": "https://your-keyvault-name.vault.azure.net/",
    "kvSigningKeyId": "sig_58...db",
    "kvRemoteSigningKeyId" : "issuerSigningKeyIon-...guid...",
    "did": "did:ion:...something...",
    "manifest": "https://beta.did.msidentity.com/v1.0/9885457a-2026-4e2c-a47e-32ff52ea0b8d/verifiableCredential/contracts/YourtenantMembership"
}
```

### Service Principal that can access Azure KeyVault
The first three items, `azTenantId`, `azClientId` and `azClientSecret` are about registering an application that has access to your Azure Key Vault mentioned in `kvVaultUri`. These values come from the initial config of Azure AD Verifiable Credentials in portal.azure.com.

If you did run the [ARM deployment script](../vc-setup/DID-deploy-issuer.ps1), it will generate a didconfig.json file where you can copy these values from.

### Other config entries
The values for `kvSigningKeyId`, `kvRemoteSigningKeyId`, `did` and `resolverEndpoint` can be found in portal.azure.com. For `kvRemoteSigningKeyId`, you should copy the middle part and not the full path.

![Issuer Portal info](/media/issuer-config-json.png)

The `kvSigningKeyId` can be found via browsing to `https://beta.discover.did.msidentity.com/1.0/identifiers/{your-did}`. The result will be a big JSON document, but next to `verificationMethod`, you will find your value (don't include the # character). 

## Test

Once you have ngrok and the sample issuer started, browse to the website using the ngrok url, launch the QR code and scan it using Microsoft Authenticator. If it the first time you do this in the Authenticator, you will not have the button `Credentials` at the bottom. You then need to do Add Account and select Other which will start the camera to scan the QR code.
