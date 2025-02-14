using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.IdentityModel.Tokens;
using Newtonsoft.Json;
using System.Security.Cryptography.X509Certificates;
using System.Security.Cryptography;
using System.Text;
using VerifiedIDEAM.Models;
using Newtonsoft.Json.Linq;
using VerifiedIDEAM.Helpers;
using System.IdentityModel.Tokens.Jwt;
using System.Reflection.Metadata;
using Azure.Core;
using System.Reflection;
using System.Runtime.ConstrainedExecution;
using Microsoft.AspNetCore.DataProtection.KeyManagement;
using System.Net.Http.Headers;
using System.Net.Http;
using System.Net;
using Microsoft.Extensions.Configuration;
using Microsoft.Identity.Abstractions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http.Extensions;
using Azure.Security.KeyVault.Keys;
using Azure.Security.KeyVault.Certificates;

namespace VerifiedIDEAM.Controllers
{
    public class OidcController : Controller {
        private IMemoryCache _cache;
        private readonly IWebHostEnvironment _env;
        private ILogger<OidcController> _log;
        private IConfiguration _configuration;
        private IHttpClientFactory _httpClientFactory;

        public OidcController( IConfiguration configuration, ILogger<OidcController> log, IMemoryCache memoryCache, IWebHostEnvironment env, IHttpClientFactory httpClientFactory ) {
            _configuration = configuration;
            _log = log;
            _cache = memoryCache;
            _env = env;
            _httpClientFactory = httpClientFactory;
        }
        /////////////////////////////////////////////////////////////////////////////////////////////////////
        // Helpers
        /////////////////////////////////////////////////////////////////////////////////////////////////////
        protected string GetClientIpAddr() {
            string ipaddr = "";
            string xForwardedFor = this.Request.Headers["X-Forwarded-For"];
            if (!string.IsNullOrEmpty( xForwardedFor ))
                ipaddr = xForwardedFor;
            else ipaddr = HttpContext.Connection.RemoteIpAddress.ToString();
            return ipaddr;
        }
        protected void TraceHttpRequest() {
            string ipaddr = GetClientIpAddr();
            StringBuilder sb = new StringBuilder();
            foreach (var header in this.Request.Headers) {
                sb.AppendFormat( "      {0}: {1}\n", header.Key, header.Value );
            }
            _log.LogTrace( "{0} {1}\n      {2} {3}://{4}{5}{6}\n{7}", DateTime.UtcNow.ToString( "o" ), ipaddr
                    , this.Request.Method, this.Request.Scheme, this.Request.Host, this.Request.Path, this.Request.QueryString, sb.ToString() );
        }

        public string GetRemoteHostName() {
            string originalHost = this.Request.Headers["x-Forwarded-host"];
            string originalProto = this.Request.Headers["x-Forwarded-proto"];
            if (string.IsNullOrWhiteSpace( originalProto )) {
                originalProto = "https";
            }
            if (string.IsNullOrWhiteSpace( originalHost )) {
                originalHost = this.Request.Host.ToString();
            }
            return $"{originalProto}://{originalHost}";
        }

        private ContentResult ReturnErrorMessage( string error_code, string error_message ) {
            _log.LogTrace( $"Error: {error_code}: {error_message}" );
            return new ContentResult { ContentType = "application/json", StatusCode = 400
                , Content = JsonConvert.SerializeObject( new { error = "400", error_description = $"{error_code}: {error_message}" } ) };
        }

        private string GetIssuerEndpoint( string tenantId ) {
            return $"{GetRemoteHostName()}/{tenantId}/v2.0/";
        }
        private string GenerateIdToken( AuthRequest authReq ) {
            if ( !authReq.authOK ) {
                return null;
            }
            if (!_cache.TryGetValue( "x509cert", out X509Certificate2 x509cert )) {
                return null;
            }

            DateTime IssuedAt = DateTime.UtcNow;
            DateTime unixEpoch = new DateTime( 1970, 1, 1 );
            Int32 IssuedAtUnix = (Int32)(IssuedAt.Subtract( unixEpoch )).TotalSeconds;
            Int32 vcExpiry = (Int32)(authReq.vcExpirationDate.Subtract( unixEpoch)).TotalSeconds;

            // for Azure AppServices, the Plan must atleast be Basic and you must add a app config key=WEBSITE_LOAD_CERTIFICATES value=...anything...
            X509SigningCredentials x509SigningCredentials = new X509SigningCredentials( x509cert );
            int IdTokenLifetime = int.Parse( _configuration["OIDC:IdTokenLifetime"] );
            var payload = new JwtPayload {
                { "iss", GetIssuerEndpoint( authReq.tenantId ) },
                { "iat", IssuedAtUnix },
                { "nbf", IssuedAtUnix },
                { "exp", IssuedAtUnix + IdTokenLifetime },
                { "aud", authReq.client_id },
                { "nonce", authReq.nonce },
                { "sub", authReq.idtSub },
                { "preferred_username", authReq.preferred_username },
                { "matchConfidenceScore", authReq.matchConfidenceScore },
                { "vc_exp", vcExpiry },
                { "acr", "possessionorinherence" },
                { "amr", new[] {"face" } }
            };
            var idToken = new JwtSecurityToken( new JwtHeader( x509SigningCredentials ), payload );
            var tokenHandler = new JwtSecurityTokenHandler();
            var token = tokenHandler.WriteToken( idToken );
            return token;
        }
        private Microsoft.IdentityModel.Tokens.JsonWebKey GetJsonWebKeyFromX509Certificate( X509Certificate2 cert ) {
            X509SecurityKey x509Key = new X509SecurityKey( cert );
            if (x509Key.PublicKey is RSA rsa) {
                Microsoft.IdentityModel.Tokens.JsonWebKey rsaJsonWebKey = JsonWebKeyConverter.ConvertFromX509SecurityKey( x509Key );
                var thumbprint = VerifiedIDEam.Helpers.Base64Url.Encode( x509Key.Certificate.GetCertHash() );
                var parameters = rsa.ExportParameters( false );
                var exponent = VerifiedIDEam.Helpers.Base64Url.Encode( parameters.Exponent );
                var modulus = VerifiedIDEam.Helpers.Base64Url.Encode( parameters.Modulus );
                rsaJsonWebKey.N = modulus;
                rsaJsonWebKey.E = exponent;
                rsaJsonWebKey.X5t = thumbprint;
                return rsaJsonWebKey;
            }
            return null;
        }
        private Microsoft.IdentityModel.Tokens.JsonWebKey GetJsonWebKeyFromKeyVault( ) {
            X509Certificate2 x509cert = null;
            if (!_cache.TryGetValue( "x509cert", out x509cert )) {
                x509cert = KeyVaultHelper.GetKeyVaultX509Certificate( _configuration, out string certName, out string certVersion );
                _cache.Set( "x509cert", x509cert, TimeSpan.FromHours( 12 ) );
            }
            return GetJsonWebKeyFromX509Certificate( x509cert );
        }

        protected bool IsMobile() {
            string userAgent = this.Request.Headers["User-Agent"];
            return (userAgent.Contains( "Android" ) || userAgent.Contains( "iPhone" ));
        }

        private bool AcquireAccessToken( out string accessToken, out string error, out string errorDescription ) {
            accessToken = null;
            error = null;
            errorDescription = null;
            var at = MsalAccessTokenHandler.GetAccessToken( _configuration ).Result;
            if (at.Item1 == String.Empty) {
                error = at.error;
                errorDescription = at.error_description;
                _log.LogError( String.Format( "failed to acquire accesstoken: {0} : {1}", at.error, at.error_description ) );
                return false;
            }
            accessToken = at.token;
            _log.LogTrace( accessToken );
            return true;
        }

        private string GetRequestParameter( HttpRequest request, string parameterName ) {
            if ("GET" == this.Request.Method ) {
                return this.Request.Query[parameterName];
            }
            if ("POST" == this.Request.Method ) {
                return this.Request.Form[parameterName];
            }
            return null;
        }
        /////////////////////////////////////////////////////////////////////////////////////////////////////
        // API Endpoints
        /////////////////////////////////////////////////////////////////////////////////////////////////////
        [HttpGet( "/{tenantId}/v2.0/.well-known/openid-configuration" )]
        [Produces( "application/json" )]
        public async Task<ActionResult> WellKnownOpenidConfiguration( string tenantId ) {
            TraceHttpRequest();
            string cfgKey = $"{tenantId}/v2.0/.well-known/openid-configuration";
            if (_cache.TryGetValue( cfgKey, out var config )) {
                return Ok( config );
            }
            string urlStart = $"{GetRemoteHostName()}/{tenantId}";
            var respContent = new {
                issuer = GetIssuerEndpoint( tenantId ),
                authorization_endpoint = $"{urlStart}/oauth2/v2.0/authorize",
                jwks_uri = $"{urlStart}/discovery/v2.0/keys",
                response_modes_supported = new[] { "form_post" },
                response_types_supported = new[] { "id_token" },
                scopes_supported = new[] {"openid" },
                subject_types_supported = new[] { "public" },
                id_token_signing_alg_values_supported = new[] { "RS256" },
                grant_types_supported = new[] { "implicit" },
                claims_supported = "email;acr;amr;sub;iss;aud;exp;iat;matchConfidenceScore;vc_exp".Split( ";" ) 
            };
            _cache.Set( cfgKey, respContent, TimeSpan.FromMinutes( 60 ) );
            //return Ok(respContent);
            string resp = JsonConvert.SerializeObject( respContent );
            _log.LogTrace( resp );
            return new ContentResult { ContentType = "application/json", Content = resp };
        }

        [HttpGet( "{tenantid}/discovery/v2.0/keys" )]
        [Produces( "application/json" )]
        public async Task<ActionResult> DiscoveryKeys( string tenantId ) {
            // https://tools.ietf.org/html/draft-ietf-jose-json-web-key-41
            // https://developer.byu.edu/docs/consume-api/use-api/implement-openid-connect/jwks-public-key-documentation
            TraceHttpRequest();
            string cfgKey = ".well-known/openid-configuration/keys";
            if (_cache.TryGetValue( cfgKey, out var config )) {
                return Ok( config );
            }
            var jwkKV = GetJsonWebKeyFromKeyVault();
            var rsaJsonWebKey = new {
                keys = new[]
                {
                    new   {
                        kty = jwkKV.Kty,
                        use = "sig",
                        kid = jwkKV.KeyId,
                        x5t = jwkKV.X5t,
                        e = jwkKV.E,
                        n = jwkKV.N,
                        x5c = new[] { jwkKV.X5c[0] }
                    }
                }
            };
            _cache.Set( cfgKey, rsaJsonWebKey, TimeSpan.FromMinutes( 60 ) ); // cache it for 60 min so we don't need to do this every time
            string resp = JsonConvert.SerializeObject( rsaJsonWebKey, Newtonsoft.Json.Formatting.None, new JsonSerializerSettings {
                                                    NullValueHandling = NullValueHandling.Ignore
                                                    } );
            _log.LogTrace( resp );
            return new ContentResult { ContentType = "application/json", Content = resp };
        }

        [HttpGet( "{tenantid}/oauth2/v2.0/authorize" )] // GET just for dev/test locally
        [HttpPost( "{tenantid}/oauth2/v2.0/authorize" )]
        public async Task<ActionResult> GetAuthorize( string tenantId ) {            
            TraceHttpRequest();

            AuthRequest authReq = new AuthRequest();
            authReq.txid = Guid.NewGuid().ToString();
            authReq.tenantId = tenantId;
            authReq.endpoint = GetRemoteHostName();

            bool localTest = false;
            // only support test in dev mode for ease of testing locally
            if ("GET" == this.Request.Method && !_env.IsDevelopment()) {
                return ReturnErrorMessage( "IDP011", $"Method GET not supported" );
            }
            if ("POST" == this.Request.Method) {
                if ( !this.Request.HasFormContentType ) {
                    return ReturnErrorMessage( "IDP012", $"unsupported Content-Type for POST method '{this.Request.ContentType}'" );
                }
                string claims = this.Request.Form["claims"];
            }

            // only for dev/debug so you can bypass actual tenant login before testing this
            if ( localTest ) {
                authReq.preferred_username = "andreasv@fawltytowers2.com";
                authReq.name = "Andreas Voldemar";
                authReq.client_id = "did:web:dev.fawltytowers2.com";
                authReq.nonce = "nonce";
                authReq.idtSub = "sub";
                authReq.response_type = "id_token";
                authReq.scope = "openid";
                authReq.redirect_uri = "https://login.microsoftonline.com/common/federation/externalauthprovider";
                authReq.state = Guid.NewGuid().ToString();
                authReq.response_mode = "form_post";
                authReq.nonce = Guid.NewGuid().ToString();
                authReq.clientRequestId = Guid.NewGuid().ToString();
            } else {
                authReq.response_type = GetRequestParameter( this.Request, "response_type");
                authReq.scope =  GetRequestParameter( this.Request, "scope");
                authReq.client_id =  GetRequestParameter( this.Request, "client_id");
                authReq.redirect_uri =  GetRequestParameter( this.Request, "redirect_uri");
                authReq.state =  GetRequestParameter( this.Request, "state");
                authReq.response_mode =  GetRequestParameter( this.Request, "response_mode");
                authReq.nonce =  GetRequestParameter( this.Request, "nonce");
                authReq.clientRequestId =  GetRequestParameter( this.Request, "client-request-id");
                authReq.id_token_hint =  GetRequestParameter( this.Request, "id_token_hint");
                if (string.IsNullOrWhiteSpace( authReq.response_mode )) {
                    authReq.response_mode = "form_post";
                }
            }

            if (authReq.response_type != "id_token") {
                return ReturnErrorMessage( "IDP007", $"unsupported response_type '{authReq.response_type}'" );
            }
            if (authReq.response_mode != "form_post") {
                return ReturnErrorMessage( "IDP008", $"unsupported response_mode '{authReq.response_mode}'" );
            }
            string redirectUri = _configuration["OIDC:redirect_uri"];
            if (redirectUri != authReq.redirect_uri) {
                return ReturnErrorMessage( "IDP006", $"invalid redirect_uri '{authReq.redirect_uri}'" );
            }
            if (string.IsNullOrWhiteSpace( authReq.id_token_hint )) {
                return ReturnErrorMessage( "IDP016", $"Missing id_token_hint" );
            }
            // don't validate JWT token signature if you're just doing dev/debug
            if ( !localTest ) {
                _log.LogTrace( authReq.id_token_hint );
                IdTokenHintValidator idtValidator = new IdTokenHintValidator( authReq.id_token_hint );
                if (!idtValidator.ValidateJwtToken( null, out string errmsg )) {
                    _log.LogInformation( $"JWT token validation error: {errmsg}" );
                    return ReturnErrorMessage( "IDP019", $"Invalid id_token_hint" );
                }
                if (!idtValidator.ContainsClaim( "tid" )) {
                    return ReturnErrorMessage( "IDP017", $"Missing claim 'tid'" );
                }
                string tid = idtValidator.GetClaim("tid");
                if (tid != tenantId) {
                    return ReturnErrorMessage( "IDP015", $"id_token not signed by tenant id {tenantId}" );
                }
                authReq.preferred_username = idtValidator.GetClaim( "preferred_username" );
                // try email if preferred_username isn't passed for some reason
                if (string.IsNullOrWhiteSpace(authReq.preferred_username)) {
                    authReq.preferred_username = idtValidator.GetClaim( "email" );
                }
                if (string.IsNullOrWhiteSpace( authReq.preferred_username )) {
                    return ReturnErrorMessage( "IDP017", $"Missing claim 'preferred_username'" );
                }
                authReq.name = idtValidator.GetClaim("name");
                authReq.idtSub = idtValidator.GetClaim("sub");
            }

            string ar = JsonConvert.SerializeObject( authReq );
            _log.LogTrace(ar);
            _cache.Set( authReq.txid, ar, TimeSpan.FromMinutes( 5 ) );

            // check if user is member or guest user (Q&D hack)
            authReq.guestAccount = true;
            string key = $"domains_{tenantId}";
            if (!_cache.TryGetValue( key, out List<string> domains )) {
                domains = IdTokenHintValidator.LoadAllowedAudiences( tenantId );
                _cache.Set( key, domains, TimeSpan.FromHours( 12 ) );
            }
            foreach ( var domain in domains ) {
                if ( authReq.preferred_username.EndsWith( $"@{domain}") ) {
                    authReq.guestAccount = false;
                    break;
                }
            }
            // call Client API and generate a presentation request
            RequestServiceClient requestServiceClient = new RequestServiceClient( _configuration, _log, _httpClientFactory, _cache, GetRemoteHostName() );
            List<string> acceptedIssuers = new List<string>();
            // if it is a guest account, accept all issuers, else, limit to our authority (passed in client_id)
            if ( !authReq.guestAccount ) {
                acceptedIssuers.Add( authReq.client_id );
            }
            PresentationRequest request = requestServiceClient.CreatePresentationRequest( authReq.txid, "", acceptedIssuers );
            requestServiceClient.AddFaceCheck( request );
            string claimName = _configuration.GetValue( "VerifiedID:ClaimName", "revocationId" );
            requestServiceClient.AddClaimsConstrains( request, claimName, RequestServiceClient.ConstraintType.Value, authReq.preferred_username );

            var accessToken = await MsalAccessTokenHandler.GetAccessToken( _configuration );
            if (accessToken.Item1 == String.Empty) {
                _log.LogError( String.Format( "failed to acquire accesstoken: {0} : {1}" ), accessToken.error, accessToken.error_description );
                return BadRequest( new { error = accessToken.error, error_description = accessToken.error_description } );
            }
            HttpResponseMessage httpResp = requestServiceClient.CallRequestServiceAPI( request, accessToken.token );
            string response = await httpResp.Content.ReadAsStringAsync();
            HttpStatusCode statusCode = httpResp.StatusCode;
            if (statusCode != HttpStatusCode.Created) {
                _log.LogError( "Error calling Verified ID API: " + response );
                return BadRequest( new { error = "400", error_description = "Verified ID API error response: " + response } );
            }
            PresentationRequestResponse presentationRequestResponse = requestServiceClient.ProcessRequestServiceAPIResponse( request, response );

            //
            ViewData["username"] = authReq.preferred_username;
            ViewData["name"] = authReq.name;
            ViewData["apiValidate"] = $"{authReq.endpoint}/api/ValidateCredentials/{authReq.txid}";
            ViewData["apiNext"] = $"{authReq.endpoint}/api/next/{authReq.txid}";
            ViewData["apiPoll"] = $"{authReq.endpoint}/api/request-status/{presentationRequestResponse.requestId}";
            ViewData["requestId"] = presentationRequestResponse.requestId;
            ViewData["requestUrl"] = presentationRequestResponse.url;
            ViewData["requestExpiry"] = presentationRequestResponse.expiry;
            ViewData["txid"] = authReq.txid;

            this.Response.Headers.Add( "Access-Control-Allow-Credentials", "true" );
            return View( "Authorize" );
        }

        [HttpPost( "api/ValidateCredentials/{txid}" )]
        public async Task<ActionResult> ValidateCredentials( string txid ) {
            TraceHttpRequest();

            if (!_cache.TryGetValue( txid, out string json )) {
                return BadRequest( new { error = "IDP001", error_description = "invalid request" } );
            }
            AuthRequest authReq = JsonConvert.DeserializeObject<AuthRequest>( json );
            bool ok = authReq.authOK; // status of Verified ID presentation
            _cache.Set( txid, JsonConvert.SerializeObject( authReq ) );
            if (!ok) {
                return ReturnErrorMessage( "IDP001", "invalid username or password" );
            } else {
                return Ok();
            }
        }

        [HttpGet( "api/next/{txid}" )]
        public async Task<ActionResult> Next(string txid) {
            TraceHttpRequest();
            if (!_cache.TryGetValue( txid, out string json )) {
                return BadRequest( new { error = "IDP001", error_description = "invalid request" } );
            }
            AuthRequest authReq = JsonConvert.DeserializeObject<AuthRequest>( json );
            if (!authReq.authOK) {
                return ReturnErrorMessage( "IDP002", "not authorized" );
            }

            string idToken = GenerateIdToken( authReq );
            _log.LogTrace( idToken );
            string path = System.IO.Path.Combine( _env.ContentRootPath, "wwwroot", "response_mode-form_post.html" );
            string htmlResponse = System.IO.File.ReadAllText( path );
            htmlResponse = htmlResponse.Replace( "{{redirectUrl}}", authReq.redirect_uri );
            htmlResponse = htmlResponse.Replace( "{{state}}", authReq.state );
            htmlResponse = htmlResponse.Replace( "{{id_token}}", idToken );
            _log.LogTrace( htmlResponse );
            //_cache.Set( txid, JsonConvert.SerializeObject( authReq ), TimeSpan.FromMinutes( 5 ) ); // update cache
            this.Response.Headers.Add( "Access-Control-Allow-Credentials", "true" );
            return new ContentResult { ContentType = "text/html", Content = htmlResponse };
        }

        [HttpGet( "api/test/{tenantId}" )]
        public async Task<ActionResult> Test( string tenantId ) {
            TraceHttpRequest();

            if (!_env.IsDevelopment()) {
                return ReturnErrorMessage( "IDP011", $"Method only supported in development mode" );
            }

            AuthRequest authReq = new AuthRequest();
            authReq.tenantId = tenantId;
            authReq.preferred_username = "testuser@somewhere.com";
            authReq.client_id = "client_id";
            authReq.nonce = "nonce";
            authReq.idtSub ="sub";
            authReq.authOK = true;

            Microsoft.IdentityModel.Tokens.JsonWebKey jwk = GetJsonWebKeyFromKeyVault();
            string idToken = GenerateIdToken( authReq );

            bool ok = false;
            var tokenHandler = new JwtSecurityTokenHandler();
            try {
                tokenHandler.ValidateToken( idToken, new TokenValidationParameters {
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = jwk,
                    ValidateIssuer = true,
                    ValidIssuer = GetIssuerEndpoint( tenantId ),
                    ValidateAudience = false,
                    ValidateLifetime = false, // we will get an near expired or an expired id_token from Entra
                    ClockSkew = TimeSpan.Zero // tokens expires on the exact second
                }, out SecurityToken validatedToken );
                //var jwtToken = (JwtSecurityToken)validatedToken;
                ok = true;
            } catch (Exception ex) {
                _log.LogTrace( ex.Message );
            }

            var rsaJsonWebKey = new {
                keys = new[]
                {
                    new   {
                        kty = jwk.Kty,
                        use = "sig",
                        kid = jwk.KeyId,
                        x5t = jwk.X5t,
                        e = jwk.E,
                        n = jwk.N,
                        x5c = new[] { VerifiedIDEam.Helpers.Base64Url.UrlEncode( jwk.X5c[0] ) }
                    }
                }
            };
            string resp = JsonConvert.SerializeObject( rsaJsonWebKey, Newtonsoft.Json.Formatting.None, new JsonSerializerSettings {
                NullValueHandling = NullValueHandling.Ignore
            } );
            JObject jwksConfig = JObject.Parse( resp );
            foreach (var key in jwksConfig["keys"]) {
                jwk = JsonConvert.DeserializeObject<Microsoft.IdentityModel.Tokens.JsonWebKey>( JsonConvert.SerializeObject( key ) );
            }
            try {
                tokenHandler.ValidateToken( idToken, new TokenValidationParameters {
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = jwk,
                    ValidateIssuer = true,
                    ValidIssuer = GetIssuerEndpoint( tenantId ),
                    ValidateAudience = false,
                    ValidateLifetime = false, // we will get an near expired or an expired id_token from Entra
                    ClockSkew = TimeSpan.Zero // tokens expires on the exact second
                }, out SecurityToken validatedToken );
                //var jwtToken = (JwtSecurityToken)validatedToken;
                ok = true;
            } catch (Exception ex) {
                _log.LogTrace( ex.Message );
            }
            var respmsg = new { rc = ok, id_token = idToken, keys = rsaJsonWebKey };
            resp = JsonConvert.SerializeObject( respmsg, Newtonsoft.Json.Formatting.None, new JsonSerializerSettings {
                NullValueHandling = NullValueHandling.Ignore
            } );

            return new ContentResult { ContentType = "application/json", Content = resp };
        }

        /////////////////////////////////////////////////////////////////////////////////////////////////////
        // Verified ID API Endpoints
        /////////////////////////////////////////////////////////////////////////////////////////////////////
        [AllowAnonymous]
        [HttpPost( "/api/verifier/presentationcallback" )]
        public async Task<ActionResult> PresentationCallback() {
            TraceHttpRequest();
            try {
                RequestServiceClient requestServiceClient = new RequestServiceClient( _configuration, _log, _httpClientFactory, _cache, GetRemoteHostName() );
                HttpStatusCode statusCode = requestServiceClient.HandleCallback( this.Request, out CallbackEvent callback, out string errorMessage );
                if (HttpStatusCode.Unauthorized == statusCode) {
                    return new ContentResult() { StatusCode = (int)HttpStatusCode.Unauthorized, Content = errorMessage };
                }
                if (HttpStatusCode.OK == statusCode) {
                    if ( callback.requestStatus == "presentation_verified" ) {
                        if (!_cache.TryGetValue( callback.state, out string json )) {
                            return BadRequest( new { error = "IDP001", error_description = "invalid request" } );
                        }
                        AuthRequest authReq = JsonConvert.DeserializeObject<AuthRequest>( json );

                        if ( callback.verifiedCredentialsData[0].claims.ContainsKey("revocationId") ) {
                            string revocationId = callback.verifiedCredentialsData[0].claims["revocationId"].ToString();
                            authReq.authOK = revocationId.ToLowerInvariant() == authReq.preferred_username.ToLowerInvariant();
                        }
                        authReq.matchConfidenceScore = callback.verifiedCredentialsData[0].faceCheck.matchConfidenceScore;
                        authReq.vcExpirationDate = DateTime.Parse(callback.verifiedCredentialsData[0].expirationDate);
                        _cache.Set( callback.state, JsonConvert.SerializeObject( authReq ) );
                    }
                    // add support for error 'low confidence score'
                    return new OkResult(); 
                }
                return BadRequest( new { error = "400", error_description = errorMessage } );
            } catch (Exception ex) {
                return BadRequest( new { error = "400", error_description = ex.Message } );
            }
        }
        [AllowAnonymous]
        [HttpGet( "/api/request-status/{requestId}" )]
        public ActionResult RequestStatus(string requestId ) {
            TraceHttpRequest();
            try {
                RequestServiceClient requestServiceClient = new RequestServiceClient( _configuration, _log, _httpClientFactory, _cache, GetRemoteHostName() );
                if (!requestServiceClient.PollRequestStatus( requestId, out JObject response )) {
                    return BadRequest( new { error = "400", error_description = JsonConvert.SerializeObject( response ) } );
                }
                return new ContentResult { ContentType = "application/json", Content = JsonConvert.SerializeObject( response ) };
            } catch (Exception ex) {
                return BadRequest( new { error = "400", error_description = ex.Message } );
            }
        }

    } // cls
} // ns
