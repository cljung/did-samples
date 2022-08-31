# Microsoft Entra Verified ID PHP sample
This is a PHP sample of how to verify a Verifiable Credential using Microsoft Entra Verified ID.

## Running the sample

Follow the [tutorials](https://aka.ms/didfordevs) for how to setup the Verified ID service and how to issue credentials. Since this sample do not implement the issuance part, you need to get another sample and issue your self a VC that you can verify.

### Update config.php
Update [config.php](inc/config.php) with the details of your deployment

```php
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
```

### Start the sample
Open a command prompt and run the following command

```DOS
php -c %yourphpinipath%\php.ini -S 127.0.0.1:8080 -t %cd%
```

In another command prompt, start `ngrok`

```dos
ngrok http 8080
```

Browse to the ngrok https URL