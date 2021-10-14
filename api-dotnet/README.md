# client-api-test-service-dotnet
Azure AD Verifiable Credentials ASP.Net Core 3.1  sample that uses the new private preview VC Client APIs.
This sample is updated to work with version v0.3 which introduces authorization and the use of an access token when calling the API. Please read the section on [Adding Authorization](https://github.com/cljung/did-samples#adding-authorization) to see what additions you need to do.

The latest version is changed to use dotnet classes to represent the issuance and presentation requests that are being sent to the VC Client API. You'll find the Models in the [VCClientApiModels.cs](Models/VCClientApiModels.cs) 
file and you can see them in use in the Controllers [ApiVerifierController.cs](ApiVerifierController.cs) and [ApiIssuerController.cs](ApiIssuerController.cs). 
The json files in the [requests](requests/) folder are not used anymore but are kept in the repo for reference.

## Two modes of operations
This sample can work in two ways:
- As a standalone WebApp with it's own web UI that let's you issue and verify DID Verifiable Credentials.
- As an API service that works in combination with Azure AD B2C in order to use VCs as a way to authenticate to B2C.

## VC Client API, what is that?

Please read the top level [README.md](https://github.com/cljung/did-samples) for an introduction.

## Running the sample

### Updating appsettings.json config file

This sample now has all its configuration in the [appsettings.json](appsettings.json) file and you need to update it before you run the app.

In the [appsettings.json](appsettings.json) file, there are a few settings, but the ones listed below needs your attention.
First, `TenantId`, `ClientId` and `ClientSecret` are used to acquire an `access_token` so you can authorize the VC Client API to your Azure Key Vault.
This is explained in the outer [README.md](../README.md#adding-authorization) file under the section `Adding Authorization`. 

The remaining five settings control what VC credential you want to issue and present. 

```JSON
    "TenantId": "",
    "ClientId": "",
    "ClientSecret": "",

    "VerifierAuthority": "did:ion:...your DID...",
    "IssuerAuthority": "did:ion:EiD_ZULUvK_3eTfWfq97cc87DeU8J0AEzIaSuFLRAHzXoQ:eyJkZWx0YSI6eyJwYXRjaGVzIjpbeyJhY3Rpb24iOiJyZXBsYWNlIiwiZG9jdW1lbnQiOnsicHVibGljS2V5cyI6W3siaWQiOiJzaWdfY2FiNjVhYTAiLCJwdWJsaWNLZXlKd2siOnsiY3J2Ijoic2VjcDI1NmsxIiwia3R5IjoiRUMiLCJ4IjoiU21xMVZNNXp0RUVpZGpoWE5uckxub3N5TkI2MEVaV05CWXdUY3dQazU3YyIsInkiOiJSeFd3QlNyQjBaWl9MdndKVGpMamRqUEtTMXlZQjgzOUZIckFWUm9EYW9nIn0sInB1cnBvc2VzIjpbImF1dGhlbnRpY2F0aW9uIiwiYXNzZXJ0aW9uTWV0aG9kIl0sInR5cGUiOiJFY2RzYVNlY3AyNTZrMVZlcmlmaWNhdGlvbktleTIwMTkifV0sInNlcnZpY2VzIjpbeyJpZCI6ImxpbmtlZGRvbWFpbnMiLCJzZXJ2aWNlRW5kcG9pbnQiOnsib3JpZ2lucyI6WyJodHRwczovL3ZjYXBpZGV2Lndvb2Rncm92ZWRlbW8uY29tLyJdfSwidHlwZSI6IkxpbmtlZERvbWFpbnMifV19fV0sInVwZGF0ZUNvbW1pdG1lbnQiOiJFaUIza0VSRUlwSWdodFp2SUxaemFlMDF1NGtXSVNnWHFqN0lFUFVfUGNBUnJBIn0sInN1ZmZpeERhdGEiOnsiZGVsdGFIYXNoIjoiRWlEcGMtelNRcHJYMGhacDdlMC1QXzhwWjkzdm9EZXhwLVo1ZXVtbFhzN2hQUSIsInJlY292ZXJ5Q29tbWl0bWVudCI6IkVpQWtKOTNBLThyeXF3X0VqSVRuSEpqdkhvZ016N2YtTlRZWXlhOENVMEdYdWcifX0",
    "CredentialType": "VerifiedCredentialExpert",
    "DidManifest": "https://beta.did.msidentity.com/v1.0/3c32ed40-8a10-465b-8ba4-0b1e86882668/verifiableCredential/contracts/VerifiedCredentialExpert",
    "IssuancePinCodeLength": 0
```
- **VerifierAuthority** - This DID identifier is used as value for the `authority` for the presentation request (verify VC) sent to the VC Client API. It will be your DID you can find in your VC blade in portal.azure.com.
- **IssuerAuthority** - The issuer DID identifier is used in two ways: 
  - For issuing VCs, it is used for the `authority`for the issuance request sent to the VC Client API. Since you don't issue someone elses credentials, it is your did from the VC Credential you created in portal.azure.com
  - For preenting/verifying VCs, it is used for the `acceptedIssuers` attribute to indicate what issuers you trust.
- **CredentialType** - For both issuance and presentation, the value is used to set the `type` attribute indicating the type name of the credential you want to issue or verify.
- **DidManifest**- The complete url to the DID manifest. It is used to set the attribute `manifest` and it is used for both issuance and presentation.
- **IssuancePinCodeLength** - If you want your issuance process to use the pin code method, you specify how many digits the pin code should have. A value of zero will not use the pin code method.

Once you clone this github repo, the minimal edit you need to do is to update the `VerifierAuthority` with your DID. Then you can run it an ***verify*** the standard `VerifiedCredentialExpert`. 
You can not issue that type of VC, but that you can do here [https://didvc-issuer-sample.azurewebsites.net/](https://didvc-issuer-sample.azurewebsites.net/). 
When you have created your own Credential in portal.azure.com, then you update the other settings and you can then issue and verify your own VCs.

### Standalone
To run the sample standalone, just clone the repository, compile & run it. It's callback endpoint must be publically reachable, and for that reason, use `ngrok` as a reverse proxy to read your app.

```Powershell
git clone https://github.com/cljung/did-samples.git
cd did-samples/api-dotnet
dotnet build "client-api-test-service-dotnet.csproj" -c Debug -o .\bin\Debug\netcoreapp3.1
dotnet run
```

Then, open a separate command prompt and run the following command

```Powershell
ngrok http 5002
```

Grab, the url in the ngrok output (like `https://96a139d4199b.ngrok.io`) and Browse to it.

### Docker build

To run it locally with Docker
```
docker build -t client-api-test-service-dotnet:v1.0 .
docker run --rm -it -p 5002:80 client-api-test-service-dotnet:v1.0
```

Then, open a separate command prompt and run the following command

```Powershell
ngrok http 5002
```

Grab, the url in the ngrok output (like `https://96a139d4199b.ngrok.io`) and Browse to it.

### Together with Azure AD B2C
To use this sample together with Azure AD B2C, you first needs to build it, which means follow the steps above. 

![API Overview](media/api-b2c-overview.png)

Then you need to deploy B2C Custom Policies that has configuration to add Verifiable Credentials as a Claims Provider and to integrate with this DotNet API. This, you will find in the github repo [https://github.com/cljung/did-samples/b2c-vc-signin](https://github.com/cljung/tree/main/b2c-vc-signin). That repo has a node.js issuer/verifier WebApp that uses the VC SDK, but you can skip the `vc` directory and only work with what is in the `b2c` directory. In the instructions on how to edit the B2C policies, it is mentioned that you need to update the `VCServiceUrl` and the `ServiceUrl` to point to your API. That means you need to update it with your `ngrok` url you got when you started the DotNet API in this sample. Otherwise, follow the instructions in [https://github.com/cljung/did-samples/blob/main/b2c-vc-signin/b2c/README.md](https://github.com/cljung/did-samples/blob/main/b2c-vc-signin/b2c/README.md) and deploy the B2C Custom Policies

### LogLevel Trace

If you set the LogLevel to `Trace` in the appsettings.*.json file, then the DotNet sample will output all HTTP requests, which will make it convenient for you to study the interaction between components.

![API Overview](media/loglevel-trace.png)