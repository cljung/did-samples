using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace AspNetCoreVerifiableCredentials.Pages
{
    public class StationMonitorModel : PageModel
    {
        public void OnGet()
        {
            string stationId = null;
            if (this.Request.Query.ContainsKey( "stationId" )) {
                stationId = this.Request.Query["stationId"].ToString();
                ViewData["stationId"] = stationId;
            }

        }
    }
}
