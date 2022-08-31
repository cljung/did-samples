<?php
define("PROJECT_ROOT_PATH", __DIR__ . "/../");
define( "TENANT_ID", "<your-tenantId>" );
define( "TOKEN_ENDPOINT", 'https://login.microsoftonline.com/'.TENANT_ID.'/oauth2/v2.0/token?api-version=1.0' );
define( "CLIENT_ID", '<your-clientId>' );
define( "CLIENT_SECRET", '<your-client-secret>' );
define( "SCOPES", "3db474b9-6a0c-4840-96ac-1fceb342124f/.default" );

define( "VERIFIEDID_ENDPOINT", "https://verifiedid.did.msidentity.com/v1.0/" );
define( "REGISTRATION_CLIENT_NAME", "PHP Entra Verified ID sample" );
define( "API_KEY", "3b80bd57-d395-45d3-b69b-3875be0459b2" );
define( "CREDENTIAL_TYPE", "VerifiedCredentialExpert" );
define( "CREDENTIAL_MANIFEST", "<your-manifest-url>" );
define( "ISSUER_AUTHORITY", "<your-DID>" );
define( "VERIFIER_AUTHORITY", "<your-DID>" );
