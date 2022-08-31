<?php
require_once "Controller/Api/BaseController.php";

class VerifierController extends BaseController
{
    public function sendResponse( $responseData, $strErrorHeader, $strErrorDesc)
    {
        // send output
        if (!$strErrorDesc) {
            $this->sendOutput( $responseData, ['Content-Type: application/json', 'HTTP/1.1 200 OK'] );
        } else {
            $this->sendOutput(json_encode( ['error' => $strErrorDesc] ), ['Content-Type: application/json', $strErrorHeader] );
        }
    }
    /**
     * GET /api/verifier/presentation-request
     */
    public function presentationRequest()
    {
        $strErrorDesc = '';
        $strErrorHeader = '';
        $responseData = '';
        $requestMethod = $_SERVER["REQUEST_METHOD"];
 
        if (strtoupper($requestMethod) == 'GET') {
            try { 
                $id = $this->create_guid();
                $string = file_get_contents("presentation_request_config.json");
                $payload = json_decode($string, true);
                $payload["callback"]["state"] = $id;
                $payload["callback"]["headers"]["api-key"] = API_KEY;
                $payload["callback"]["url"] = 'https://' . $_SERVER['HTTP_HOST'] . '/api/verifier/presentation-request-callback';
                $payload["registration"]["clientName"] = REGISTRATION_CLIENT_NAME;
                $payload["authority"] = VERIFIER_AUTHORITY;
                $payload["requestedCredentials"][0]["type"] = CREDENTIAL_TYPE;
                $payload["requestedCredentials"][0]["acceptedIssuers"][0] = ISSUER_AUTHORITY;
                $jsonPayload = json_encode($payload, JSON_UNESCAPED_SLASHES);

                $accessToken = $this->acquireAccessToken();
                $apiEndpoint = VERIFIEDID_ENDPOINT . 'verifiableCredentials/createPresentationRequest';
                $this->console_log( $jsonPayload );
                $responseData = $this->httpPost( $apiEndpoint, 
                                                ['Content-type: application/json', 'Authorization: Bearer ' . $accessToken ], 
                                                $jsonPayload );
  
                // add our id
                $response = json_decode($responseData, true);
                $response['id'] = $id;
                $responseData = json_encode($response, JSON_UNESCAPED_SLASHES);
                $this->console_log( $responseData );
            } catch (Error $e) {
                $strErrorDesc = $e->getMessage().'Something went wrong! Please contact support.';
                $strErrorHeader = 'HTTP/1.1 500 Internal Server Error';
            }
        } else {
            $strErrorDesc = 'Method not supported';
            $strErrorHeader = 'HTTP/1.1 422 Unprocessable Entity';
        } 
        $this->sendResponse( $responseData, $strErrorHeader, $strErrorDesc );
    }
    /**
     * POST /api/verifier/presentation-request-callback
     */
    public function presentationRequestCallback()
    {
        $strErrorDesc = '';
        $strErrorHeader = '';
        $responseData = "{}";
        $requestMethod = $_SERVER["REQUEST_METHOD"];
        if (strtoupper($requestMethod) == 'POST') {
            try {
                $json = file_get_contents('php://input');
                $this->console_log( $json );
                $responseData = json_decode($json, true);
                $id = $responseData["state"];
                if ( $responseData["requestStatus"] == "request_retrieved") {
                    $cacheData = [
                        "status" => $responseData["requestStatus"],
                        "message" => "QR Code is scanned. Waiting for validation..."
                    ];
                    $this->putCache( $id, $cacheData );
                }
                if ( $responseData["requestStatus"] == "presentation_verified") {
                    $cacheData = [
                        "status" => $responseData["requestStatus"],
                        "message" => "Presentation received",
                        "payload" => $responseData["verifiedCredentialsData"],
                        "subject" => $responseData["subject"],
                        "firstName" => $responseData["verifiedCredentialsData"][0]["claims"]["firstName"],
                        "lastName" => $responseData["verifiedCredentialsData"][0]["claims"]["lastName"],
                        "presentationResponse" => $responseData
                    ];
                    $this->putCache( $id, $cacheData );
                }
            } catch (Error $e) {
                $strErrorDesc = $e->getMessage().'Something went wrong! Please contact support.';
                $strErrorHeader = 'HTTP/1.1 500 Internal Server Error';
            }
        } else {
            $strErrorDesc = 'Method not supported';
            $strErrorHeader = 'HTTP/1.1 422 Unprocessable Entity';
        }
 
        // send output
        if (!$strErrorDesc) {
            $this->sendOutput( $responseData, [ 'Content-Type: application/json', 'HTTP/1.1 200 OK'] );
        } else {
            $this->sendOutput(json_encode( ['error' => $strErrorDesc] ), ['Content-Type: application/json', $strErrorHeader] );
        }
    }
    /**
     * GET /api/verifier/presentation-response
     */
    public function presentationResponse()
    {
        $strErrorDesc = '';
        $strErrorHeader = '';
        $cacheData = "";
        $requestMethod = $_SERVER["REQUEST_METHOD"];
 
        if (strtoupper($requestMethod) == 'GET') {
            try {
                $id = -1;
                if (isset($_GET['id'])) {
                    $id = $_GET['id'];
                    $cacheData = $this->getCache( $id );
                    $this->console_log( $cacheData );
                    if ( isset($cacheData["status"]) && $cacheData["status"] == "presentation_verified") {
                        $this->deleteCache( $id );
                    }
                }
                if ( $cacheData === "" ) {
                    $cacheData = "{}";
                }                
                $cacheData = json_encode( $cacheData, JSON_UNESCAPED_SLASHES);
            } catch (Error $e) {
                $strErrorDesc = $e->getMessage().'Something went wrong! Please contact support.';
                $strErrorHeader = 'HTTP/1.1 500 Internal Server Error';
            }
        } else {
            $strErrorDesc = 'Method not supported';
            $strErrorHeader = 'HTTP/1.1 422 Unprocessable Entity';
        }
 
        $this->sendResponse( $cacheData, $strErrorHeader, $strErrorDesc );
    }

}