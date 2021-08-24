# did-samples/api-java
Azure AD Verifiable Credentials Java Springbot sample that uses the new private preview VC Client APIs.
This sample is updated to work with version v0.3 which introduces authorization and the use of an access token when calling the API. Please read the section on [Adding Authorization](https://github.com/cljung/did-samples/#adding-authorization) to see what additions you need to do.

## Two modes of operations
This sample can work in two ways:
- As a standalone WebApp with it's own web UI that let's you issue and verify DID Verifiable Credentials.
- As an API service that works in combination with Azure AD B2C in order to use VCs as a way to authenticate to B2C.

## VC Client API, what is that?
Please read the top level [README.md](https://github.com/cljung/did-samples) for an introduction.

## Running the sample

To run the sample issuer standalone, just clone the repository, compile & run it. To do this, you need to jave Java SKD and Maven installed on your machine. If you prefer to run it with docker, please see instructions below. It's callback endpoint must be publically reachable, and for that reason, use `ngrok` as a reverse proxy to read your app.

### Build
```Powershell
git clone https://github.com/cljung/did-samples.git
cd did-samples/api-java
mvn clean package -DskipTests
```

### Run 
The sample uses a few environment variables to work correctly. The script file `run.cmd` shows you which ones to set befor running the Java app. The first three lines comes from your authorization setup for how your app connects to your Azure Key Vault, see [Adding Authorization](https://github.com/cljung/did-samples/#adding-authorization). The last three lines determinds which of your credentials you want the sample to use. The `DidManifest` points to your manifest contract. The `AADVC.PRESENTATIONFILE` and the `AADVC.ISSUANCEFILE` points to the payload files in the `.\requests` directory you want to use. You can update these to use your own credentials.

```json
set AADVC.TenantId=<YOUR-AAD-TENANDID>
set AADVC.ClientID=<YOUR-AAD-CLIENTID-FOR-KEYVAULT-ACCESS>
set AADVC.ClientSecret=<YOUR-AAD-CLIENTSECRET-FOR-KEYVAULT-ACCESS>
set AADVC.DidManifest=https://beta.did.msidentity.com/v1.0/9885457a-2026-4e2c-a47e-32ff52ea0b8d/verifiableCredential/contracts/Cljungdemob2cMembership
set AADVC.PRESENTATIONFILE=%cd%\requests\presentation_request_cljungdemob2c.json
set AADVC.ISSUANCEFILE=%cd%\requests\issuance_request_cljungdemob2c.json
```

Once you have updated the `run.cmd` file, just run it to start the app. The app will contain both the Issuing and the Verification service for your credential. 

Then, open a separate command prompt and run the following command

```Powershell
ngrok http 8080
```

Grab, the url in the ngrok output (like `https://96a139d4199b.ngrok.io`) and Browse to it.

### Docker build

To run it locally with Docker, you need to edit the file `docker-run.cmd` to add the environment variables for your settings. The `docker-build.cmd` builds the app using the standard `docker build` command.

```Powershell
docker build -t did-api-java .
```

Then, running the docker app becomes a bit more of a job as you need to add your environment settings. If you add/edit more json files for your payloads, they will be automatically copied and you can reference them as `/use/local/lib/<my-file-name>`.

```Powershell
docker run --rm -it -p 8080:8080 ^
    -e AADVC.TenantId=<YOUR-AAD-TENANDID> ^
    -e AADVC.ClientID=<YOUR-AAD-CLIENTID-FOR-KEYVAULT-ACCESS> ^
    -e AADVC.ClientSecret=<YOUR-AAD-CLIENTSECRET-FOR-KEYVAULT-ACCESS> ^
    -e AADVC.DidManifest=https://beta.did.msidentity.com/v1.0/9885457a-2026-4e2c-a47e-32ff52ea0b8d/verifiableCredential/contracts/Cljungdemob2cMembership ^
    -e AADVC.PRESENTATIONFILE=/usr/local/lib/presentation_request_cljungdemob2c.json ^
    -e AADVC.ISSUANCEFILE=/usr/local/lib/issuance_request_cljungdemob2c.json ^
    did-api-java:latest
```

Then, open a separate command prompt and run the following command

```Powershell
ngrok http 8080
```
