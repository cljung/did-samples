using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace client_api_test_service_dotnet.Models
{
    public class AppSettingsModel
    {
        public string ApiEndpoint { get; set; }
        public string ApiKey { get; set; }
        public string UseAkaMs { get; set; }
        public string CookieKey { get; set; }
        public int CookieExpiresInSeconds { get; set; }
        public int CacheExpiresInSeconds { get; set; }
        public string ActiveCredentialType { get; set; }
        public string client_name { get; set; }

        public string TenantId { get; set; }
        public string scope { get; set;}
        public string ClientId { get; set; }
        public string ClientSecret { get; set; }
        public string Authority { get; set; }

    }
}
