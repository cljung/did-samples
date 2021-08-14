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
var jwt_decode = require('jwt-decode')
var fetch = require( 'node-fetch' );
var secureRandom = require('secure-random');
const https = require('https')
const url = require('url')
const { Console } = require('console');
var msal = require('@azure/msal-node');

//////////// didconfig file can come from command line, env var or the default
var didConfigFile = process.argv.slice(2)[1];
if ( !didConfigFile ) {
  didConfigFile = process.env.DIDCONFIG || './didconfig.json';
}
const config = require( didConfigFile )
if (!config.azTenantId) {
  throw new Error('The didconfig.json file is missing.')
}

//////////// Setup the presentation request payload template
var requestConfigFile = process.argv.slice(2)[0];
if ( !requestConfigFile ) {
  requestConfigFile = process.env.DIDPRESENTATIONREQUESTCONFIG || './presentation_request_config_v2.json';
}
var presentationRequestConfig = require( requestConfigFile );
presentationRequestConfig.registration.clientName = "Node.js SDK API Verifier";

var didContract = null;

//////////// MSAL
const msalConfig = {
  auth: {
      clientId: config.azClientId,
      authority: `https://login.microsoftonline.com/${config.azTenantId}`,
      clientSecret: config.azClientSecret,
  },
  system: {
      loggerOptions: {
          loggerCallback(loglevel, message, containsPii) {
              console.log(message);
          },
          piiLoggingEnabled: false,
          logLevel: msal.LogLevel.Verbose,
      }
  }
};
const cca = new msal.ConfidentialClientApplication(msalConfig);
const msalClientCredentialRequest = {
  scopes: ["bbb94529-53a3-4be5-a069-7eaf2712b826/.default"],
  skipCache: false, 
};
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
  console.log( `${h1}\n${getDateFormatted()}: ${req.method} ${req.protocol}://${req.hostname}${req.originalUrl}` );
  console.log( req.headers );
}

// echo function so you can test deployment
app.get("/echo",
    function (req, res) {
        requestTrace( req );
        res.status(200).json({
            'date': new Date().toISOString(),
            'api': req.protocol + '://' + req.hostname + req.originalUrl,
            'Host': req.hostname,
            'x-forwarded-for': req.headers['x-forwarded-for'],
            'x-original-host': req.headers['x-original-host'],
            'issuerDid': presentationRequestConfig.presentation.requestedCredentials[0].trustedIssuers[0],
            'credentialType': presentationRequestConfig.presentation.requestedCredentials[0].type,
            'client_purpose': presentationRequestConfig.presentation.requestedCredentials[0].client_purpose,
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

app.get('/presentation-request', async (req, res) => {
  requestTrace( req );

  // prep a session state of 0
  sessionStore.get(req.session.id, (error, session) => {
    var sessionData = {
      "status" : 0,
      "message": "Waiting for QR code to be scanned"
    };
    if ( session ) {
      session.sessionData = sessionData;
      sessionStore.set( req.session.id, session);
    }
  });

  // get the Access Token
  var accessToken = "";
  try {
    const result = await cca.acquireTokenByClientCredential(msalClientCredentialRequest);
    if ( result ) {
      accessToken = result.accessToken;
    }
  } catch {
      res.status(401).json({
        'error': 'Could not acquire credentials to access your Azure Key Vault'
        });  
      return; 
  }
  console.log( `accessToken: ${accessToken}` );

  // call the VC Client API
  presentationRequestConfig.callback.url = `https://${req.hostname}/presentation-request-api-callback`;
  presentationRequestConfig.callback.state = req.session.id;

  console.log( 'VC Client API Request' );
  console.log( presentationRequestConfig );

  var payload = JSON.stringify(presentationRequestConfig);
  const fetchOptions = {
    method: 'POST',
    body: payload,
    headers: {
      'Content-Type': 'application/json',
      'Content-Length': payload.length.toString(),
      'Authorization': `Bearer ${accessToken}`
    }
  };

  var client_api_request_endpoint = `https://beta.did.msidentity.com/v1.0/${config.azTenantId}/verifiablecredentials/request`;
  const response = await fetch(client_api_request_endpoint, fetchOptions);
  var apiResp = await response.json()

  apiResp.id = req.session.id;                              // add session id so browser can pull status

  console.log( 'VC Client API Response' );
  console.log( apiResp );
  
  res.status(200).json(apiResp);       
})

app.post('/presentation-request-api-callback', parser, async (req, res) => {
  var body = '';
  req.on('data', function (data) {
    body += data;
  });
  req.on('end', function () {
    requestTrace( req );
    console.log( body );
    var presentationResponse = JSON.parse(body.toString());
    if ( presentationResponse.code == "request_retrieved" ) {
      sessionStore.get(presentationResponse.state, (error, session) => {
        var cacheData = {
            "status": 1,
            "message": "QR Code is scanned. Waiting for validation..."
        };
        session.sessionData = cacheData;
        sessionStore.set( presentationResponse.state, session, (error) => {
          res.send();
        });
      })      
    }
    if ( presentationResponse.code == "presentation_verified" ) {
      sessionStore.get(presentationResponse.state, (error, session) => {
        var cacheData = {
            "status": 2,
            "message": "VC Presented",
            "presentationResponse": presentationResponse
        };
        session.sessionData = cacheData;
        sessionStore.set( presentationResponse.state, session, (error) => {
          res.send();
        });
      })      
    }
  });  
  res.send()
})

app.get('/presentation-response-status', async (req, res) => {
  var id = req.query.id;
  requestTrace( req );
  sessionStore.get( id, (error, store) => {
    if (store && store.sessionData) {
      console.log(`status: ${store.sessionData.status}, message: ${store.sessionData.message}`);
      var message = store.sessionData.message;
      var claims = null;
      // if VC has been presented, try to return the user's name as the message so we can display that in the UX
      if ( store.sessionData.status == 2 ) {
        claims = store.sessionData.presentationResponse.issuers[0].claims;
      }
      res.status(200).json({
        'status': store.sessionData.status, 
        'message': message,
        'claims': claims
        });   
      }
  })
})
// same as above but for B2C where we return 409 Conflict if Auth Result isn't OK
var parserJson = bodyParser.json();
app.post('/presentation-response-b2c', parserJson, async (req, res) => {
  var id = req.body.id;
  requestTrace( req );
  // If a credential has been received, display the contents in the browser
  sessionStore.get( id, (error, store) => {
    if (store && store.sessionData && store.sessionData.status == 2 ) {
      console.log("Has VC. Will return it to B2C");
      
      // We need to decode the id_token from the presentatior receipt. 
      // It's in three layers: SIOP -> VP -> VC
      // In the VC we want to pass back the 'iss' and the 'sub' claims for reference
      // (Who issued this VC and who is the holder)
      var jwtSIOP = jwt_decode( store.sessionData.presentationResponse.receipt.id_token );
      var jwtVP;
      for( var pres in jwtSIOP.attestations.presentations ) {
        jwtVP = jwt_decode( jwtSIOP.attestations.presentations[pres] );  
      }
      var jwtVC = jwt_decode( jwtVP.vp.verifiableCredential[0] );

      var claims = store.sessionData.presentationResponse.issuers[0].claims;
      var tid = null;
      var oid = null;
      var username = null;
      try {
        oid = claims.sub;
        tid = claims.tid;
        username = claims.username;
      } catch {
      }
      var responseBody = {
        'id': id, 
        'credentialsVerified': true,
        'credentialType': presentationRequestConfig.presentation.requestedCredentials[0].type,
        'displayName': `${claims.firstName} ${claims.lastName}`,
        'givenName': claims.firstName,
        'surName': claims.lastName,
        'iss': jwtVC.iss,    // who issued this VC?
        'sub': jwtVC.sub,    // who are you?
        'key': jwtVC.sub.replace("did:ion:", "did.ion.").split(":")[0], //.replace("did.ion.", "did:ion:"),
        'oid': oid,
        'tid': tid,
        'username': username
        };
        req.session.sessionData = null; // wack it
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

async function getDidContract( ) {
  const fetchOptions = {
    method: 'GET'
  };
  const response = await fetch( presentationRequestConfig.presentation.requestedCredentials[0].manifest, fetchOptions );
  didContract = await response.json()
  console.log( 'DID contract' );
  console.log( didContract );
  if ( presentationRequestConfig.presentation.requestedCredentials[0].type.length == 0 ) {
    presentationRequestConfig.presentation.requestedCredentials[0].type = didContract.id;
  }
  presentationRequestConfig.presentation.requestedCredentials[0].trustedIssuers[0] = didContract.input.issuer;
  console.log(presentationRequestConfig.authority);
  if ( !(presentationRequestConfig.authority.startsWith("did:ion:")) ) {
    console.log("authority <-- manifest");
    presentationRequestConfig.authority = didContract.input.issuer;
  }
}

getDidContract();

//doHttpRequest( `https://beta.discover.did.msidentity.com/1.0/identifiers/${config.didIssuer}`, didResolverCallback );

// start server
app.listen(port, () => console.log(`Example app listening on port ${port}!`))
