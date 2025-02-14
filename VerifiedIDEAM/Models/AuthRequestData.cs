using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace VerifiedIDEAM.Models
{
    public class AuthRequest
    {
        public string txid { get; set;  }

        public string tenantId { get; set; }
        public string endpoint { get; set; }
        public string response_type { get; set; }
        public string scope { get; set;  }
        public string client_id { get; set; }
        public string redirect_uri { get; set;  }
        public string state { get; set;  }
        public string response_mode { get; set;  }
        public string id_token_hint { get; set; }
        public string nonce { get; set; }

        public string preferred_username { get; set; }
        public string name { get; set; }
        public bool guestAccount { get; set; }
        public bool authOK { get; set; }
        public double matchConfidenceScore { get; set; }
        public DateTime vcExpirationDate { get; set; }
        public string grant_type { get; set; }
        public string clientRequestId { get; set; }
        public string idtSub { get; set; } // id_token_hint sub(ject)
    }
}
