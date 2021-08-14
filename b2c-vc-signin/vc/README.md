# Verifiable Credentials Issuer and Verifier 

## How to issue your own credentials from Azure AD B2C

You need to follow setup procedure for enabling Verifiable Credentials in your Azure AD Premium tenant. The docs can be found [here](https://didproject.azurewebsites.net/docs/issuer-setup.html). You can also use the ARM template located in the [vc-setup](./vc-setup) folder to create Azure resources needed for the VC issuance.

Note that you will not use this Azure AD tenant as the Identity Provider for the issuing of the VC. You just need to configure it in AAD due to license reasons. 

When you come to the part where you edit the Display and the Rules file, you must have your Azure AD B2C tenant ready, because Rules file contains a reference to the well-known openid-configuration. You can either setup a simple User Flow that can be used during issuance of the VC, or you use the [SignUpOrSignin.xml](/b2c/SignUpOrSignin.xml) custom policy in this repo.

For the Display and the Rules file, there are alot of `Yourtenant` everywhere which you can translate into what you want to call your VC card.

## Issuer

The issuer folder contains the node.js sample issuer app and once you have completed the above configuration, you can continue to deploy the sample issuer.

## Verifier 

The verifier folder contains the node.js sample verifier and you can start deploying it after you are done with the issuer. You will be able to test scanning the QR code without uploading the B2C Custom Policies.

## Display file changes

The Display file don't need that many changes as the Rules file. You should change `title` to give your VC a proper name. This name will appear in multiple locations in other files and it will have a displayName, like `Yourname Membership` and an identifier name, like `YournameMembership`, where there is no space separator. Whatever name you choose, make sure you do search and replace and update all places.

The `issuedBy` attribute is a technical name and can be the name of your B2C tenant, but it is just a piece of information in visual display of a VC.

The `logo` is a uri to the logotype that will be displayed in the Microsoft Authenticator when viewing the VC.

```json
{
    "default": {
      "locale": "en-US",
      "card": {
        "title": "Yourname Membership",
        "issuedBy": "yourtenant",
        "backgroundColor": "#B8CEC1",
        "textColor": "#ffffff",
        "logo": {
          "uri": "https://yourstorageaccount.blob.core.windows.net/uxcust/templates/images/snoopy-small.jpg",
          "description": "yourtenant Logo"
        },
        "description": "Use your verified credential card to prove you are a yourtenant member."
      },
      "consent": {
        "title": "Do you want to get your yourtenant membership card?",
        "instructions": "Sign in with your account to get your card."
      },
...
```

## Rules file changes

The `vc.type` is the technical name of your Verifiable Credential. The `mapping` is what claims in thhe identity Providers id_token that you want to capture and persist in the VC being issued. In the below example, we capture the user's objectId and tenantId from the B2C tenant, which is the way we later know which B2C account this is when we present the VC.

The `configuration` attribute is the well-known openid metadata endpoint. Microsoft Authenticator uses this when it is time to authenticate the user during the issuance of the VC. For Azure AD B2C, the `scope` value can be just `openid`, but for other IDPs, like AAD, you might need to specify `openid email profile` to get the claims you need.

The `client_id` is the Application Registration in the B2C tenant you have created. It needs to be a Public client (Mobile and desktop applications) with the redirect_uri set to `vcclient://openid`.
 
```json
{
  "vc": {
    "type": [ "YourtenantMembership" ]
  },
  "validityInterval": 2592000,
  "attestations": {
    "idTokens": [
      {
        "mapping": {
          "displayName": { "claim": "name" },
          "sub": { "claim": "sub" },
          "tid": { "claim": "tid" },
          "username": { "claim": "email" },
          "lastName": { "claim": "family_name" },
          "firstName": { "claim": "given_name" },
          "country": { "claim": "ctry" }
        },
        "configuration": "https://yourtenant.b2clogin.com/yourtenant.onmicrosoft.com/B2C_1_susi/v2.0/.well-known/openid-configuration",
        "client_id": "your b2c AppReg client_id / AppID",
        "scope": "openid",
        "redirect_uri": "vcclient://openid"
      }
    ]
  }
}
```