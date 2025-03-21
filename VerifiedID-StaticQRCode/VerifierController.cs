using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Identity.Client;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Diagnostics;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using System.Security.Cryptography.X509Certificates;
using System.Net.Http.Headers;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Identity.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using System.Security.Policy;
using Microsoft.AspNetCore.Connections;
using Microsoft.AspNetCore.Http.Extensions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Azure.Core;
using System.Linq;

namespace VerifiedIDSignalR
{
    [Route("api/[controller]/[action]")]
    public class VerifierController : Controller
    {
        //protected readonly AppSettingsModel AppSettings;
        protected IMemoryCache _cache;
        protected readonly ILogger<VerifierController> _log;
        private IHttpClientFactory _httpClientFactory;
        private string _apiKey;
        private IConfiguration _configuration;
        public VerifierController(IConfiguration configuration, IMemoryCache memoryCache, ILogger<VerifierController> log, IHttpClientFactory httpClientFactory)
        {
            _configuration = configuration;
            _cache = memoryCache;
            _log = log;
            _httpClientFactory = httpClientFactory;
            _apiKey = System.Environment.GetEnvironmentVariable("API-KEY");
        }
        /// <summary>
        /// Subscribes to listening to presentation events happening at a station
        /// </summary>
        /// <param name="stationId"></param>
        /// <returns>201 created or 200 OK (if already subscribed)</returns>
        [AllowAnonymous]
        [HttpPost( "/api/subscribeToStation/{stationId}" )]
        public async Task<ActionResult> SubscribeToStation( string stationId ) {
            _log.LogTrace( this.Request.GetDisplayUrl() );
            try {
                bool added = false;
                string connectionId = this.Request.Headers["X-SignalR-ConnectionId"].ToString();
                if ( !_cache.TryGetValue<List<string>>( stationId, out List<string> connections ) ) {
                    connections = new List<string>();
                }
                if ( !connections.Contains( connectionId ) ) {
                    connections.Add( connectionId );
                    added = true;
                }
                _cache.Set( stationId, connections );
                if ( added )
                     return new CreatedResult();
                else return new OkResult();
            } catch (Exception ex) {
                return BadRequest( new { error = "400", error_description = ex.Message } );
            }
        }

        /// <summary>
        /// This method returns a static link that points back to our service procy
        /// </summary>
        /// <returns>JSON static deeplink to this API</returns>
        [AllowAnonymous]
        [HttpGet( "/api/verifier/get-static-link" )]
        [HttpGet( "/api/verifier/get-static-link/{stationId}" )]
        public async Task<ActionResult> GetStaticLink(string stationId ) {
            _log.LogTrace( this.Request.GetDisplayUrl() );
            try {
                string correlationId = Guid.NewGuid().ToString();
                var resp = new {
                    requestId = correlationId,
                    url = $"openid-vc://?request_uri={GetRequestHostName()}/api/verifier/presentation-request-proxy/{correlationId}/{stationId}",
                    expiry = (int)(DateTime.UtcNow.AddDays( 1 ) - new DateTime( 1970, 1, 1 )).TotalSeconds,
                    id = correlationId
                };
                var cacheData = new {
                    status = "request_created",
                    message = "Static QR Code created",
                    expiry = resp.expiry,
                    connectionId = this.Request.Headers["X-SignalR-ConnectionId"].ToString(),
                    stationId = stationId
                };
                _cache.Set( correlationId, JsonConvert.SerializeObject( cacheData ), DateTimeOffset.Now.AddDays(1 ) );
                string respJson = JsonConvert.SerializeObject( resp );
                _log.LogTrace( "API static request Response\n{0}", respJson );
                return new ContentResult { ContentType = "application/json", Content = respJson };
            } catch (Exception ex) {
                return BadRequest( new { error = "400", error_description = ex.Message } );
            }
        }
        /// <summary>
        /// This method get's called by the Microsoft Authenticator when it scans the QR code and 
        /// wants to retrieve the request. We call the VC Request Service API, get the request_uri 
        /// in the response, invoke that url and retrieve the response and pass it to the caller.
        /// This way this API acts as a proxy.
        /// </summary>
        /// <returns></returns>
        [AllowAnonymous]
        [HttpGet( "/api/verifier/presentation-request-proxy/{correlationId}" )]
        [HttpGet( "/api/verifier/presentation-request-proxy/{correlationId}/{stationId}" )]
        public async Task<ActionResult> StaticPresentationReferenceProxy( string stationId, string correlationId ) {
            _log.LogTrace( this.Request.GetDisplayUrl() );
            try {
                var accessToken = await MsalAccessTokenHandler.GetAccessToken( _configuration );
                if (accessToken.Item1 == String.Empty) {
                    _log.LogError( String.Format( "failed to acquire accesstoken: {0} : {1}", accessToken.error, accessToken.error_description ) );
                    return BadRequest( new { error = accessToken.error, error_description = accessToken.error_description } );
                }

                string signalRconnectionId = null;
                if (_cache.TryGetValue( correlationId, out string requestState )) {
                    JObject reqState = JObject.Parse( requestState );
                    signalRconnectionId = reqState["connectionId"].ToString();
                }
                // 1. Create a Presentation Request and call the Client API to get the 'real' request_uri
                correlationId = Guid.NewGuid().ToString();
                
                string[] acceptedIssuers = null;
                string buf = _configuration.GetValue("VerifiedID:acceptedIssuers", "" );
                if ( !string.IsNullOrWhiteSpace( buf ) ) { 
                    if ( buf == "*")
                         acceptedIssuers = new string[0];
                    else acceptedIssuers = buf.Split(";");
                }
                PresentationRequest request = CreatePresentationRequest( correlationId, null, acceptedIssuers );
                if (!hasFaceCheck( request ) && _configuration.GetValue( "VerifiedID:useFaceCheck", false )) {
                    AddFaceCheck( request, null, this.Request.Query["photoClaimName"] ); // when qp is null, appsettings value is used
                }

                string jsonString = JsonConvert.SerializeObject( request, Newtonsoft.Json.Formatting.None, new JsonSerializerSettings {
                    NullValueHandling = NullValueHandling.Ignore
                } );
                _log.LogTrace( "VC Client API Request\n{0}", jsonString );
                string url = $"{_configuration["VerifiedID:ApiEndpoint"]}createPresentationRequest";

                var client = _httpClientFactory.CreateClient();
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue( "Bearer", accessToken.token );
                HttpResponseMessage res = await client.PostAsync( url, new StringContent( jsonString, Encoding.UTF8, "application/json" ) );
                string contents = await res.Content.ReadAsStringAsync();
                HttpStatusCode statusCode = res.StatusCode;
                if (statusCode == HttpStatusCode.Created) {
                    _log.LogTrace( "succesfully called Request Service API" );
                    // 2. Get the 'real' request_uri from the response and make a HTTP GET to it to retrieve the JWT Token for the Authenticator
                    JObject apiResp = JObject.Parse( contents );
                    string request_uri = apiResp["url"].ToString().Split( "=" )[1]; // openid-vc://?request_uri=<...url to retrieve request...>
                    string response = null;
                    string contentType = null;

                    // simulate the wallet and retrieve the presentation request
                    var clientWalletSim = _httpClientFactory.CreateClient();
                    // forward headers we got from Authenticator to Verified ID
                    string[] headersToForward = new string[] { "User-Agent", "Ms-Cv", "Prefer", "Accept-Language" };
                    foreach (string hdrKey in headersToForward) {
                        if (this.Request.Headers.ContainsKey( hdrKey )) {
                            clientWalletSim.DefaultRequestHeaders.Add( hdrKey, this.Request.Headers[hdrKey].ToString() );
                        }
                    }
                    res = await clientWalletSim.GetAsync( request_uri );
                    response = await res.Content.ReadAsStringAsync();
                    statusCode = res.StatusCode;
                    contentType = res.Content.Headers.ContentType.ToString();

                    //We use in memory cache to keep state about the request. The UI will check the state when calling the presentationResponse method
                    var cacheData = new {
                        id = correlationId,
                        status = "request_created",
                        message = "Waiting for QR code to be scanned",
                        expiry = apiResp["expiry"].ToString(),
                        connectionId = signalRconnectionId,
                        stationId = stationId
                    };
                    _cache.Set( request.callback.state, JsonConvert.SerializeObject( cacheData )
                                    , DateTimeOffset.Now.AddSeconds( _configuration.GetValue<int>( "AppSettings:CacheExpiresInSeconds", 300 ) ) );

                    // Return the response to the Authenticator + pass back some headers we got from Verified ID when retrieving the JWT
                    string[] headersToPassBack = new string[] { "request-id", "ms-cv", "Server-Timing" };
                    foreach (string hdrKey in headersToPassBack) {
                        if (res.Headers.TryGetValues( hdrKey, out IEnumerable<string>? hdrValues) ) {
                            this.Response.Headers.Append( hdrKey, hdrValues.First() );
                        }                        
                    }

                    _log.LogTrace( "VC Client API GET Response\nStatusCode={0}\nContent-Type={1}\n{2}", statusCode, contentType, response );
                    return new ContentResult { StatusCode = (int)statusCode, ContentType = contentType, Content = response };
                } else {
                    _log.LogError( "Error calling Verified ID API: " + contents );
                    return BadRequest( new { error = "400", error_description = "Verified ID API error response: " + contents, request = jsonString } );
                }
            } catch (Exception ex) {
                return BadRequest( new { error = "400", error_description = ex.Message } );
            }
        }

        //
        //this function is called from the UI to get some details to display in the UI about what
        //credential is being asked for
        //
        [AllowAnonymous]
        [HttpGet("/api/verifier/get-presentation-details")]
        public ActionResult getPresentationDetails() {
            _log.LogTrace( this.Request.GetDisplayUrl() );
            try {
                PresentationRequest request = CreatePresentationRequest();
                var details = new {
                    clientName = request.registration.clientName,
                    purpose = request.registration.purpose,
                    VerifierAuthority = request.authority,
                    type = request.requestedCredentials[0].type,
                    acceptedIssuers = request.requestedCredentials[0].acceptedIssuers.ToArray(),
                    PhotoClaimName = _configuration.GetValue( "VerifiedID:PhotoClaimName", "" )
                };
                return new ContentResult { ContentType = "application/json", Content = JsonConvert.SerializeObject(details) };
            } catch (Exception ex) {
                return BadRequest(new { error = "400", error_description = ex.Message });
            }
        }

        //some helper functions
        protected string GetRequestHostName()
        {
            string scheme = "https";// : this.Request.Scheme;
            string originalHost = this.Request.Headers["x-original-host"];
            string hostname = "";
            if (!string.IsNullOrEmpty(originalHost))
                hostname = string.Format("{0}://{1}", scheme, originalHost);
            else hostname = string.Format("{0}://{1}", scheme, this.Request.Host);
            return hostname;
        }
        /// <summary>
        /// This method creates a PresentationRequest object instance from configuration
        /// </summary>
        /// <param name="stateId"></param>
        /// <param name="credentialType"></param>
        /// <param name="acceptedIssuers"></param>
        /// <returns></returns>
        public PresentationRequest CreatePresentationRequest( string stateId = null, string credentialType = null, string[] acceptedIssuers = null ) {
            PresentationRequest request = new PresentationRequest() {
                includeQRCode = _configuration.GetValue( "VerifiedID:includeQRCode", false ),
                authority = _configuration["VerifiedID:DidAuthority"],
                registration = new Registration() {
                    clientName = _configuration["VerifiedID:client_name"],
                    purpose = _configuration.GetValue( "VerifiedID:purpose", "" )
                },
                callback = new Callback() {
                    url = $"{GetRequestHostName()}/api/verifier/presentationcallback",
                    state = (string.IsNullOrWhiteSpace( stateId ) ? Guid.NewGuid().ToString() : stateId),
                    headers = new Dictionary<string, string>() { { "api-key", this._apiKey } }
                },
                includeReceipt = _configuration.GetValue( "VerifiedID:includeReceipt", false ),
                requestedCredentials = new List<RequestedCredential>(),
            };
            if ("" == request.registration.purpose) {
                request.registration.purpose = null;
            }
            if (string.IsNullOrEmpty( credentialType )) {
                credentialType = _configuration["VerifiedID:CredentialType"];
            }
            List<string> okIssuers;
            if (acceptedIssuers == null) {
                okIssuers = new List<string>( new string[] { _configuration["VerifiedID:DidAuthority"] } );
            } else {
                okIssuers = new List<string>( acceptedIssuers );
            }
            bool allowRevoked = _configuration.GetValue( "VerifiedID:allowRevoked", false );
            bool validateLinkedDomain = _configuration.GetValue( "VerifiedID:validateLinkedDomain", true );
            AddRequestedCredential( request, credentialType, okIssuers, allowRevoked, validateLinkedDomain );
            return request;
        }
        public PresentationRequest AddRequestedCredential( PresentationRequest request
                                                , string credentialType, List<string> acceptedIssuers
                                                , bool allowRevoked = false, bool validateLinkedDomain = true ) {
            request.requestedCredentials.Add( new RequestedCredential() {
                type = credentialType,
                acceptedIssuers = (null == acceptedIssuers ? new List<string>() : acceptedIssuers),
                configuration = new Configuration() {
                    validation = new Validation() {
                        allowRevoked = allowRevoked,
                        validateLinkedDomain = validateLinkedDomain
                    }
                }
            } );
            return request;
        }
        private PresentationRequest AddFaceCheck( PresentationRequest request, string credentialType, string sourcePhotoClaimName = "photo", int matchConfidenceThreshold = 70 ) {
            if ( string.IsNullOrWhiteSpace(sourcePhotoClaimName) ){
                sourcePhotoClaimName = _configuration.GetValue( "VerifiedID:PhotoClaimName", "photo" );
            }
            foreach (var requestedCredential in request.requestedCredentials) {
                if (null == credentialType || requestedCredential.type == credentialType) {
                    requestedCredential.configuration.validation.faceCheck = new FaceCheck() { sourcePhotoClaimName = sourcePhotoClaimName, matchConfidenceThreshold = matchConfidenceThreshold };
                    request.includeReceipt = false; // not supported while doing faceCheck
                }
            }
            return request;
        }
        private bool hasFaceCheck( PresentationRequest request ) {
            foreach (var requestedCredential in request.requestedCredentials) {
                if ( null != requestedCredential.configuration.validation.faceCheck ) {
                    return true;
                }
            }
            return false;
        }

    } // cls
} // ns
