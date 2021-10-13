set AADVC.TenantId=9885457a-2026-4e2c-a47e-32ff52ea0b8d
set AADVC.ClientID=b8c0bc22-7524-4a84-a57e-085f2d4bbe84
set AADVC.ClientSecret=a58d0c57-a65c-4191-9082-e9f2c8739706
set AADVC.DidManifest=https://beta.did.msidentity.com/v1.0/9885457a-2026-4e2c-a47e-32ff52ea0b8d/verifiableCredential/contracts/FawltyTowers2CampusPass
set AADVC.PRESENTATIONFILE=%cd%\requests\presentation_request_fawltytowers2_campuspass.json
set AADVC.ISSUANCEFILE=%cd%\requests\issuance_request_fawltytowers2_campuspass.json

java -jar .\target\client-test-api-test-service-java-0.0.1-SNAPSHOT.jar