using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using client_api_test_service_dotnet.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;
using Microsoft.Identity.Client;

namespace client_api_test_service_dotnet
{
    public class ApiBaseVCController : ControllerBase
    {
        protected IMemoryCache _cache;
        protected readonly IWebHostEnvironment _env;
        protected readonly ILogger<ApiBaseVCController> _log;
        protected readonly AppSettingsModel AppSettings;
        protected readonly IConfiguration _configuration;
        private string _apiEndpoint;
        private string _authority;

        public ApiBaseVCController(IConfiguration configuration, IOptions<AppSettingsModel> appSettings, IMemoryCache memoryCache, IWebHostEnvironment env, ILogger<ApiBaseVCController> log)
        {
            this.AppSettings = appSettings.Value;
            _cache = memoryCache;
            _env = env;
            _log = log;
            _configuration = configuration;

            _apiEndpoint = string.Format(this.AppSettings.ApiEndpoint, this.AppSettings.TenantId);
            _authority = string.Format(this.AppSettings.Authority, this.AppSettings.TenantId);

        }

        /// ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
        /// Helpers
        /// ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

        protected string GetRequestHostName() {
            string scheme = "https";// : this.Request.Scheme;
            string originalHost = this.Request.Headers["x-original-host"];
            string hostname = "";
            if (!string.IsNullOrEmpty(originalHost))
                 hostname = string.Format("{0}://{1}", scheme, originalHost);
            else hostname = string.Format("{0}://{1}", scheme, this.Request.Host);
            return hostname;
        }
        // return 400 error-message
        protected ActionResult ReturnErrorMessage(string errorMessage) {
            return BadRequest(new { error = "400", error_description = errorMessage });
        }
        // return 200 json 
        protected ActionResult ReturnJson( string json ) {
            return new ContentResult { ContentType = "application/json", Content = json };
        }
        protected ActionResult ReturnErrorB2C(string message) {
            var msg = new {
                version = "1.0.0", status = 400, userMessage = message
            };
            return new ContentResult { StatusCode = 409, ContentType = "application/json", Content = JsonConvert.SerializeObject(msg) };
        }
        protected async Task<(string, string)> GetAccessToken() {
            IConfidentialClientApplication app = ConfidentialClientApplicationBuilder.Create( this.AppSettings.ClientId )
                                                        .WithClientSecret( this.AppSettings.ClientSecret )
                                                        .WithAuthority( new Uri( _authority ) )
                                                        .Build();
            string[] scopes = new string[] { this.AppSettings.scope };
            AuthenticationResult result = null;
            try {
                result = await app.AcquireTokenForClient( scopes ).ExecuteAsync();
            } catch ( Exception ex) {
                return (String.Empty, ex.Message);
            }
            _log.LogTrace( result.AccessToken );
            return (result.AccessToken, String.Empty);
        }
        // POST to VC Client API
        protected bool HttpPost(string body, out HttpStatusCode statusCode, out string response) {
            response = null;            
            var accessToken = GetAccessToken( ).Result;            
            if (accessToken.Item1 == String.Empty ) {
                statusCode = HttpStatusCode.Unauthorized;
                response = accessToken.Item2;
                return false;
            }
            HttpClient client = new HttpClient();
            client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken.Item1 );
            HttpResponseMessage res = client.PostAsync( _apiEndpoint, new StringContent(body, Encoding.UTF8, "application/json") ).Result;
            response = res.Content.ReadAsStringAsync().Result;
            client.Dispose();
            statusCode = res.StatusCode;
            return res.IsSuccessStatusCode;
        }
        protected bool HttpGet(string url, out HttpStatusCode statusCode, out string response) {
            response = null;
            HttpClient client = new HttpClient();
            HttpResponseMessage res = client.GetAsync( url ).Result;
            response = res.Content.ReadAsStringAsync().Result;
            client.Dispose();
            statusCode = res.StatusCode;
            return res.IsSuccessStatusCode;
        }

        protected void TraceHttpRequest() {
            string ipaddr = "";
            string xForwardedFor = this.Request.Headers["X-Forwarded-For"];
            if (!string.IsNullOrEmpty(xForwardedFor))
                 ipaddr = xForwardedFor;
            else ipaddr = HttpContext.Connection.RemoteIpAddress.ToString();
            _log.LogTrace("{0} {1} -> {2} {3}://{4}{5}{6}", DateTime.UtcNow.ToString("o"), ipaddr
                    , this.Request.Method, this.Request.Scheme, this.Request.Host, this.Request.Path, this.Request.QueryString );
        }
        protected string GetRequestBody() {
            return new System.IO.StreamReader(this.Request.Body).ReadToEndAsync().Result;
        }

        protected JObject JWTTokenToJObject( string token ) {
            string[] parts = token.Split(".");
            parts[1] = parts[1].PadRight(4 * ((parts[1].Length + 3) / 4), '=');
            return JObject.Parse(Encoding.UTF8.GetString(Convert.FromBase64String(parts[1])));
        }

        protected bool GetCachedValue(string key, out string value) {
            return _cache.TryGetValue(key, out value);
        }
        protected bool GetCachedJsonObject(string key, out JObject value) {
            value = null;
            if ( !_cache.TryGetValue(key, out string buf) ) {
                return false;
            } else {
                value = JObject.Parse(buf);
                return true;
            }
        }
        protected void CacheJsonObjectWithExpiery( string key, object jsonObject ) {
            _cache.Set( key, JsonConvert.SerializeObject(jsonObject), DateTimeOffset.Now.AddSeconds(this.AppSettings.CacheExpiresInSeconds));
        }
        protected void CacheValueWithExpiery(string key, string value) {
            _cache.Set(key, value, DateTimeOffset.Now.AddSeconds(this.AppSettings.CacheExpiresInSeconds));
        }
        protected void CacheValueWithNoExpiery(string key, string value) {
            _cache.Set(key, value );
        }
        protected void RemoveCacheValue( string key ) {
            _cache.Remove(key);
        }
    } // cls
} // ns
