<?php
require_once "Controller/Api/VerifierController.php";

$uri = parse_url($_SERVER['REQUEST_URI'], PHP_URL_PATH);
$uriPath = explode( '/', $uri );

/**
 * Setup REST API routes
 */
if ( str_starts_with( $uri, "/api/verifier/") ) {
    $verifier = new VerifierController();
    if ( $uri == '/api/verifier/presentation-request') {
      $verifier->presentationRequest();
    }
    if ( $uri == '/api/verifier/presentation-response') {
      $verifier->presentationResponse();
    }
    if ( $uri == '/api/verifier/presentation-request-callback') {
      $verifier->presentationRequestCallback();
    }
    exit();
}
