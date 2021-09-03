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
using System.Collections.Generic;

namespace client_api_test_service_dotnet
{
    [Route("api/issuer/[action]")]
    [ApiController]
    public class ApiIssuerController : ApiBaseVCController
    {
        private const string IssuanceRequestConfigFile = "%cd%\\requests\\issuance_request_config_v2.json";

        public ApiIssuerController(IConfiguration configuration, IOptions<AppSettingsModel> appSettings, IMemoryCache memoryCache, IWebHostEnvironment env, ILogger<ApiIssuerController> log) : base(configuration, appSettings, memoryCache, env, log)
        {
            GetIssuanceManifest();
        }

        protected string GetApiPath() {
            return string.Format("{0}/api/issuer", GetRequestHostName() );
        }

        protected JObject GetIssuanceManifest() {
            if ( GetCachedValue("manifestIssuance", out string json)) {
                return JObject.Parse(json); ;
            }
            // download manifest and cache it
            string contents;
            HttpStatusCode statusCode = HttpStatusCode.OK;
            if (!HttpGet( this.AppSettings.DidManifest, out statusCode, out contents)) {
                _log.LogError("HttpStatus {0} fetching manifest {1}", statusCode, this.AppSettings.DidManifest );
                return null;
            }
            CacheValueWithNoExpiery("manifestIssuance", contents);
            return JObject.Parse(contents);
        }

        protected Dictionary<string,string> GetSelfAssertedClaims( JObject manifest ) {
            Dictionary<string, string> claims = new Dictionary<string, string>();
            if (manifest["input"]["attestations"]["idTokens"][0]["id"].ToString() == "https://self-issued.me") {
                foreach (var claim in manifest["input"]["attestations"]["idTokens"][0]["claims"]) {
                    claims.Add(claim["claim"].ToString(), "");
                }
            }
            return claims;
        }
        /// ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
        /// REST APIs
        /// ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

        [HttpGet("/api/issuer/echo")]
        public ActionResult Echo() {
            TraceHttpRequest();
            try {
                JObject manifest = GetIssuanceManifest();
                Dictionary<string,string> claims = GetSelfAssertedClaims( manifest );
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
                    selfAssertedClaims = claims
                };
                return ReturnJson(JsonConvert.SerializeObject(info));
            } catch (Exception ex) {
                return ReturnErrorMessage(ex.Message);
            }
        }

        [HttpGet]
        [Route("/api/issuer/logo.png")]
        public ActionResult Logo() {
            TraceHttpRequest();
            JObject manifest = GetIssuanceManifest();
            return Redirect(manifest["display"]["card"]["logo"]["uri"].ToString());
        }

        [HttpGet("/api/issuer/issue-request")]
        public ActionResult IssuanceReference() {
            TraceHttpRequest();
            try {
                string correlationId = Guid.NewGuid().ToString();
                VCIssuanceRequest request = new VCIssuanceRequest() {
                    includeQRCode = false,
                    authority = this.AppSettings.VerifierAuthority,
                    registration = new Registration() {
                        clientName = this.AppSettings.client_name
                    },
                    callback = new Callback() {
                        url = string.Format("{0}/issue-callback", GetApiPath()),
                        state = correlationId,
                        headers = new Dictionary<string, string>() { { "my-api-key", this.AppSettings.ApiKey } }
                    },
                    issuance = new Issuance() {
                        type = this.AppSettings.CredentialType,
                        manifest = this.AppSettings.DidManifest,
                        pin = null
                    }
                };

                // if pincode is required, set it up in the request
                if (this.AppSettings.IssuancePinCodeLength > 0 ) {
                    int pinCode = RandomNumberGenerator.GetInt32(1, int.Parse("".PadRight(this.AppSettings.IssuancePinCodeLength, '9') ) );
                    _log.LogTrace("pin={0}", pinCode);
                    request.issuance.pin = new Pin() { 
                        length = this.AppSettings.IssuancePinCodeLength,
                        value = string.Format("{0:D" + this.AppSettings.IssuancePinCodeLength.ToString() + "}", pinCode)
                    };
                }

                // set self-asserted claims passed as query string parameters
                // This sample assumes that ALL claims comes from the UX
                JObject manifest = GetIssuanceManifest();
                Dictionary<string,string> claims = GetSelfAssertedClaims( manifest );
                if (claims.Count > 0 ) {
                    request.issuance.claims = new Dictionary<string, string>();
                    foreach (KeyValuePair<string, string> kvp in claims) {
                        request.issuance.claims.Add(kvp.Key, this.Request.Query[kvp.Key].ToString());
                    }
                }

                string jsonString = JsonConvert.SerializeObject( request, Formatting.None, new JsonSerializerSettings {
                    NullValueHandling = NullValueHandling.Ignore
                });
                _log.LogTrace("VC Client API Request\n{0}", jsonString);

                string contents = "";
                HttpStatusCode statusCode = HttpStatusCode.OK;
                if ( !HttpPost(jsonString, out statusCode, out contents) ) {
                    _log.LogError("VC Client API Error Response\n{0}", contents);
                    return ReturnErrorMessage( contents );
                }
                // add the id and the pin to the response we give the browser since they need them
                JObject requestConfig = JObject.Parse(contents);
                if (this.AppSettings.IssuancePinCodeLength > 0) {
                    requestConfig["pin"] = request.issuance.pin.value;
                }
                requestConfig.Add(new JProperty("id", correlationId));
                jsonString = JsonConvert.SerializeObject(requestConfig);
                _log.LogTrace("VC Client API Response\n{0}", jsonString);
                return ReturnJson( jsonString );
            }  catch (Exception ex)  {
                return ReturnErrorMessage( ex.Message );
            }
        }

        [HttpPost("/api/issuer/issue-callback")]
        public ActionResult IssuanceCallbackModel() {
            TraceHttpRequest();
            try {
                string body = GetRequestBody();
                _log.LogTrace(body);
                VCCallbackEvent callback = JsonConvert.DeserializeObject<VCCallbackEvent>(body);
                CacheObjectWithExpiery(callback.state, callback);
                return new OkResult();
            } catch (Exception ex) {
                return ReturnErrorMessage(ex.Message);
            }
        }

        [HttpGet("/api/issuer/issue-response")]
        public ActionResult IssuanceResponseModel() {
            TraceHttpRequest();
            try {
                string correlationId = this.Request.Query["id"];
                if (string.IsNullOrEmpty(correlationId)) {
                    return ReturnErrorMessage("Missing argument 'id'");
                }
                if (GetCachedObject<VCCallbackEvent>(correlationId, out VCCallbackEvent callback)) {
                    if (callback.code == "request_retrieved") {
                        return ReturnJson(JsonConvert.SerializeObject(new { status = 1, message = "QR Code is scanned. Waiting for issuance to complete." }));
                    }
                    if (callback.code == "issuance_succesful") {
                        return ReturnJson(JsonConvert.SerializeObject(new { status = 2, message = "Issuance process is completed" }));
                    }
                    if (callback.code == "issuance_failed") {
                        return ReturnJson(JsonConvert.SerializeObject(new { status = 99, message = "Issuance process failed with reason: " + callback.error.message }));
                    }
                    RemoveCacheValue(correlationId);
                }
                return new OkResult();
            } catch (Exception ex) {
                return ReturnErrorMessage(ex.Message);
            }
        }
    } // cls
} // ns
