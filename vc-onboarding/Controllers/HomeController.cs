using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using vc_onboarding.Models;
using System.Security.Claims;
using Microsoft.Extensions.Options;

namespace vc_onboarding.Controllers
{
    public class HomeController : Controller
    {
        private readonly ILogger<HomeController> _logger;
        protected readonly AppSettingsModel AppSettings;

        public HomeController(ILogger<HomeController> logger, IOptions<AppSettingsModel> appSettings)
        {
            _logger = logger;
            this.AppSettings = appSettings.Value;
        }

        public IActionResult Index()
        {
            return View();
        }

        public IActionResult Privacy()
        {
            return View();
        }

        public IActionResult Claims() {
            return View();
        }

        public IActionResult IssueVC() {

            string userObjectId = User.Claims.Where(c => c.Type == "http://schemas.microsoft.com/identity/claims/objectidentifier").Select(c => c.Value).SingleOrDefault();
            if (userObjectId == null) {
                userObjectId = User.Claims.Where(c => c.Type == "http://schemas.xmlsoap.org/ws/2005/05/identity/claims/nameidentifier").Select(c => c.Value).SingleOrDefault();
            }
            string surname = User.Claims.Where(c => c.Type == "http://schemas.xmlsoap.org/ws/2005/05/identity/claims/surname").Select(c => c.Value).SingleOrDefault();
            string givenname = User.Claims.Where(c => c.Type == "http://schemas.xmlsoap.org/ws/2005/05/identity/claims/givenname").Select(c => c.Value).SingleOrDefault();

            IDictionary<string, string> vcClaims = new Dictionary<string, string>();
            vcClaims.Add( "tid", this.AppSettings.TenantId);
            vcClaims.Add( "objectId", userObjectId);
            vcClaims.Add( "displayName", User.Identity.Name );
            vcClaims.Add( "lastName", surname);
            vcClaims.Add( "firstName", givenname);

            ViewData["vcClaims"] = vcClaims;

            return View();
        }
        public IActionResult VerifyVC() {
            IDictionary<string, string> vcClaims = new Dictionary<string, string>();
            vcClaims.Add("tid", "");
            vcClaims.Add("sub", "");
            vcClaims.Add("displayName", "");
            vcClaims.Add("lastName", "");
            vcClaims.Add("firstName", "");
            vcClaims.Add("title", "");
            vcClaims.Add("language", "");
            ViewData["vcClaims"] = vcClaims;
            return View();
        }

        [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
        public IActionResult Error()
        {
            return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
        }
    }
}
