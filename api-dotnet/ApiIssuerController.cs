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

namespace client_api_test_service_dotnet
{
    [Route("api/issuer/[action]")]
    [ApiController]
    public class ApiIssuerController : ApiBaseVCController
    {
        private const string IssuanceRequestConfigFile = "%cd%\\requests\\issuance_request_config_v2.json";

        public ApiIssuerController(IConfiguration configuration, IOptions<AppSettingsModel> appSettings, IMemoryCache memoryCache, IWebHostEnvironment env, ILogger<ApiIssuerController> log) : base(configuration, appSettings, memoryCache, env, log)
        {
            GetIssuanceRequest();
        }

        protected string GetApiPath() {
            return string.Format("{0}/api/issuer", GetRequestHostName() );
        }

        protected JObject GetIssuanceRequest() {
            string json = null;
            if ( GetCachedValue("issuanceRequest", out json)) {
                return JObject.Parse(json);
            }
            // see if file path was passed on command line
            string issuanceRequestFile = _configuration.GetValue<string>("IssuanceRequestConfigFile");
            if (string.IsNullOrEmpty(issuanceRequestFile)) {
                issuanceRequestFile = IssuanceRequestConfigFile;
            }
            issuanceRequestFile = issuanceRequestFile.Replace("%cd%", System.IO.Directory.GetCurrentDirectory() );
            if (!System.IO.File.Exists(issuanceRequestFile)) {
                _log.LogError("File not found: {0}", issuanceRequestFile);
                return null;
            }
            _log.LogTrace("IssuanceRequest file: {0}", issuanceRequestFile );
            json = System.IO.File.ReadAllText(issuanceRequestFile);
            JObject config = JObject.Parse(json);

            // download manifest and cache it
            string contents;
            HttpStatusCode statusCode = HttpStatusCode.OK;
            if (!HttpGet(config["issuance"]["manifest"].ToString(), out statusCode, out contents)) {
                _log.LogError("HttpStatus {0} fetching manifest {1}", statusCode, config["issuance"]["manifest"].ToString());
                return null;
            }
            CacheValueWithNoExpiery("manifestIssuance", contents);
            JObject manifest = JObject.Parse(contents);

            // update presentationRequest from manifest with things that don't change for each request
            if (!config["authority"].ToString().StartsWith("did:ion:")) {
                config["authority"] = manifest["input"]["issuer"];
            }
            config["registration"]["clientName"] = AppSettings.client_name;

            if (config["issuance"]["type"].ToString().Length == 0 ) {
                config["issuance"]["type"] = manifest["id"];
            }

            // if we have pin code but length is zero, remove it since VC Client API will give error then
            if ( ((JObject)config["issuance"]).ContainsKey("pin") ) {
                if ( int.TryParse( config["issuance"]["pin"]["length"].ToString(), out int pinLength ) ) {
                    if ( pinLength == 0 ) {
                        (config["issuance"] as JObject).Remove("pin");
                    }
                }
            }

            json = JsonConvert.SerializeObject(config);
            CacheValueWithNoExpiery("issuanceRequest", json);
            return config;
        }
        protected JObject GetIssuanceManifest() {
            if ( GetCachedValue("manifestIssuance", out string json)) {
                return JObject.Parse(json); ;
            }
            return null;
        }

        /// ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
        /// REST APIs
        /// ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

        [HttpGet]
        public async Task<ActionResult> echo() {
            TraceHttpRequest();
            try {
                JObject config = GetIssuanceRequest();
                JObject manifest = GetIssuanceManifest();
                var info = new
                {
                    date = DateTime.Now.ToString(),
                    host = GetRequestHostName(),
                    api = GetApiPath(),
                    didIssuer = manifest["input"]["issuer"], 
                    credentialType = manifest["id"], 
                    displayCard = manifest["display"]["card"],
                    buttonColor = "#000080",
                    contract = manifest["display"]["contract"],
                    selfAssertedClaims = config["issuance"]["claims"]
                };
                return ReturnJson(JsonConvert.SerializeObject(info));
            } catch (Exception ex) {
                return ReturnErrorMessage(ex.Message);
            }
        }

        [HttpGet]
        [Route("/api/issuer/logo.png")]
        public async Task<ActionResult> logo() {
            TraceHttpRequest();
            JObject manifest = GetIssuanceManifest();
            return Redirect(manifest["display"]["card"]["logo"]["uri"].ToString());
        }

        [HttpGet("/api/issuer/issue-request")]
        public async Task<ActionResult> issuanceReference() {
            TraceHttpRequest();
            try {
                JObject issuanceRequest = GetIssuanceRequest();
                if (issuanceRequest == null) {
                    return ReturnErrorMessage("Issuance Request Config File not found");
                }
                // set self-asserted claims passed as query string parameters
                if ( ((JObject)issuanceRequest["issuance"]).ContainsKey("claims") ) {
                    foreach (var c in ((JObject)issuanceRequest["issuance"]["claims"]) ) {
                        issuanceRequest["issuance"]["claims"][c.Key] = this.Request.Query[c.Key].ToString();
                    }
                }
                string correlationId = Guid.NewGuid().ToString();
                issuanceRequest["callback"]["url"] = string.Format("{0}/issuanceCallback", GetApiPath());
                issuanceRequest["callback"]["state"] = correlationId;
                issuanceRequest["callback"]["nounce"] = Guid.NewGuid().ToString();
                issuanceRequest["callback"]["headers"]["my-api-key"] = this.AppSettings.ApiKey;
                string pin = null;

                int pinLength = 0;
                if (((JObject)issuanceRequest["issuance"]).ContainsKey("pin")) {
                    int.TryParse(issuanceRequest["issuance"]["pin"]["length"].ToString(), out pinLength);
                    if ( pinLength > 0 ) {
                        int pinMaxValue = int.Parse("".PadRight( pinLength, '9'));          // 9999999
                        int randomNumber = RandomNumberGenerator.GetInt32(1, pinMaxValue);
                        pin = string.Format("{0:D" + pinLength.ToString() + "}", randomNumber);
                        _log.LogTrace("pin={0}", pin);
                        issuanceRequest["issuance"]["pin"]["value"] = pin;
                    }
                }

                string jsonString = JsonConvert.SerializeObject(issuanceRequest);
                _log.LogTrace("VC Client API Request\n{0}", jsonString);
                string contents = "";
                HttpStatusCode statusCode = HttpStatusCode.OK;
                if ( !HttpPost(jsonString, out statusCode, out contents) ) {
                    _log.LogError("VC Client API Error Response\n{0}", contents);
                    return ReturnErrorMessage( contents );
                }
                JObject requestConfig = JObject.Parse(contents);
                if ( pinLength > 0) {
                    requestConfig["pin"] = pin;
                }
                requestConfig.Add(new JProperty("id", correlationId));
                jsonString = JsonConvert.SerializeObject(requestConfig);
                _log.LogTrace("VC Client API Response\n{0}", jsonString);
                return ReturnJson( jsonString );
            }  catch (Exception ex)  {
                return ReturnErrorMessage( ex.Message );
            }
        }

        [HttpPost]
        public async Task<ActionResult> issuanceCallback() {
            TraceHttpRequest();
            try {
                _log.LogTrace("issuanceCallback");
                string body = GetRequestBody();
                _log.LogTrace(body);
                JObject issuanceResponse = JObject.Parse(body);
                string correlationId = issuanceResponse["state"].ToString();
                string code = issuanceResponse["code"].ToString();
                if (code == "request_retrieved") {
                    CacheJsonObjectWithExpiery( correlationId, new { status = 1,message = "QR Code is scanned. Waiting for issuance to complete." });
                }
                if (code == "issuance_succesful") {
                    CacheJsonObjectWithExpiery( correlationId, new { status = 2, message = "Issuance process is completed." });
                }
                if (code == "issuance_failed") {
                    CacheJsonObjectWithExpiery( correlationId, new { status = 99, message = "Issuance process failed with reason: " + issuanceResponse["details"].ToString() });
                }
                return new OkResult();
            } catch (Exception ex) {
                return ReturnErrorMessage(ex.Message);
            }
        }

        [HttpPost]
        public async Task<ActionResult> response() {
            TraceHttpRequest();
            try {
                _log.LogTrace("response");
                string body = GetRequestBody();
                JObject claims = JObject.Parse(body);
                CacheJsonObjectWithExpiery(claims["state"].ToString(), claims);
                return new OkResult();
            } catch( Exception ex ) {
                return ReturnErrorMessage( ex.Message );
            }
        }

        [HttpGet("/api/issuer/issue-response")]
        public async Task<ActionResult> issuanceResponse() {
            TraceHttpRequest();
            try {
                string correlationId = this.Request.Query["id"];
                if (string.IsNullOrEmpty(correlationId)) {
                    return ReturnErrorMessage("Missing argument 'id'");
                }
                string body = null;
                if ( GetCachedValue(correlationId, out body)) {
                    RemoveCacheValue(correlationId);
                    return ReturnJson(body);
                }
                return new OkResult();
            } catch (Exception ex) {
                return ReturnErrorMessage(ex.Message);
            }
        }

    } // cls
} // ns
