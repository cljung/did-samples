package com.didsamples.didapijava.controller;

import java.util.*;
import java.util.logging.*;
import java.util.concurrent.*;
import java.util.stream.*;
import java.text.*;
import java.net.*;
import java.io.IOException;
import java.nio.charset.StandardCharsets;
import java.nio.file.*;
import com.fasterxml.jackson.databind.*;
import com.fasterxml.jackson.databind.node.ObjectNode;
import javax.servlet.http.HttpServletRequest;
import javax.servlet.http.HttpServletResponse;
import org.springframework.http.*;
import org.springframework.web.reactive.function.client.WebClient;
import org.springframework.web.reactive.function.BodyInserters;
import org.springframework.web.bind.annotation.*;
import org.springframework.beans.factory.annotation.*;
import org.springframework.cache.annotation.*;
import com.github.benmanes.caffeine.cache.*;
import com.microsoft.aad.msal4j.*;
import com.nimbusds.oauth2.sdk.http.HTTPResponse;

@RestController
@EnableCaching
public class ApiVCController {
    private static final Logger lgr = Logger.getLogger(ApiVCController.class.getName());

    private Cache<String, String> cache = Caffeine.newBuilder()
                                            .expireAfterWrite(15, TimeUnit.MINUTES)
                                            .maximumSize(100)
                                            .build();

    // *********************************************************************************
    // application properties - from envvars
    // *********************************************************************************
    @Value("${aadvc.DidManifest}")
    private String didManifest;

    @Value("${aadvc.PresentationFile}")
    private String presentationJsonFIle;

    @Value("${aadvc.IssuanceFile}")
    private String IssuanceJsonFile;

    @Value("${aadvc.ApiEndpoint}")
    private String apiEndpoint;

    @Value("${aadvc.TenantId}")
    private String tenantId;

    @Value("${aadvc.ApiKey}")
    private String apiKey;

    @Value("${aadvc.client_name}")
    private String clientName;

    @Value("${aadvc.scope}")
    private String scope;

    @Value("${aadvc.ClientId}")
    private String clientId;
    
    @Value("${aadvc.ClientSecret}")
    private String clientSecret;
    
    @Value("${aadvc.Authority}")
    private String aadAuthority;

    // *********************************************************************************
    // helpers
    // *********************************************************************************
    private String getDateTimeAsIso8601() {
        DateFormat df = new SimpleDateFormat("yyyy-MM-dd'T'HH:mm:ss'Z'"); // Quoted "Z" to indicate UTC, no timezone offset
        df.setTimeZone(TimeZone.getTimeZone("UTC"));
        return df.format(new Date());
    }

    public static String getBasePath(HttpServletRequest request) {
        String basePath = request.getScheme() + "://" + request.getServerName() + ":" + request.getServerPort() + "/";
        return basePath;
    }
    public static String getApiPath(HttpServletRequest request) {
        String path = request.getContextPath();
        String basePath = request.getScheme() + "://" + request.getServerName() + ":" + request.getServerPort() + path + "/";
        return basePath;
    }
    public static void traceHttpRequest( HttpServletRequest request ) {
        String method = request.getMethod();
        String requestURL = request.getRequestURL().toString();
        String queryString = request.getQueryString();
    
        if (queryString != null) {
            requestURL += "?" + queryString;
        }
        lgr.info( method + " " + requestURL );
    }

    private static String readFileAllText(String filePath) 
    {
        StringBuilder contentBuilder = new StringBuilder();
        try (Stream<String> stream = Files.lines( Paths.get(filePath), StandardCharsets.UTF_8)) {
            stream.forEach(s -> contentBuilder.append(s).append("\n"));
        } catch (IOException e) {
            e.printStackTrace();
        }
        return contentBuilder.toString();
    }
    
    @Cacheable("manifest")
    private String getDidManifest() {
        lgr.info( "DidManifest=" + didManifest );
        String manifest = cache.getIfPresent("manifest");
        if ( manifest == null || manifest.isEmpty() ) {
            WebClient client = WebClient.create();
            WebClient.ResponseSpec responseSpec = client.get()
                                                        .uri( didManifest )
                                                        .retrieve();
            manifest = responseSpec.bodyToMono(String.class).block();    
            lgr.info( manifest );
            cache.put( "manifest", manifest );
        }
        return manifest;
    }

    private String callVCClientAPI( String payload ) {
        String accessToken = "";
        try {
            accessToken = cache.getIfPresent( "MSALAccessToken" );
            if ( accessToken == null || accessToken.isEmpty() ) {
                lgr.info( "MSAL Acquire AccessToken via Client Credentials" );
                accessToken = getAccessTokenByClientCredentialGrant();
                lgr.info( accessToken );
                cache.put( "MSALAccessToken", accessToken );
            }
        } catch( Exception ex ) {
            ex.printStackTrace();
            return null;
        }
        String endpoint = apiEndpoint.replace("{0}", tenantId );
        lgr.info( "callVCClientAPI: " + endpoint + "\n" + payload );
        WebClient client = WebClient.create();
        WebClient.ResponseSpec responseSpec = client.post()
                                                    .uri( endpoint )
                                                    .header(HttpHeaders.CONTENT_TYPE, MediaType.APPLICATION_JSON_VALUE)
                                                    .header("Authorization", "Bearer " + accessToken)
                                                    .accept(MediaType.APPLICATION_JSON)
                                                    .body(BodyInserters.fromObject(payload))
                                                    .retrieve();
        String responseBody = responseSpec.bodyToMono(String.class).block();    
        lgr.info( responseBody );
        return responseBody;
    }

    public String generatePinCode( Integer length ) {
        int min = 0;
        int max = (int)(Integer.parseInt( "999999999999999999999".substring(0, length) ));
        Integer pin = (Integer)(int)((Math.random() * (max - min)) + min);
        return String.format( String.format("%%0%dd", length), pin );
    }  
    
    private String getAccessTokenByClientCredentialGrant() throws Exception {
        String authority = aadAuthority.replace("{0}", tenantId );
        lgr.info( aadAuthority );
        lgr.info( authority );
        ConfidentialClientApplication app = ConfidentialClientApplication.builder(
                clientId,
                ClientCredentialFactory.createFromSecret(clientSecret))
                .authority(authority)
                .build();
        ClientCredentialParameters clientCredentialParam = ClientCredentialParameters.builder(
                Collections.singleton(scope))
                .build();
        CompletableFuture<IAuthenticationResult> future = app.acquireToken(clientCredentialParam);
        IAuthenticationResult result = future.get();
        return result.accessToken();
    }    

    // *********************************************************************************
    // public
    // *********************************************************************************
    @RequestMapping(value = "/api/logo.png", method = RequestMethod.GET)
    public void method(HttpServletResponse httpServletResponse) {
        String manifest = getDidManifest();        
        String redirectUri = "";
        ObjectMapper objectMapper = new ObjectMapper();
        try {
            JsonNode rootNode = objectMapper.readTree( manifest );
            redirectUri = rootNode.path("display").path("card").path("logo").path("uri").asText();
            httpServletResponse.setHeader("Location", redirectUri);
            httpServletResponse.setStatus(302);
        } catch (java.io.IOException ex) {
            ex.printStackTrace();
            httpServletResponse.setStatus(400);
        }    
    }

    @CrossOrigin(origins = "*")
    @GetMapping("/api/echo")
    public ResponseEntity<String> echo( HttpServletRequest request, @RequestHeader HttpHeaders headers ) {
        traceHttpRequest( request );

        String basePath = getBasePath( request );
        String manifest = getDidManifest();        
        String json = "{}";
        ObjectMapper objectMapper = new ObjectMapper();
        try {
            JsonNode rootNode = objectMapper.readTree( manifest );
            ObjectNode data = objectMapper.createObjectNode();
            data.put("date", getDateTimeAsIso8601() );
            data.put("host", basePath );
            data.put("credentialType", rootNode.path("id").asText() );
            data.put("didIssuer", rootNode.path("input").path("issuer").asText());
            data.put("displayCard", rootNode.path("display").get("card") );
            data.put("contract",  rootNode.path("display").path("contract").asText());
            data.put("buttonColor", "#000080" );
            json = objectMapper.writerWithDefaultPrettyPrinter().writeValueAsString(data);
        } catch (java.io.IOException ex) {
            ex.printStackTrace();
            return ResponseEntity.status(HttpStatus.BAD_REQUEST).body( "Technical error" );
        }    

        HttpHeaders responseHeaders = new HttpHeaders();
        responseHeaders.set("Content-Type", "application/json");    
        return ResponseEntity.ok()
          .headers(responseHeaders)
          .body(json);
    }

    @GetMapping("/api/manifest")
    public ResponseEntity<String> manifest( HttpServletRequest request ) {
        traceHttpRequest( request );
        String manifest = getDidManifest();        
        HttpHeaders responseHeaders = new HttpHeaders();
        responseHeaders.set("Content-Type", "application/json");    
        return ResponseEntity.ok()
          .headers(responseHeaders)
          .body(manifest);
    }

    // *********************************************************************************
    // Issuance
    // *********************************************************************************
    @CrossOrigin(origins = "*")
    @GetMapping("/api/issue-request")
    public ResponseEntity<String> issueRequest( HttpServletRequest request, @RequestHeader HttpHeaders headers ) {
        traceHttpRequest( request );

        lgr.info( "IssuanceJsonFile=" + IssuanceJsonFile );
        String jsonRequest = cache.getIfPresent("IssuanceJsonFile");
        if ( jsonRequest == null || jsonRequest.isEmpty() ) {
            jsonRequest = readFileAllText( IssuanceJsonFile );
        }

        String callback = getBasePath( request ) + "api/issue-request-callback";
        String correlationId = java.util.UUID.randomUUID().toString();

        String payload = "{}";
        ObjectMapper objectMapper = new ObjectMapper();
        Integer pinCodeLength = 0;
        String pinCode = null;
        String responseBody = "";
        try {
            JsonNode rootNode = objectMapper.readTree( jsonRequest );
            ((ObjectNode)(rootNode.path("registration"))).put("clientName", clientName );
            ((ObjectNode)(rootNode.path("callback"))).put("url", callback );
            ((ObjectNode)(rootNode.path("callback"))).put("state", correlationId );
            ((ObjectNode)(rootNode.path("callback").path("headers"))).put("api-key", apiKey );
            if ( rootNode.path("issuance").has("pin") ) {
                pinCodeLength = rootNode.path("issuance").path("pin").path("length").asInt();
                if ( pinCodeLength <= 0) {
                    ((ObjectNode)rootNode.path("issuance")).remove("pin");
                } else {
                    pinCode = generatePinCode( pinCodeLength );
                    ((ObjectNode)(rootNode.path("issuance").path("pin"))).put("value", pinCode );
                }
            }
            payload = objectMapper.writerWithDefaultPrettyPrinter().writeValueAsString(rootNode);
            responseBody = callVCClientAPI( payload );
            JsonNode apiResponse = objectMapper.readTree( responseBody );
            ((ObjectNode)apiResponse).put( "id", correlationId );
            if ( pinCodeLength > 0 ) {
                ((ObjectNode)apiResponse).put( "pin", pinCode );
            }
            responseBody = objectMapper.writerWithDefaultPrettyPrinter().writeValueAsString(apiResponse);
        } catch (java.io.IOException ex) {
            ex.printStackTrace();
            return ResponseEntity.status(HttpStatus.BAD_REQUEST).body( "Technical error" );
        }    
        
        HttpHeaders responseHeaders = new HttpHeaders();
        responseHeaders.set("Content-Type", "application/json");    
        return ResponseEntity.ok()
          .headers(responseHeaders)
          .body( responseBody );
    }

    @RequestMapping(value = "/api/issue-request-callback", method = RequestMethod.POST, produces = "application/json", consumes = "application/json")
    public ResponseEntity<String> issueRequestCallback( HttpServletRequest request
                                                      , @RequestHeader HttpHeaders headers
                                                      , @RequestBody String body ) {
        traceHttpRequest( request );
        lgr.info( body );
        ObjectMapper objectMapper = new ObjectMapper();
        try {
            if ( !request.getHeader("api-key").equals(apiKey) ) {
                lgr.info( "api-key wrong or missing" );
                return ResponseEntity.status(HttpStatus.UNAUTHORIZED).body( "api-key wrong or missing" );
            }
            JsonNode presentationResponse = objectMapper.readTree( body );
            String code = presentationResponse.path("code").asText();
            ObjectNode data = null;
            if ( code.equals( "request_retrieved" )  ) {
                data = objectMapper.createObjectNode();
                data.put("message", "QR Code is scanned. Waiting for issuance to complete..." );
            }
            if ( code.equals("issuance_successful") ) {
                data = objectMapper.createObjectNode();
                data.put("message", "Credential successfully issued" );
            }
            if ( code.equals( "issuance_error" ) ) {
                data = objectMapper.createObjectNode();
                data.put("message", presentationResponse.path("error").path("message").asText() );
            }
            if ( data != null ) {
                data.put("status", code );
                cache.put( presentationResponse.path("state").asText(), objectMapper.writerWithDefaultPrettyPrinter().writeValueAsString(data) );
            }
        } catch (java.io.IOException ex) {
            ex.printStackTrace();
            return ResponseEntity.status(HttpStatus.BAD_REQUEST).body( "Technical error" );
        }    
        
        return ResponseEntity.ok()
          .body( "{}" );
    }

    @CrossOrigin(origins = "*")
    @GetMapping("/api/issue-response-status")
    public ResponseEntity<String> issueResponseStatus( HttpServletRequest request
                                                            , @RequestHeader HttpHeaders headers
                                                            , @RequestParam String id ) {
        traceHttpRequest( request );

        String responseBody = "";
        String data = cache.getIfPresent( id ); // id == correlationId
        if ( !(data == null || data.isEmpty()) ) {
            ObjectMapper objectMapper = new ObjectMapper();
            try {
                JsonNode cacheData = objectMapper.readTree( data  );
                ObjectNode statusResponse = objectMapper.createObjectNode();
                statusResponse.put("status", cacheData.path("status").asText() );
                statusResponse.put("message", cacheData.path("message").asText() );
                responseBody = objectMapper.writerWithDefaultPrettyPrinter().writeValueAsString(statusResponse);
            } catch (java.io.IOException ex) {
                ex.printStackTrace();
                return ResponseEntity.status(HttpStatus.BAD_REQUEST).body( "Technical error" );
            }    
        }
        HttpHeaders responseHeaders = new HttpHeaders();
        responseHeaders.set("Content-Type", "application/json");    
        return ResponseEntity.ok()
          .headers(responseHeaders)
          .body( responseBody );
    }

    // *********************************************************************************
    // Presentation
    // *********************************************************************************
    @CrossOrigin(origins = "*")
    @GetMapping("/api/presentation-request")
    public ResponseEntity<String> presentationRequest( HttpServletRequest request, @RequestHeader HttpHeaders headers ) {
        traceHttpRequest( request );

        lgr.info( "presentationJsonFIle=" + presentationJsonFIle );
        String jsonRequest = cache.getIfPresent("presentationJsonFIle");
        if ( jsonRequest == null || jsonRequest.isEmpty() ) {
            jsonRequest = readFileAllText( presentationJsonFIle );
        }

        String callback = getBasePath( request ) + "api/presentation-request-callback";
        String correlationId = java.util.UUID.randomUUID().toString();

        String payload = "{}";
        ObjectMapper objectMapper = new ObjectMapper();
        String responseBody = "";
        try {
            JsonNode rootNode = objectMapper.readTree( jsonRequest );
            ((ObjectNode)(rootNode.path("registration"))).put("clientName", clientName );
            ((ObjectNode)(rootNode.path("callback"))).put("url", callback );
            ((ObjectNode)(rootNode.path("callback"))).put("state", correlationId );
            ((ObjectNode)(rootNode.path("callback").path("headers"))).put("api-key", apiKey );
            payload = objectMapper.writerWithDefaultPrettyPrinter().writeValueAsString(rootNode);
            responseBody = callVCClientAPI( payload );
            JsonNode apiResponse = objectMapper.readTree( responseBody );
            ((ObjectNode)apiResponse).put( "id", correlationId );
            responseBody = objectMapper.writerWithDefaultPrettyPrinter().writeValueAsString(apiResponse);
        } catch (java.io.IOException ex) {
            ex.printStackTrace();
            return ResponseEntity.status(HttpStatus.BAD_REQUEST).body( "Technical error" );
        }    
        
        HttpHeaders responseHeaders = new HttpHeaders();
        responseHeaders.set("Content-Type", "application/json");    
        return ResponseEntity.ok()
          .headers(responseHeaders)
          .body( responseBody );
    }

    @RequestMapping(value = "/api/presentation-request-callback", method = RequestMethod.POST, produces = "application/json", consumes = "application/json")
    public ResponseEntity<String> presentationRequestCallback( HttpServletRequest request
                                                             , @RequestHeader HttpHeaders headers
                                                             , @RequestBody String body ) {
        traceHttpRequest( request );
        lgr.info( body );
        ObjectMapper objectMapper = new ObjectMapper();
        try {
            if ( !request.getHeader("api-key").equals(apiKey) ) {
                lgr.info( "api-key wrong or missing" );
                return ResponseEntity.status(HttpStatus.UNAUTHORIZED).body( "api-key wrong or missing" );
            }
            JsonNode presentationResponse = objectMapper.readTree( body );
            String code = presentationResponse.path("code").asText();
            ObjectNode data = null;

            if ( code.equals( "request_retrieved" ) ) {
                data = objectMapper.createObjectNode();
                data.put("status", 1 );
                data.put("message", "QR Code is scanned. Waiting for validation..." );
            }
            if ( code.equals( "presentation_verified") ) {
                data = objectMapper.createObjectNode();
                //data.put("message", presentationResponse.path("issuers").get(0).path("claims").path("displayName").asText() );
                data.put("status", 2 );
                data.put("message", "VC Presented" );
                data.set("presentationResponse", presentationResponse );
            }
            if ( data != null ) {
                data.put("code", code );
                cache.put( presentationResponse.path("state").asText(), objectMapper.writerWithDefaultPrettyPrinter().writeValueAsString(data) );
            }
        } catch (java.io.IOException ex) {
            lgr.info( ex.getMessage() );
            ex.printStackTrace();
            return ResponseEntity.status(HttpStatus.BAD_REQUEST).body( "Technical error" );
        }    
        
        return ResponseEntity.ok().body( "{}" );
    }

    @CrossOrigin(origins = "*")
    @GetMapping("/api/presentation-response-status")
    public ResponseEntity<String> presentationResponseStatus( HttpServletRequest request
                                                            , @RequestHeader HttpHeaders headers
                                                            , @RequestParam String id ) {
        traceHttpRequest( request );

        String responseBody = "";
        String data = cache.getIfPresent( id ); // id == correlationId
        if ( !(data == null || data.isEmpty()) ) {
            ObjectMapper objectMapper = new ObjectMapper();
            try {
                JsonNode cacheData = objectMapper.readTree( data  );
                ObjectNode statusResponse = objectMapper.createObjectNode();
                statusResponse.put("status", cacheData.path("status").asText() );
                statusResponse.put("message", cacheData.path("message").asText() );
                responseBody = objectMapper.writerWithDefaultPrettyPrinter().writeValueAsString(statusResponse);
            } catch (java.io.IOException ex) {
                ex.printStackTrace();
                return ResponseEntity.status(HttpStatus.BAD_REQUEST).body( "Technical error" );
            }    
        }
        HttpHeaders responseHeaders = new HttpHeaders();
        responseHeaders.set("Content-Type", "application/json");    
        return ResponseEntity.ok()
          .headers(responseHeaders)
          .body( responseBody );
    }

    // *********************************************************************************
    // B2C
    // *********************************************************************************
    @RequestMapping(value = "/api/presentation-response-b2c", method = RequestMethod.POST, produces = "application/json", consumes = "application/json")
    public ResponseEntity<String> presentationResponseB2C( HttpServletRequest request
                                                             , @RequestHeader HttpHeaders headers
                                                             , @RequestBody String body ) {
        traceHttpRequest( request );
        lgr.info( body );
        String responseBody = "";
        try {
            ObjectMapper objectMapper = new ObjectMapper();
            JsonNode b2cRequest = objectMapper.readTree( body );
            String correlationId = b2cRequest.path("id").asText();
            String data = cache.getIfPresent( correlationId ); 
            if ( (data == null || data.isEmpty()) ) {
                return ResponseEntity.status(HttpStatus.CONFLICT).body( formatB2CError( "Verifiable Credentials not presented" ) );
            } 
            JsonNode cacheData = objectMapper.readTree( data  );
            String didSubject = cacheData.path("presentationResponse").path("subject").asText();
            String didIssuer = cacheData.path("presentationResponse").path("issuers").get(0).path("authority").asText();
            JsonNode vcClaims = cacheData.path("presentationResponse").path("issuers").get(0).path("claims");
            String firstName = getClaimValue( vcClaims, "firstName" );
            String lastName = getClaimValue( vcClaims, "lastName" );
            String displayName = getClaimValue( vcClaims, "displayName" );
            if ( displayName == null || displayName.isEmpty() ) { 
                displayName = firstName + " " + lastName;
            }

            String manifest = getDidManifest();        
            JsonNode rootNode = objectMapper.readTree( manifest );
            String credentialType = rootNode.path("id").asText();

            ((ObjectNode) vcClaims).put("vcType", credentialType);
            ((ObjectNode) vcClaims).put("vcIss", didIssuer);
            ((ObjectNode) vcClaims).put("vcSub", didSubject);
            ((ObjectNode) vcClaims).put("vcKey", didSubject.replace("did:ion:", "did.ion.").split(":")[0] );
            ((ObjectNode) vcClaims).put("displayName", displayName);

            responseBody = objectMapper.writerWithDefaultPrettyPrinter().writeValueAsString(vcClaims);
        } catch (java.io.IOException ex) {
            ex.printStackTrace();
            return ResponseEntity.status(HttpStatus.CONFLICT).body( formatB2CError( "Technical error" ) );
        }    
        
        HttpHeaders responseHeaders = new HttpHeaders();
        responseHeaders.set("Content-Type", "application/json");    
        return ResponseEntity.ok()
          .headers(responseHeaders)
          .body( responseBody );
    }

    private String formatB2CError( String message ) {
        return "{\"version\": \"1.0.0\", \"status\": 400, \"userMessage\": \"{0}}\"}".replace( "{0}", message );
    }

    private String getClaimValue( JsonNode node, String name ) {
        if ( node.has(name) ) { 
            return node.path(name).asText();
        } else {
            return null;
        }
    }
} // cls