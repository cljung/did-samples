using Microsoft.IdentityModel.Tokens;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.IdentityModel.Tokens.Jwt;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Claims;
using System.Text;

namespace VerifiedIDEAM.Helpers
{
    public class IdTokenHintValidator
    {
        public string IdToken { get; set; }
        public JObject JwtHeader { get; set; }
        public JObject JwtPayload { get; set; }
        public string WellKnownMetadataEndpoint { get; set; }
        public JObject WellKnownOidcMetadata { get; set; }
        public JObject Jwks { get; set; }

        public IdTokenHintValidator( string token ) { 
            IdToken = token;
            JwtHeader = GetJwtHeader( token );
            JwtPayload = GetJwtPayload( token );
        }
        private JObject GetJwtPart( string jwtToken, int part ) {
            if (!(part == 0 || part == 1))
                throw new ArgumentOutOfRangeException( "part", "Must be 0 or 1" );
            string[] parts = jwtToken.Split( "." );
            parts[part] = parts[part].PadRight( 4 * ((parts[part].Length + 3) / 4), '=' );
            return JObject.Parse( Encoding.UTF8.GetString( Convert.FromBase64String( parts[part] ) ) );
        }

        // Parse the JWT header into a JObject
        private JObject GetJwtHeader( string jwtToken ) {
            return GetJwtPart( jwtToken, 0 );
        }
        private JObject GetJwtPayload( string jwtToken ) {
            return GetJwtPart( jwtToken, 1 );
        }
        public bool ContainsClaim( string name ) {
            return JwtPayload.ContainsKey(name);
        }
        public string GetClaim( string name ) {
            if ( JwtPayload.ContainsKey(name) )
                 return JwtPayload[name].ToString(); 
            else return null;
        }
        public bool LoadMetadata(string wellKnownOidcConfigurationEndpoint = null ) {
            Jwks = null;
            if ( string.IsNullOrWhiteSpace(wellKnownOidcConfigurationEndpoint) ) {
                wellKnownOidcConfigurationEndpoint = $"{GetClaim( "iss" )}/.well-known/openid-configuration";
            }
            WellKnownMetadataEndpoint = wellKnownOidcConfigurationEndpoint;
            HttpClient client = new HttpClient();
            HttpResponseMessage res = client.GetAsync(wellKnownOidcConfigurationEndpoint).Result;
            string oidcConfig = res.Content.ReadAsStringAsync().Result;
            
            if ( !res.IsSuccessStatusCode ) {
                client.Dispose();
                return false;
            }

            WellKnownOidcMetadata = JObject.Parse(oidcConfig);
            string jwks_uri = WellKnownOidcMetadata["jwks_uri"].ToString();

            res = client.GetAsync(jwks_uri).Result;
            string keys = res.Content.ReadAsStringAsync().Result;
            client.Dispose();

            if ( res.IsSuccessStatusCode ) {
                Jwks = JObject.Parse(keys);
                return true;
            } else return false;
        }
        public static List<string> LoadAllowedAudiences( string tenantId ) {
            List<string> allowedAudiences = new List<string>();
            string url = $"https://login.microsoft.com/{tenantId}/metadata/json/1";
            HttpClient client = new HttpClient();
            HttpResponseMessage res = client.GetAsync( url ).Result;
            string metadata = res.Content.ReadAsStringAsync().Result;

            if (!res.IsSuccessStatusCode) {
                client.Dispose();
                return allowedAudiences;
            }
            
            var json = JObject.Parse( metadata );
            if ( json.ContainsKey("allowedAudiences") ) {
                foreach (var aa in json["allowedAudiences"]) { 
                    string domain = aa.ToString().Split("@")[1];
                    if ( domain != tenantId ) {
                        allowedAudiences.Add( domain );
                    }
                }
            }
            return allowedAudiences;
        }
        // Validate a JWT token that is either self signed or issued+signed by an OIDC provider
        public bool ValidateJwtToken(string? aud, out string errorMessage  ) {
            errorMessage = "";
            if ( null == WellKnownOidcMetadata ) {
                if (!LoadMetadata()) {
                    errorMessage = $"Could not load well-known metadata from {WellKnownMetadataEndpoint}";
                    return false;
                }
            }
            // just get the JWT claims so we can output them to the console
            //int nbf = int.Parse(JwtPayload["nbf"].ToString());
            //int exp = int.Parse( JwtPayload["exp"].ToString());
            SecurityKey signKey = CreateJsonWebKey(JwtHeader["kid"].ToString(), Jwks);
            var tokenHandler = new JwtSecurityTokenHandler();
            try {
                tokenHandler.ValidateToken(IdToken, new TokenValidationParameters {
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = signKey,
                    ValidateIssuer = true,
                    ValidIssuer = WellKnownOidcMetadata["issuer"].ToString(),
                    ValidateAudience = !string.IsNullOrWhiteSpace(aud),
                    ValidAudience = aud,
                    ValidateLifetime = false, // we will get an near expired or an expired id_token from Entra
                    ClockSkew = TimeSpan.Zero // tokens expires on the exact second
                }, out SecurityToken validatedToken);
                return true;
            } catch (Exception ex) {
                errorMessage = ex.Message;
                Console.WriteLine(ex.Message);
                return false;
            }
        }
        // Create a Json Web Key for the JWT token based on the JWKS from the .well-known/openid-configuration metadata 
        private JsonWebKey CreateJsonWebKey(string kid, JObject jwksConfig) {
            JsonWebKey jwk = null;
            // find matching 'kid' in metadata from jwks_uri
            foreach (var key in jwksConfig["keys"]) {
                if (key["kid"].ToString() == kid) {
                    jwk = JsonConvert.DeserializeObject<JsonWebKey>( JsonConvert.SerializeObject(key) );
                    return jwk;
                }
            }
            return null;
        }
    }
}
