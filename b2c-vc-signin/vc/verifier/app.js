// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

// Verifiable Credentials Verifier Sample

////////////// Node packages
var http = require('http');
var fs = require('fs');
var path = require('path');
var express = require('express')
var session = require('express-session')
var bodyParser = require('body-parser')
var base64url = require('base64url')
var secureRandom = require('secure-random');

const https = require('https')
const url = require('url')

//////////////// Verifiable Credential SDK
var { ClientSecretCredential } = require('@azure/identity');
var { CryptoBuilder, 
      RequestorBuilder, 
      ValidatorBuilder,
      KeyReference
    } = require('verifiablecredentials-verification-sdk-typescript');

////////// Verifier's DID configuration values
var didConfigFile = process.argv.slice(2)[0];
if ( !didConfigFile ) {
  didConfigFile = './didconfig-b2c.json';
}
const config = require( didConfigFile )
if (!config.did) {
  throw new Error('Make sure you run the DID generation script before starting the server.')
}

/////////// Verifier's client details
const client = config.client;

/////////// Set the expected values for the Verifiable Credential

const credential = config.manifest
const issuerDid = [ config.did ]
var credentialType = '';
var didContract = null;

////////// Load the VC SDK with the Issuing Service's DID and Key Vault details
const kvCredentials = new ClientSecretCredential(config.azTenantId, config.azClientId, config.azClientSecret);
const signingKeyReference = new KeyReference(config.kvSigningKeyId, 'key', config.kvRemoteSigningKeyId);

var crypto = new CryptoBuilder()
    .useSigningKeyReference(signingKeyReference)
    .useKeyVault(kvCredentials, config.kvVaultUri)
    .useDid(config.didVerifier)
    .build();

//////////// Main Express server function
// Note: You'll want to update port values for your setup.
const app = express()
const port = process.env.PORT || 8082;

var parser = bodyParser.urlencoded({ extended: false });

// Serve static files out of the /public directory
app.use(express.static('public'))

// Set up a simple server side session store.
// The session store will briefly cache presentation requests
// to facilitate QR code scanning, and store presentation responses
// so they can be retrieved by the browser.
var sessionStore = new session.MemoryStore();
app.use(session({
  secret: 'cookie-secret-key',
  resave: false,
  saveUninitialized: true,
  store: sessionStore
}))

app.use(function (req, res, next) {
  res.header("Access-Control-Allow-Origin", "*");
  res.header("Access-Control-Allow-Headers", "Authorization, Origin, X-Requested-With, Content-Type, Accept");
  next();
});

function getDateFormatted() {
  return new Date().toISOString().replace("T", " ");
}
function requestTrace( req ) {
  var h1 = '//****************************************************************************';
  console.log( `${h1}\n${getDateFormatted()}: ${req.method} ${req.protocol}://${req.hostname}${req.originalUrl}\n'x-forwarded-for': ${req.headers['x-forwarded-for']}` );
  if ( req.method == 'POST') {
    console.log( req.body )
  }
}

// echo function so you can test deployment
app.get("/api/verifier/echo",
    function (req, res) {
        requestTrace( req );
        res.status(200).json({
            'date': new Date().toISOString(),
            'api': req.protocol + '://' + req.hostname + req.originalUrl,
            'Host': req.hostname,
            'x-forwarded-for': req.headers['x-forwarded-for'],
            'x-original-host': req.headers['x-original-host'],
            'issuerDid': issuerDid,
            'credentialType': credentialType,
            'client_purpose': client.client_purpose,
            'displayCard': didContract.display.card,
            'buttonColor': "#000080"
            });
    }
);

// Serve index.html as the home page
app.get('/', function (req, res) { 
  requestTrace( req );
  res.sendFile('public/index.html', {root: __dirname})
})

app.get('/logo.png', function (req, res) { 
  //res.redirect( didContract.display.card.logo.uri );
  var address = res.location(didContract.display.card.logo.uri).get('Location');
  res.statusCode = 302;
  var body = 'Redirecting to ' + address;
  res.set('Content-Length', Buffer.byteLength(body));
  if (res.req.method === 'HEAD') {
    res.end();
  } else {
    res.end(body);
  }
})

// Generate an presentation request, cache it on the server,
// and return a reference to the issuance reqeust. The reference
// will be displayed to the user on the client side as a QR code.
app.get('/api/verifier/presentation-request', async (req, res) => {
  requestTrace( req );
  // Construct a request to issue a verifiable credential 
  // using the verifiable credential issuer service
  state = req.session.id;
  const nonce = base64url.encode(Buffer.from(secureRandom.randomUint8Array(10)));
  const clientId = `https://${req.hostname}/api/verifier/presentation-response`;
  const requestBuilder = new RequestorBuilder({
    clientName: client.client_name,
    clientId: clientId,
    redirectUri: clientId,
    logoUri: client.logo_uri,
    tosUri: client.tos_uri,
    client_purpose: client.client_purpose,
    presentationDefinition: {
      input_descriptors: [{
          id: "ninja",
          schema: {
              uri: [credentialType],
          },
          issuance: [{
              manifest: credential
          }]
      }]
  }
},  crypto)
    .useNonce(nonce)
    .useState(state);

  // Cache the issue request on the server
  req.session.presentationRequest = await requestBuilder.build().create(); 
  // Return a reference to the presentation request that can be encoded as a QR code
  var requestUri = encodeURIComponent(`https://${req.hostname}/api/verifier/presentation-request.jwt?id=${req.session.id}`);
  var presentationRequestReference = 'openid://vc/?request_uri=' + requestUri;
 // res.send(presentationRequestReference);
  res.status(200).json({
    'id': req.session.id, 
    "url": presentationRequestReference
    });
})

// When the QR code is scanned, Authenticator will dereference the
// presentation request to this URL. This route simply returns the cached
// presentation request to Authenticator.
app.get('/api/verifier/presentation-request.jwt', async (req, res) => {
  requestTrace( req );
  // Look up the issue reqeust by session ID
  sessionStore.get(req.query.id, (error, session) => {
    console.log( `Presentation Request:\n${session.presentationRequest.request}` );
    res.send(session.presentationRequest.request);
  })
})

// Once the user approves the presentation request,
// Authenticator will present the credential back to this server
// at this URL. We can verify the credential and extract its contents
// to verify the user is a Verified Credential Ninja.
app.post('/api/verifier/presentation-response', parser, async (req, res) => {
  requestTrace( req );
  if(req.body.constructor === Object && Object.keys(req.body).length === 0) {
    console.log('BODY missing');
    return res.send()
  }  
  // Set up the Verifiable Credentials SDK to validate all signatures
  // and claims in the credential presentation.
  const clientId = `https://${req.hostname}/api/verifier/presentation-response`

  // Validate the credential presentation and extract the credential's attributes.
  // If this check succeeds, the user is a Verified Credential Ninja.
  // Log a message to the console indicating successful verification of the credential.
  const validator = new ValidatorBuilder(crypto)
    .useTrustedIssuersForVerifiableCredentials({[credentialType]: issuerDid})
    .useAudienceUrl(clientId)
    .build();

  const token = req.body.id_token;
  const validationResponse = await validator.validate(req.body.id_token);
  if (!validationResponse.result) {
      console.error(`${getDateFormatted()}Validation failed: ${validationResponse.detailedError}`);
      return res.send()
  }

  var verifiedCredential = validationResponse.validationResult.verifiableCredentials[credentialType].decodedToken;
  console.log(`${getDateFormatted()}Verifier Validation result: ${verifiedCredential.vc.credentialSubject.firstName} ${verifiedCredential.vc.credentialSubject.lastName} is a ${credentialType}!`);

  // Store the successful presentation in session storage
  sessionStore.get(req.body.state, (error, session) => {
    session.verifiedCredential = verifiedCredential;
    sessionStore.set(req.body.state, session, (error) => {
      res.send();
    });
  })
})

// Checks to see if the server received a successful presentation
// of a Verified Credential Ninja card. Updates the browser UI with
// a successful message if the user is a verified ninja.
app.get('/api/verifier/presentation-response', async (req, res) => {
  requestTrace( req );
  var id = req.query.id;
  // If a credential has been received, display the contents in the browser
  sessionStore.get( id, (error, session) => {
    if (req.session.verifiedCredential) {
      presentedCredential = req.session.verifiedCredential;
      req.session.verifiedCredential = null;
      console.log( 'VC Claims:' );
      console.log( presentedCredential.vc.credentialSubject );
      return res.send(`Congratulations, ${presentedCredential.vc.credentialSubject.firstName} ${presentedCredential.vc.credentialSubject.lastName} is a ${credentialType}!`)  
    }
    // If no credential has been received, just display an empty message
    res.send('')
  })
})

app.get('/api/verifier/presentation-response-status', async (req, res) => {
  requestTrace( req );
  var id = req.query.id;
  // If a credential has been received, display the contents in the browser
  sessionStore.get( id, (error, session) => {
    var credentialsVerified = false;
    var givenName = null;
    var surName = null;
    var status = 0;
    if (session && session.verifiedCredential) {
      console.log( "/presentation-response-status - has VC " + id);
      console.log(session.verifiedCredential);
      givenName = session.verifiedCredential.vc.credentialSubject.firstName;
      surName = session.verifiedCredential.vc.credentialSubject.lastName;
      credentialsVerified = true;
      status = 2;
    } else {
      console.log( "/presentation-response-status - no VC " + id);
    }
    res.status(200).json({
      'status': status,
      'message': `${givenName} ${surName}`,
      'id': id, 
      'credentialsVerified': credentialsVerified,
      'credentialType': credentialType,
      'displayName': `${givenName} ${surName}`,
      'givenName': givenName,
      'surName': surName
      });   
  })

})
// same as above but for B2C where we return 409 Conflict if Auth Result isn't OK
var parserJson = bodyParser.json();
app.post('/api/verifier/presentation-response-b2c', parserJson, async (req, res) => {
  requestTrace( req );
  var id = req.body.id;  
  // If a credential has been received, display the contents in the browser
  sessionStore.get( id, (error, session) => {
    if (session && session.verifiedCredential) {
      console.log("Has VC. Will return it to B2C");
      var tid = null;
      var oid = null;
      var username = null;
      try {
        oid = session.verifiedCredential.vc.credentialSubject.sub;
        tid = session.verifiedCredential.vc.credentialSubject.tid;
        username = session.verifiedCredential.vc.credentialSubject.username;
      } catch {
      }
      var responseBody = {
        'id': id, 
        'credentialsVerified': true,
        'credentialType': credentialType,
        'displayName': `${session.verifiedCredential.vc.credentialSubject.firstName} ${session.verifiedCredential.vc.credentialSubject.lastName}`,
        'givenName': session.verifiedCredential.vc.credentialSubject.firstName,
        'surName': session.verifiedCredential.vc.credentialSubject.lastName,
        'iss': session.verifiedCredential.iss,    // who issued this VC?
        'sub': session.verifiedCredential.sub,    // who are you?
        'key': session.verifiedCredential.sub.replace("did:ion:", "did.ion.").split(":")[0],
        'oid': oid,
        'tid': tid,
        'username': username
        };
        req.session.verifiedCredential = null; // wack it
        console.log( responseBody );
        res.status(200).json( responseBody );   
    } else {
      console.log('Will return 409 to B2C');
      res.status(409).json({
        'version': '1.0.0', 
        'status': 400,
        'userMessage': 'Verifiable Credentials not presented'
        });   
    }
  })
})



function doHttpRequest( endpoint, callback ) {
  const options = {
    method: 'GET',
    port: 443,
    hostname: url.parse(endpoint).hostname,
    path: url.parse(endpoint).pathname
  }
  var str = '';
  try {
    datarecv = function(response) {
      response.on('data', function (chunk) {
        str += chunk;
      });
      response.on('end', function() {
        callback( endpoint, str );
      });
    }    
    var req = https.request(options, datarecv).end();
  } catch(ex) {
    console.log(ex);
  }
}

// https://beta.discover.did.msidentity.com/1.0/identifiers
function didContractCallback( endpoint, data ) {
  console.log( `\nDID contract - ${endpoint}\n\n${data}\n` );
  didContract = JSON.parse( data );
  credentialType = didContract.id;
}

function didResolverCallback( endpoint, data ) {
  console.log( `\nDID resolver - ${endpoint}\n\n${data}\n` );
  didInfo = JSON.parse( data );
}

doHttpRequest( `https://beta.discover.did.msidentity.com/1.0/identifiers/${config.did}`, didResolverCallback );
doHttpRequest( credential, didContractCallback );

console.log( didConfigFile );

// start server
app.listen(port, () => console.log(`Example app listening on port ${port}!`))
