set AADVC.TenantId=<YOUR-AAD-TENANDID>
set AADVC.ClientID=<YOUR-AAD-CLIENTID-FOR-KEYVAULT-ACCESS>
set AADVC.ClientSecret=<YOUR-AAD-CLIENTSECRET-FOR-KEYVAULT-ACCESS>
set AADVC.DidManifest=https://beta.did.msidentity.com/v1.0/9885457a-2026-4e2c-a47e-32ff52ea0b8d/verifiableCredential/contracts/Cljungdemob2cMembership
set AADVC.PRESENTATIONFILE=%cd%\requests\presentation_request_cljungdemob2c.json
set AADVC.ISSUANCEFILE=%cd%\requests\issuance_request_cljungdemob2c.json

java -jar .\target\client-test-api-test-service-java-0.0.1-SNAPSHOT.jar