function RequestService(onDrawQRCode, onNavigateToDeepLink, onRequestRetrieved, onPresentationVerified, onIssuanceSuccesful, onSelfieTaken, onError, pollFrequency) {
    this.onDrawQRCode = onDrawQRCode;
    this.onNavigateToDeepLink = onNavigateToDeepLink;
    this.onRequestRetrieved = onRequestRetrieved;
    this.onPresentationVerified = onPresentationVerified;
    this.onIssuanceSuccesful = onIssuanceSuccesful;
    this.onSelfieTaken = onSelfieTaken;
    this.onError = onError;
    this.pollFrequency = (pollFrequency == undefined ? 1000 : pollFrequency);
    this.apiCreateIssuanceRequest = '/api/issuer/issuance-request';
    this.apiSetPhoto = '/api/issuer/userphoto';
    this.apiCreateSelfieRequest = '/api/issuer/selfie-request';
    this.apiCreatePresentationRequest = '/api/verifier/presentation-request';
    this.apiPollPresentationRequest = '/api/request-status';
    this.logEnabled = false;
    this.log = function (msg) {
        if (this.logEnabled) {
            console.log(msg);
        }
    }
    this.request = null;
    this.requestType = "";
    this.uuid = 'xxxxxxxx-xxxx-4xxx-yxxx-xxxxxxxxxxxx'
            .replace(/[xy]/g, function (c) {
                const r = Math.random() * 16 | 0, v = c == 'x' ? r : (r & 0x3 | 0x8);
                return v.toString(16);
            });

    // function to create a presentation request
    this.createRequest = async function (url) {
        const response = await fetch(url, { method: 'GET', headers: { 'Accept': 'application/json', 'rsid': this.uuid, 'X-SignalR-ConnectionId': connection.connectionId } });
        const respJson = await response.json();
        console.log(respJson);
        if (respJson.error_description) {
            this.onError(this.requestType, respJson.error_description);
        } else {
            this.request = respJson;
            if (/Android/i.test(navigator.userAgent) || /iPhone/i.test(navigator.userAgent)) {
                this.log(`Mobile device (${navigator.userAgent})! Using deep link (${respJson.url}).`);
                this.onNavigateToDeepLink(this.requestType, respJson.id, respJson.url);
            } else {
                this.log(`Not Android or IOS. Generating QR code encoded with ${message}`);
                this.onDrawQRCode(this.requestType, respJson.id, respJson.url, respJson.pin);
                //this.pollRequestStatus(respJson.id);
            }
        }
    };
    this.createPresentationRequest = function (callback) {
        this.requestType = "presentation";
        if (callback) {
            this.onPresentationVerified = callback;
            this.onRequestRetrieved = callback;
            this.onRequestCancelled = callback;
            this.onError = callback;
        }
        this.createRequest(this.apiCreatePresentationRequest)
    };
    this.createIssuanceRequest = function (callback) {
        this.requestType = "issuance";
        if (callback) {
            this.onIssuanceSuccesful = callback;
            this.onRequestRetrieved = callback;
            this.onRequestCancelled = callback;
            this.onError = callback;
        }
        this.createRequest(this.apiCreateIssuanceRequest)
    };
    this.createSelfieRequest = function (callback) {
        this.requestType = "selfie";
        if (callback) {
            this.onSelfieTaken = callback;
            this.onRequestRetrieved = callback;
            this.onRequestCancelled = callback;
            this.onError = callback;
        }
        this.createRequest(this.apiCreateSelfieRequest);
    };

    this.setUserPhoto = async function (base64Image) {
        this.log('setUserPhoto(): ' + base64Image );
        const response = await fetch(this.apiSetPhoto, {
            headers: { 'Accept': 'application/json', 'Content-Type': 'image/jpeg', 'rsid': this.uuid, 'X-SignalR-ConnectionId': connection.connectionId },
                                                         method: 'POST',
                                                         body: base64Image
                                                        });
        const respJson = await response.json();
        this.log(respJson);
        if (respJson.error_description) {
            this.onError(this.requestType, respJson.error_description);
            return -1;
        } else {
            return respJson.id;
        }
    };

    this.getRequestStatus = async function (id, requestStatus) {
        const response = await fetch(`${requestService.apiPollPresentationRequest}?id=${id}`);
        const respMsg = await response.json();        
        if (requestStatus == "selfie_taken") {
            this.onSelfieTaken(id, respMsg);
        } else {
            this.onPresentationVerified(id, respMsg);
        }
    };
} // RequestService

///////////////////////////////////////////////////////////////////////////////////////////////////////
// SignalR
///////////////////////////////////////////////////////////////////////////////////////////////////////
var connection = new signalR.HubConnectionBuilder().withUrl("/verifiedIDHub").build();

connection.on("RequestCallback", function (id, requestType, requestStatus, error ) {
    requestService.log(`SignalR - requestType: ${requestType},  requestStatus: ${requestStatus}`);
    switch (requestStatus) {
        case "request_retrieved":
            requestService.onRequestRetrieved(requestType);
            break;
        case "presentation_error":
        case "issuance_error":
            requestService.onError(requestType, error);
            break;
        case "issuance_successful":
            requestService.onIssuanceSuccesful(id, "");
            break;
        // we don't receive the actual details in the SignalR callback here, so we have to call API and get it
        case "presentation_verified":
        case "selfie_taken":
            requestService.getRequestStatus(id, requestStatus);
            break;

    }
});

connection.start().then(function () {
    console.log(`connection.start ${connection.connectionId}`);
}).catch(function (err) {
    return console.error(err.toString());
});
