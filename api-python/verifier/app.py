#!/usr/bin/python

from flask import Flask
from flask import request,Response,redirect
from flask_caching import Cache
from flask.json import jsonify
import json
import logging
import sys, os, tempfile, uuid, time, datetime
import configparser
import argparse
import requests
import jwt, base64
import msal

cacheConfig = {
    "DEBUG": True,          # some Flask specific configs
    "CACHE_TYPE": "SimpleCache",  # Flask-Caching related configs
    "CACHE_DEFAULT_TIMEOUT": 300
}
app = Flask(__name__,static_url_path='',static_folder='static',template_folder='static')

app.config.from_mapping(cacheConfig)
cache = Cache(app)

log = logging.getLogger() 
log.setLevel(logging.INFO)

fP = open(sys.argv[1],)
presentationConfig = json.load(fP)
fP.close()  

configFileName = ".\didconfig.json"
if len(sys.argv) >= 3:
   configFileName = sys.argv[2]

fP = open(configFileName,)
config = json.load(fP)
fP.close()  

cca = msal.ConfidentialClientApplication( config["azClientId"], 
    authority="https://login.microsoftonline.com/" + config["azTenantId"],
    client_credential=config["azClientSecret"],
    )

print( presentationConfig["presentation"]["requestedCredentials"][0]["manifest"] )

r = requests.get(url = str(presentationConfig["presentation"]["requestedCredentials"][0]["manifest"]) )
manifest = r.json()
print( manifest )

presentationConfig["registration"]["clientName"] = "Python Client API Verifier"
if not str(presentationConfig["authority"]).startswith("did:ion:"):
    presentationConfig["authority"] = manifest["input"]["issuer"]
presentationConfig["presentation"]["requestedCredentials"][0]["trustedIssuers"][0] = manifest["input"]["issuer"]
if str(presentationConfig["presentation"]["requestedCredentials"][0]["type"]) == "":
    presentationConfig["presentation"]["requestedCredentials"][0]["type"] = manifest["id"]


@app.route('/')
def root():
    return app.send_static_file('index.html')

@app.route("/logo.png", methods = ['GET'])
def logoRedirector():    
    return redirect(str(manifest["display"]["card"]["logo"]["uri"]) )

@app.route("/echo", methods = ['GET'])
def echoApi():
    result = {
        'date': datetime.datetime.utcnow().isoformat(),
        'api': request.url,
        'Host': request.headers.get('host'),
        'x-forwarded-for': request.headers.get('x-forwarded-for'),
        'x-original-host': request.headers.get('x-original-host'),
        'issuerDid':  presentationConfig["presentation"]["requestedCredentials"][0]["trustedIssuers"][0],
        'credentialType': presentationConfig["presentation"]["requestedCredentials"][0]["type"],
        'client_purpose': presentationConfig["presentation"]["requestedCredentials"][0]["purpose"],
        'displayCard': manifest["display"]["card"],
        'buttonColor': "#000080"
    }
    return Response( json.dumps(result), status=200, mimetype='application/json')

@app.route("/presentation-request", methods = ['GET'])
def presentationRequest():
    id = str(uuid.uuid4())

    accessToken = ""
    result = cca.acquire_token_for_client( scopes="bbb94529-53a3-4be5-a069-7eaf2712b826/.default" )
    if "access_token" in result:
        print( result['access_token'] )
        accessToken = result['access_token']
    else:
        print(result.get("error") + result.get("error_description"))

    payload = presentationConfig.copy()
    payload["callback"]["url"] = str(request.url_root).replace("http://", "https://") + "presentation-request-api-callback"
    payload["callback"]["state"] = id
    print( json.dumps(payload) )
    post_headers = { "content-type": "application/json", "Authorization": "Bearer " + accessToken }
    client_api_request_endpoint = "https://beta.did.msidentity.com/v1.0/" + config["azTenantId"] + "/verifiablecredentials/request"
    r = requests.post( client_api_request_endpoint
                    , headers=post_headers, data=json.dumps(payload))
    resp = r.json()
    print(resp)
    resp["id"] = id            
    return Response( json.dumps(resp), status=200, mimetype='application/json')

@app.route("/presentation-request-api-callback", methods = ['GET'])
def presentationRequestApiCallbackGET():
    print('test')
    return ""

@app.route("/presentation-request-api-callback", methods = ['POST'])
def presentationRequestApiCallback():
    presentationResponse = request.json
    print(presentationResponse)
    if presentationResponse["code"] == "request_retrieved":
        cacheData = {
            "status": 1,
            "message": "QR Code is scanned. Waiting for validation..."
        }
        cache.set( presentationResponse["state"], json.dumps(cacheData) )
        return ""
    if presentationResponse["code"] == "presentation_verified":
        cacheData = {
            "status": 2,
            "message": "VC Presented",
            "presentationResponse": presentationResponse
        }
        cache.set( presentationResponse["state"], json.dumps(cacheData) )
        return ""
    return ""

@app.route("/presentation-response-status", methods = ['GET'])
def presentationRequestStatus():
    id = request.args.get('id')
    print(id)
    data = cache.get(id)
    print(data)
    if data is not None:
        cacheData = json.loads(data)
        if cacheData["status"] == 1:
            browserData = {
                'status': cacheData["status"],
                'message': cacheData["message"]
            }
        if cacheData["status"] == 2:
            browserData = {
                'status': cacheData["status"],
                'message': cacheData["message"],
                'claims': cacheData["presentationResponse"]["issuers"][0]["claims"]
            }
        return Response( json.dumps(browserData), status=200, mimetype='application/json')
    else:
        return ""

def decodeJwtToken(token):
    return json.loads( base64.b64decode(token.split('.')[1]+'==').decode("utf-8") )

@app.route("/presentation-response-b2c", methods = ['POST'])
def presentationResponseB2C():
    presentationResponse = request.json
    id = presentationResponse["id"]
    print(id)
    data = cache.get(id)
    print(data)
    if data is not None:
        cacheData = json.loads(data)
        if cacheData["status"] == 2:
            jwtSIOP = decodeJwtToken( cacheData["presentationResponse"]["receipt"]["id_token"] )
            jwtVP = None
            for pres in jwtSIOP["attestations"]["presentations"]:
                jwtVP = decodeJwtToken( jwtSIOP["attestations"]["presentations"][pres] )
            jwtVC = decodeJwtToken( jwtVP["vp"]["verifiableCredential"][0] )
            claims = cacheData["presentationResponse"]["issuers"][0]["claims"]
            tid = None
            oid = None
            username = None
            if "sub" in claims:
                oid = claims["sub"]
            if "tid" in claims:
                tid = claims["tid"]
            if "username" in claims:
                username = claims["username"]
            responseBody = {
               'id': id, 
               'credentialsVerified': True,
               'credentialType': presentationConfig["presentation"]["requestedCredentials"][0]["type"],
               'displayName': claims["firstName"] + " " + claims["lastName"],
               'givenName': claims["firstName"],
               'surName': claims["lastName"],
               'iss': jwtVC["iss"],    # who issued this VC?
               'sub': jwtVC["sub"],    # who are you?
               'key': jwtVC["sub"].replace("did:ion:", "did.ion.").split(":")[0].replace("did.ion.", "did:ion:"),
               'oid': oid,
               'tid': tid,
               'username': username 
            }
            return Response( json.dumps(responseBody), status=200, mimetype='application/json')

    errmsg = {
        'version': '1.0.0', 
        'status': 400,
        'userMessage': 'Verifiable Credentials not presented'
        }
    return Response( json.dumps(errmsg), status=409, mimetype='application/json')

if __name__ == "__main__":
    app.run(host="0.0.0.0", port=8082)