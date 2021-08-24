docker run --rm -it -p 8080:8080 ^
    -e AADVC.TenantId=<YOUR-AAD-TENANDID> ^
    -e AADVC.ClientID=<YOUR-AAD-CLIENTID-FOR-KEYVAULT-ACCESS> ^
    -e AADVC.ClientSecret=<YOUR-AAD-CLIENTSECRET-FOR-KEYVAULT-ACCESS> ^
    -e AADVC.DidManifest=https://beta.did.msidentity.com/v1.0/9885457a-2026-4e2c-a47e-32ff52ea0b8d/verifiableCredential/contracts/Cljungdemob2cMembership ^
    -e AADVC.PRESENTATIONFILE=/usr/local/lib/presentation_request_cljungdemob2c.json ^
    -e AADVC.ISSUANCEFILE=/usr/local/lib/issuance_request_cljungdemob2c.json ^
    did-api-java:latest
    