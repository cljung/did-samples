using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Security.Policy;
using System.Threading.Tasks;
using Azure;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Configuration;
using Newtonsoft.Json;

namespace AspNetCoreVerifiableCredentials.Pages
{
    public class StationModel : PageModel
    {
        private IConfiguration _configuration;
        public StationModel( IConfiguration configuration ) {
            _configuration = configuration;
        }
        public void OnGet()
        {
            string credentialType = _configuration["VerifiedID:CredentialType"];
            string[] acceptedIssuers = new string[] { _configuration["VerifiedID:DidAuthority"] };

            string stationId = null;
            if (this.Request.Query.ContainsKey( "stationId" )) {
                stationId = this.Request.Query["stationId"].ToString();
            } else {
                stationId = string.Format( "{0:D4}", RandomNumberGenerator.GetInt32( 1, int.Parse( "".PadRight( 4, '9' ) ) ) );
            }
            ViewData["stationId"] = stationId;

            ViewData["Message"] = "";
            ViewData["acceptedIssuers"] = acceptedIssuers;

            // setting credentialType via QueriString parameter?
            if (this.Request.Query.ContainsKey( "credentialType" )) {
                credentialType = this.Request.Query["credentialType"].ToString();
                this.Request.HttpContext.Session.SetString( "credentialType", credentialType );
            } else {
                HttpContext.Session.Remove( "credentialType" );
            }
            ViewData["CredentialType"] = credentialType;

            // setting acceptedIssuers via QueriString parameter? Note - we store "*" to distinguish from empty array
            if (this.Request.Query.ContainsKey( "acceptedIssuers" )) {
                acceptedIssuers = this.Request.Query["acceptedIssuers"].ToString().Split( ";", StringSplitOptions.RemoveEmptyEntries );
                if (acceptedIssuers.Length == 0) {
                    acceptedIssuers = new string[] { "*" };
                }
                this.Request.HttpContext.Session.SetString( "acceptedIssuers", string.Join( ";", acceptedIssuers ) );
            } else {
                this.Request.HttpContext.Session.Remove( "acceptedIssuers" );
            }
            ViewData["acceptedIssuers"] = acceptedIssuers;
        }
    }
}
