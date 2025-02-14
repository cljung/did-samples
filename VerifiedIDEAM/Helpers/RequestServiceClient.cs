using Microsoft.Extensions.Configuration;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json;
using System.Net.Http.Headers;
using System.Net.Http;
using System.Text;
using VerifiedIDEAM.Controllers;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.AspNetCore.Mvc;
using System.Net;

namespace VerifiedIDEAM.Helpers
{
    public class RequestServiceClient
    {
        private IConfiguration _configuration;
        private ILogger<OidcController> _log;
        private IHttpClientFactory _httpClientFactory;
        private IMemoryCache _cache;
        private string _apiKey;
        private string _hostName;

        public enum ConstraintType
        {
            Unknown,
            Value,
            Contains,
            StartsWith
        };

        public RequestServiceClient(IConfiguration configuration, ILogger<OidcController> log, IHttpClientFactory httpClientFactory, IMemoryCache cache, string hostName)
        {
            _configuration = configuration;
            _log = log;
            _httpClientFactory = httpClientFactory;
            _cache = cache;
            _apiKey = Environment.GetEnvironmentVariable("API-KEY");
            _hostName = hostName;
        }
        public HttpResponseMessage CallRequestServiceAPI(PresentationRequest request, string accessToken)
        {
            string jsonString = JsonConvert.SerializeObject(request, Formatting.None, new JsonSerializerSettings
            {
                NullValueHandling = NullValueHandling.Ignore
            });
            _log.LogTrace($"Request API payload: {jsonString}");
            var client = _httpClientFactory.CreateClient();
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
            HttpResponseMessage res = client.PostAsync($"{_configuration["VerifiedID:ApiEndpoint"]}createPresentationRequest", new StringContent(jsonString, Encoding.UTF8, "application/json")).Result;
            return res;
        }
        public PresentationRequestResponse ProcessRequestServiceAPIResponse(PresentationRequest request, string response)
        {
            _log.LogTrace("succesfully called Request API");
            JObject requestConfig = JObject.Parse(response);
            requestConfig.Add(new JProperty("id", request.callback.state));
            string jsonString = JsonConvert.SerializeObject(requestConfig);
            //We use in memory cache to keep state about the request. The UI will check the state when calling the presentationResponse method
            var cacheData = new
            {
                status = "request_created",
                message = "Waiting for QR code to be scanned",
                expiry = requestConfig["expiry"].ToString()
            };
            PresentationRequestResponse presentationRequestResponse = JsonConvert.DeserializeObject<PresentationRequestResponse>( jsonString );
            _cache.Set( presentationRequestResponse.requestId, JsonConvert.SerializeObject(cacheData)
                                              , DateTimeOffset.Now.AddSeconds(_configuration.GetValue("AppSettings:CacheExpiresInSeconds", 300)));
            return presentationRequestResponse;
        }
        public PresentationRequest CreatePresentationRequest(string stateId, string credentialType, List<string> acceptedIssuers )
        {
            PresentationRequest request = new PresentationRequest()
            {
                includeQRCode = _configuration.GetValue("VerifiedID:includeQRCode", false),
                authority = _configuration["VerifiedID:DidAuthority"],
                registration = new Registration()
                {
                    clientName = _configuration.GetValue("VerifiedID:client_name", "Entra Verified ID MFA"),
                    purpose = _configuration.GetValue("VerifiedID:purpose", "To prove your identity")
                },
                callback = new Callback()
                {
                    url = $"{_hostName}/api/verifier/presentationcallback",
                    state = string.IsNullOrWhiteSpace(stateId) ? Guid.NewGuid().ToString() : stateId,
                    headers = new Dictionary<string, string>() { { "api-key", _apiKey } }
                },
                includeReceipt = _configuration.GetValue("VerifiedID:includeReceipt", false),
                requestedCredentials = new List<RequestedCredential>(),
            };
            if ("" == request.registration.purpose)
            {
                request.registration.purpose = null;
            }            
            AddRequestedCredential( request, credentialType, acceptedIssuers );
            
            return request;
        }
        public PresentationRequest AddRequestedCredential(PresentationRequest request
                                , string credentialType, List<string> acceptedIssuers)
        {
            if (string.IsNullOrEmpty(credentialType))
            {
                credentialType = _configuration.GetValue("VerifiedID:CredentialType", "VerifiedEmployee");
            }
            bool allowRevoked = _configuration.GetValue("VerifiedID:allowRevoked", false);
            bool validateLinkedDomain = _configuration.GetValue("VerifiedID:validateLinkedDomain", true);
            return AddRequestedCredential(request, credentialType, acceptedIssuers, allowRevoked, validateLinkedDomain);
        }
        public PresentationRequest AddRequestedCredential(PresentationRequest request
                                    , string credentialType, List<string> acceptedIssuers
                                    , bool allowRevoked, bool validateLinkedDomain)
        {
            request.requestedCredentials.Add(new RequestedCredential()
            {
                type = credentialType,
                acceptedIssuers = null == acceptedIssuers ? new List<string>() : acceptedIssuers,
                configuration = new Configuration()
                {
                    validation = new Validation()
                    {
                        allowRevoked = allowRevoked,
                        validateLinkedDomain = validateLinkedDomain,
                        faceCheck = new FaceCheck()
                        {
                            sourcePhotoClaimName = _configuration.GetValue("VerifiedID:sourcePhotoClaimName", "photo"),
                            matchConfidenceThreshold = _configuration.GetValue("VerifiedID:matchConfidenceThreshold", 70)
                        }
                    }
                }
            });
            return request;
        }
        public PresentationRequest AddFaceCheck(PresentationRequest request)
        {
            string sourcePhotoClaimName = _configuration.GetValue("VerifiedID:PhotoClaimName", "photo");
            int matchConfidenceThreshold = _configuration.GetValue("VerifiedID:matchConfidenceThreshold", 70);
            return AddFaceCheck(request, request.requestedCredentials[0].type, sourcePhotoClaimName, matchConfidenceThreshold);
        }
        public PresentationRequest AddFaceCheck(PresentationRequest request, string credentialType, string sourcePhotoClaimName = "photo", int matchConfidenceThreshold = 70)
        {
            if (string.IsNullOrWhiteSpace(sourcePhotoClaimName))
            {
                sourcePhotoClaimName = _configuration.GetValue("VerifiedID:PhotoClaimName", "photo");
            }
            foreach (var requestedCredential in request.requestedCredentials)
            {
                if (null == credentialType || requestedCredential.type == credentialType)
                {
                    requestedCredential.configuration.validation.faceCheck = new FaceCheck() { sourcePhotoClaimName = sourcePhotoClaimName, matchConfidenceThreshold = matchConfidenceThreshold };
                    request.includeReceipt = false; // not supported while doing faceCheck
                }
            }
            return request;
        }

        public PresentationRequest AddClaimsConstrains(PresentationRequest request, string claimName, ConstraintType type, string value)
        {
            Constraint constraint = null;
            if (type == ConstraintType.Value)
            {
                constraint = new Constraint()
                {
                    claimName = claimName,
                    values = new List<string>() { value }
                };
            }
            if (type == ConstraintType.Contains)
            {
                constraint = new Constraint()
                {
                    claimName = claimName,
                    contains = value
                };
            }
            if (type == ConstraintType.StartsWith)
            {
                constraint = new Constraint()
                {
                    claimName = claimName,
                    startsWith = value
                };
            }
            if (null != constraint)
            {
                if (null == request.requestedCredentials[0].constraints)
                {
                    request.requestedCredentials[0].constraints = new List<Constraint>();
                }
                request.requestedCredentials[0].constraints.Add(constraint);
            }
            return request;
        }
        public HttpStatusCode HandleCallback( HttpRequest httpRequest, out CallbackEvent callback, out string errorMessage ) {
            callback = null;
            errorMessage = null;
            httpRequest.Headers.TryGetValue( "api-key", out var apiKey );
            if (this._apiKey != apiKey) {
                _log.LogTrace( "api-key wrong or missing: {apiKey}" );
                errorMessage = "api-key wrong or missing";
                return HttpStatusCode.Unauthorized;
            }
            string body = new System.IO.StreamReader( httpRequest.Body ).ReadToEndAsync().Result;
            _log.LogTrace( body );

            HttpStatusCode statusCode = HttpStatusCode.BadRequest;
            List<string> presentationStatus = new List<string>() { "request_retrieved", "presentation_verified", "presentation_error" };
            callback = JsonConvert.DeserializeObject<CallbackEvent>( body );

            if (presentationStatus.Contains( callback.requestStatus )) {
                if (!_cache.TryGetValue( callback.requestId, out string requestState )) {
                    errorMessage = $"Invalid requestId '{callback.requestId}'";
                } else {
                    JObject reqState = JObject.Parse( requestState );
                    reqState["status"] = callback.requestStatus;
                    if (reqState.ContainsKey( "callback" )) {
                        reqState["callback"] = body;
                    } else {
                        reqState.Add( "callback", body );
                    }
                    _cache.Set( callback.requestId, JsonConvert.SerializeObject( reqState )
                        , DateTimeOffset.Now.AddSeconds( _configuration.GetValue<int>( "AppSettings:CacheExpiresInSeconds", 300 ) ) );
                    statusCode = HttpStatusCode.OK;
                }
            } else {
                errorMessage = $"Unknown request status '{callback.requestStatus}'";
            }
            return statusCode;
        }
        public bool PollRequestStatus( string requestId, out JObject result ) {
            result = null;
            if (string.IsNullOrEmpty( requestId )) {
                result = JObject.FromObject( new { status = "error", message = "Missing argument 'requestId'" } );
                return false;
            }
            bool rc = true;
            if (_cache.TryGetValue( requestId, out string requestState )) {
                JObject reqState = JObject.Parse( requestState );
                string requestStatus = reqState["status"].ToString();
                CallbackEvent callback = null;
                switch (requestStatus) {
                    case "request_created":
                        result = JObject.FromObject( new { status = requestStatus, message = "Waiting to scan QR code" } );
                        break;
                    case "request_retrieved":
                        result = JObject.FromObject( new { status = requestStatus, message = "QR code is scanned. Waiting for user action..." } );
                        break;
                    case "presentation_error":
                        callback = JsonConvert.DeserializeObject<CallbackEvent>( reqState["callback"].ToString() );
                        result = JObject.FromObject( new { status = requestStatus, message = "Presentation failed:" + callback.error.message } );
                        break;
                    case "presentation_verified":
                        callback = JsonConvert.DeserializeObject<CallbackEvent>( reqState["callback"].ToString() );
                        JObject resp = JObject.Parse( JsonConvert.SerializeObject( new {
                            status = requestStatus,
                            message = "Presentation verified",
                            type = callback.verifiedCredentialsData[0].type,
                            claims = callback.verifiedCredentialsData[0].claims,
                            subject = callback.subject,
                            payload = callback.verifiedCredentialsData,
                        }, Newtonsoft.Json.Formatting.None, new JsonSerializerSettings {
                            NullValueHandling = NullValueHandling.Ignore
                        } ) );
                        result = resp;
                        break;
                    case "selfie_taken":
                        callback = JsonConvert.DeserializeObject<CallbackEvent>( reqState["callback"].ToString() );
                        result = JObject.FromObject( new { status = requestStatus, message = "Selfie taken", photo = callback.photo } );
                        break;
                    default:
                        result = JObject.FromObject( new { status = "error", message = $"Invalid requestStatus '{requestStatus}'" } );
                        rc = false;
                        break;
                }
            } else {
                result = JObject.FromObject( new { status = "request_not_created", message = "No data" } );
                rc = false;
            }
            return rc;
        }

    } // cls
} // ns
