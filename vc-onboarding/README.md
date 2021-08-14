# VC Onboarding sample
This sample is an ASPNet core webapp that uses Azure AD B2C as its authentication authority. The signed in user can issue him/herself an Azure AD verifiable Credential. The claims will come from the user's claims, like displayName, and some other claims are directly added by the user in the web page, like title and prefered language.

## Live sample deployment
There is a live sample deployment that you can test [here](https://cljung-did-idtokenhint.azurewebsites.net/). Since it is B2C, you can use the self-service sign-up functionality and create your own account. Then you can issue yourself a VC.

## Updating appsettings.json
The app's config file contains two distinct sections that needs to be updated - one for Azure AD B2C and the other for Azure AD Verifiable Credentials.

The Azure AD B2C section looks like below. It is standard B2C stuff, which means you need to replace `yourtenant` with the name of your B2C tenant. You also need to add the names of your B2C policies. The `ClientId` is the B2C App Registration and it needs to have your deployments url as a valid redirect_uri. For the live sample app, this value is `https://cljung-did-idtokenhint.azurewebsites.net/signin-oidc`.

```json
  "AzureAdB2C": {
    "Instance": "https://yourtenant.b2clogin.com/tfp",
    "ClientId": "<YOURCLIENTID-B2C>",
    "Domain": "yourtenant.onmicrosoft.com",
    "SignedOutCallbackPath": "/signout/B2C_1_susi",
    "SignUpSignInPolicyId": "B2C_1_susi",
    "ResetPasswordPolicyId": "B2C_1_PasswordReset",
    "EditProfilePolicyId": "B2C_1_ProfileEdit",
    "CallbackPath": "/signin-oidc"
  },
```

The other section controls Verifiable Credential settings. You need to update `TenantId`, `ClientId`, `ClientSecret`, and `DidManifest`. For the first three, please see the [Adding Authorization](https://github.com/cljung/did-samples/#adding-authorization) for background on what they represent, but they are a way of creating an access token, via client credentials, that authorize us to the VC Client API. The `DidManifest` is the url to your VC credential.

```json
  "AppSettings": {
    "ApiEndpoint": "https://beta.did.msidentity.com/v1.0/{0}/verifiablecredentials/request",
    "TenantId": "<YOURTENANTID-THAT-PROTECTS-AZUREKEYVAULT-FOR-DID>",
    "Authority": "https://login.microsoftonline.com/{0}",
    "scope": "bbb94529-53a3-4be5-a069-7eaf2712b826/.default",
    "ClientId": "<YOURCLIENTID-DID>",
    "ClientSecret": "<YOURCLIENTSECRET-DID>",
    "ApiKey": "not used",
    "CookieKey": "state",
    "CookieExpiresInSeconds": 7200,
    "CacheExpiresInSeconds": 300,
    "client_name": "VC MSAL B2C Onboarding",
    "DidManifest": "<YOUR-DID-MANIFEST-URL>"
  }

```

## Running the sample
The sample is self contained and contains the callback APIs that the VC Client API uses (see the ApiVCController.cs). That means that the webapp needs a public endpoint and will not work with localhost. If you want to get something running fast, you can use `ngrok` as an temp App Proxy.