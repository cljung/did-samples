// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

// Verifiable Credentials Issuer Sample

////////////// Node packages
var express = require('express')
var session = require('express-session')
var base64url = require('base64url')
var secureRandom = require('secure-random');
var bodyParser = require('body-parser')
var fetch = require( 'node-fetch' );
const https = require('https')
const url = require('url')
const { SSL_OP_COOKIE_EXCHANGE } = require('constants');
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

//////////// Setup the issuance request payload template
var requestConfigFile = process.argv.slice(2)[0];
if ( !requestConfigFile ) {
  requestConfigFile = process.env.DIDISSUANCEREQUESTCONFIG || './issuance_request_config_v2.json';
}
var issuanceRequestConfig = require( requestConfigFile );
issuanceRequestConfig.registration.clientName = "Node.js SDK API Issuer";

// if there is pin code in the config, but length is zero - remove it. It really shouldn't be there
if ( issuanceRequestConfig.issuance.pin && issuanceRequestConfig.issuance.pin.length == 0 ) {
  issuanceRequestConfig.issuance.pin = null;
}

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
const port = process.env.PORT || 8081;

var parser = bodyParser.urlencoded({ extended: false });

// Serve static files out of the /public directory
app.use(express.static('public'))

// Set up a simple server side session store.
// The session store will briefly cache issuance requests
// to facilitate QR code scanning.
var sessionStore = new session.MemoryStore();
app.use(session({
  secret: 'cookie-secret-key',
  resave: false,
  saveUninitialized: true,
  store: sessionStore
}))

function getDateFormatted() {
  return new Date().toISOString().replace("T", " ");
}
function requestTrace( req ) {
  var h1 = '//****************************************************************************';
  console.log( `${h1}\n${getDateFormatted()}: ${req.method} ${req.protocol}://${req.hostname}${req.originalUrl}\n'x-forwarded-for': ${req.headers['x-forwarded-for']}` );
  console.log( `Headers:\n`);
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
            'issuerDid': issuanceRequestConfig.authority,
            'credentialType': issuanceRequestConfig.issuance.type,
            'credential': issuanceRequestConfig.issuance.manifest,
            'displayCard': didContract.display.card,
            'buttonColor': "#000080",
            'selfAssertedClaims': issuanceRequestConfig.issuance.claims
            });
    }
);

// Serve index.html as the home page
app.get('/', function (req, res) { 
  requestTrace( req );
  res.sendFile('public/index.html', {root: __dirname})
})

// when browser is asking for logo.png, we try to give it the logo defined in the manifest
app.get('/logo.png', function (req, res) { 
  if ( didContract.display ) {
    res.redirect( didContract.display.card.logo.uri );
  } else {
    res.redirect( 'ninja-icon.png' );
  }
})

function generatePin () {
  min = 0,
  max = 9999;
  return ("0" + (Math.floor(Math.random() * (max - min + 1)) + min)).substr(-4);
}

app.get('/issue-request-api', async (req, res) => {
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
  issuanceRequestConfig.callback.url = `https://${req.hostname}/issue-request-api-callback`;
  issuanceRequestConfig.callback.state = req.session.id;
  if ( issuanceRequestConfig.issuance.pin ) {
    issuanceRequestConfig.issuance.pin.value = generatePin();
  }
  if ( issuanceRequestConfig.issuance.claims ) {
    for( var claimName in issuanceRequestConfig.issuance.claims ) {
      issuanceRequestConfig.issuance.claims[claimName] = req.query[claimName];
    }
  }

  console.log( 'VC Client API Request' );
  console.log( issuanceRequestConfig );

  var payload = JSON.stringify(issuanceRequestConfig);
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
  if ( issuanceRequestConfig.issuance.pin ) {
    apiResp.pin = issuanceRequestConfig.issuance.pin.value;   // add pin code so browser can display it
  }
  console.log( 'VC Client API Response' );
  console.log( apiResp );
  
  res.status(200).json(apiResp);       
})

app.post('/issue-request-api-callback', parser, async (req, res) => {
  var body = '';
  req.on('data', function (data) {
    body += data;
  });
  req.on('end', function () {
    requestTrace( req );
    console.log( body );
    var issuanceResponse = JSON.parse(body.toString());
    if ( issuanceResponse.code == "request_retrieved" ) {
      sessionStore.get(issuanceResponse.state, (error, session) => {
        var sessionData = {
          "status" : 1,
          "message": "QR code is scanned. Complete issuance in Authenticator."
        };
        session.sessionData = sessionData;
        sessionStore.set( issuanceResponse.state, session, (error) => {
          res.send();
        });
      })      
    }
    res.send()
  });  
  res.send()
})

app.get('/issue-response-status', async (req, res) => {
  var id = req.query.id;
  requestTrace( req );
  sessionStore.get( id, (error, store) => {
    if (store && store.sessionData) {
      console.log(store.sessionData);
      res.status(200).json({
        'status': store.sessionData.status, 
        'message': store.sessionData.message
        });   
      }
  })
})

async function getDidContract( ) {

  const fetchOptions = {
    method: 'GET'
  };
  const response = await fetch( issuanceRequestConfig.issuance.manifest, fetchOptions );
  didContract = await response.json()
  console.log( 'DID contract' );
  console.log( didContract );
  if ( issuanceRequestConfig.issuance.type.length == 0 ) {
    issuanceRequestConfig.issuance.type = didContract.id;
  }
  issuanceRequestConfig.authority = didContract.input.issuer;
}

getDidContract();

// start server
app.listen(port, () => console.log(`Example issuer app listening on port ${port}!`))
