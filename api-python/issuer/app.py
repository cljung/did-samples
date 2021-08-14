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
from random import randint
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

fI = open(sys.argv[1],)
issuanceConfig = json.load(fI)
fI.close()  

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

print( issuanceConfig["issuance"]["manifest"] )

r = requests.get(url = str(issuanceConfig["issuance"]["manifest"]) )
manifest = r.json()
print( manifest )

issuanceConfig["registration"]["clientName"] = "Python Client API Verifier"
if not str(issuanceConfig["authority"]).startswith("did:ion:"):
    issuanceConfig["authority"] = manifest["input"]["issuer"]
if str(issuanceConfig["issuance"]["type"]) == "":
    issuanceConfig["issuance"]["type"] = manifest["id"]
if "pin" in issuanceConfig["issuance"] is not None:
    if int(issuanceConfig["issuance"]["pin"]["length"]) == 0:
        del issuanceConfig["issuance"]["pin"]

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
        'issuerDid':  issuanceConfig["authority"],
        'credentialType': issuanceConfig["issuance"]["type"],
        'displayCard': manifest["display"]["card"],
        'buttonColor': "#000080",
        'selfAssertedClaims': None
    }
    if "claims" in issuanceConfig["issuance"] is not None:
        result["selfAssertedClaims"] = issuanceConfig["issuance"]["claims"]

    return Response( json.dumps(result), status=200, mimetype='application/json')

@app.route("/issue-request-api", methods = ['GET'])
def presentationRequest():
    id = str(uuid.uuid4())

    accessToken = ""
    result = cca.acquire_token_for_client( scopes="bbb94529-53a3-4be5-a069-7eaf2712b826/.default" )
    if "access_token" in result:
        print( result['access_token'] )
        accessToken = result['access_token']
    else:
        print(result.get("error") + result.get("error_description"))

    payload = issuanceConfig.copy()
    payload["callback"]["url"] = str(request.url_root).replace("http://", "https://") + "issue-request-api-callback"
    payload["callback"]["state"] = id
    pinCode = 0
    if "pin" in payload["issuance"] is not None:
        pinCode = ''.join(str(randint(0,9)) for _ in range(int(payload["issuance"]["pin"]["length"])))
        payload["issuance"]["pin"]["value"] = pinCode
    if "claims" in payload["issuance"] is not None:
        for claim in issuanceConfig["issuance"]["claims"]:
            payload["issuance"]["claims"][claim] = id = request.args.get(claim)
    print( json.dumps(payload) )
    post_headers = { "content-type": "application/json", "Authorization": "Bearer " + accessToken }
    client_api_request_endpoint = "https://beta.did.msidentity.com/v1.0/" + config["azTenantId"] + "/verifiablecredentials/request"
    r = requests.post( client_api_request_endpoint
                    , headers=post_headers, data=json.dumps(payload))
    resp = r.json()
    print(resp)
    resp["id"] = id
    if "pin" in payload["issuance"] is not None:
        resp["pin"] = pinCode
    return Response( json.dumps(resp), status=200, mimetype='application/json')

@app.route("/issue-request-api-callback", methods = ['POST'])
def issuanceRequestApiCallback():
    issuanceResponse = request.json
    print(issuanceResponse)
    if issuanceResponse["code"] == "request_retrieved":
        cacheData = {
            "status": 1,
            "message": "QR Code is scanned. Complete issuance in Authenticator."
        }
        cache.set( issuanceResponse["state"], json.dumps(cacheData) )
        return ""
    return ""

@app.route("/issue-response-status", methods = ['GET'])
def issuanceRequestStatus():
    id = request.args.get('id')
    print(id)
    data = cache.get(id)
    print(data)
    if data is not None:
        cacheData = json.loads(data)
        browserData = {
            'status': cacheData["status"],
            'message': cacheData["message"]
        }
        return Response( json.dumps(browserData), status=200, mimetype='application/json')
    else:
        return ""

if __name__ == "__main__":
    app.run(host="0.0.0.0", port=8081)