---
page_type: sample
languages:
- dotnet
- powershell
products:
- Entra
- Verified ID
description: "A code sample demonstrating issuance and verification of verifiable credentials."
urlFragment: "active-directory-verifiable-credentials-dotnet"
---
# Verified ID sample using SignalR

This code sample demonstrates how to use Microsoft Entra Verified ID to issue and consume verifiable credentials using [SignalR](learn.microsoft.com/en-us/aspnet/core/tutorials/signalr). 
It is an exact copy of the offical sample [1-asp-net-core-api-idtokenhint](https://github.com/Azure-Samples/active-directory-verifiable-credentials-dotnet/tree/main/1-asp-net-core-api-idtokenhint) 
with the exception that it uses SignalR instead of the standard polling maechanism.

## About this sample

Learn about the sample in the official README. This README will only describe the changes made in order to get SignalR integrated. See further down in this README.

## Deploy to Azure

Complete the [setup](#Setup) before deploying to Azure so that you have all the required parameters.


[![Deploy to Azure](https://aka.ms/deploytoazurebutton)](https://portal.azure.com/#create/Microsoft.Template/uri/https%3A%2F%2Fraw.githubusercontent.com%2FCLJUNG%2FDID-SAMPLES%2Fmain%2FVerifiedID-SignalR%2FARMTemplate%2Ftemplate.json)

## Setup

Before you can run this sample make sure your environment is setup correctly, follow the instructions in the documentation [here](https://aka.ms/didfordevs).

### Configuring Managed Identity

If you deploy the sample to Azure App Services, Managed Identity is the preferred way of authenticating the app over using client secrets.

1. Enable Managed Identity for your App Service app at `Settings` > `Identity`
1. In portal.azure.com, open the `Cloud Shell` in powershell mode and run the following to grant your MSI service principal the permission to call Verified ID.

```Powershell
$TenantID="<YOUR TENANTID>"
$YourAppName="<NAME OF YOUR AZURE WEBAPP>"

#Do not change this values below
#
$ApiAppId = "3db474b9-6a0c-4840-96ac-1fceb342124f"
$PermissionName = "VerifiableCredential.Create.All"
 
# Install the module
Install-Module AzureAD

Connect-AzureAD -TenantId $TenantID

$MSI = (Get-AzureADServicePrincipal -Filter "displayName eq '$YourAppName'")

Start-Sleep -Seconds 10

$ApiServicePrincipal = Get-AzureADServicePrincipal -Filter "appId eq '$ApiAppId'"
$AppRole = $ApiServicePrincipal.AppRoles | Where-Object {$_.Value -eq $PermissionName -and $_.AllowedMemberTypes -contains "Application"}
New-AzureAdServiceAppRoleAssignment -ObjectId $MSI.ObjectId -PrincipalId $MSI.ObjectId ` -ResourceId $ApiServicePrincipal.ObjectId -Id $AppRole.Id
```

### Running the app locally

If you run the app on your local machine you need to register an app in Entra ID, grant the permission to the app, create a client secret and then update your appsettings.json with it. 
You also need a reverse proxy tool like ngrok as you can't use localhost. Please see instructions in official README.

## Changes made to get SignalR to work

There are actually fewer changes you need to make to integrate the sample code with SignalR than you might think. Below are all the places where the code is changed.

### Program.cs

In the startup code in [Program.cs](Program.cs), you need to add SignalR and map the hub we are going to use. SignalR comes with aspnet, so you don't need to add any nuget packages.

```CSharp
using VerifiedIDSignalR.Hubs;

builder.Services.AddSignalR();

app.MapHub<VerifiedIDHub>( "/verifiedIDHub" );
```

### VerifiedIDHub.cs

We need to define a class that represents the hub, but this class can be empty as we are not going to use it directly.

```CSharp
using Microsoft.AspNetCore.SignalR;

namespace VerifiedIDSignalR.Hubs;

public class VerifiedIDHub : Hub {    
}
```

### Issuer/VerifierCallback.cs

When the UI makes the calls to create the issuance or presentation requests, it passes the SignalR connectionId from the browser to the API in a new HTTP header we created. 
The APIs adds the connectionId to the cached data so we have it when we get the callbacks from VerifiedID's Request Service.

```CSharp
var cacheData = new
{
    status = "request_created",
    message = "Waiting for QR code to be scanned",
    expiry = requestConfig["expiry"].ToString(),
    connectionId = this.Request.Headers["X-SignalR-ConnectionId"].ToString()
};
```

### CallbackController.cs

When we get the callback events from VerifiedID, the implementation in the official sample code is just to cache the data for the UI to pick up during its polling. 
Using SignalR, the UI doesn't poll and we send a SignalR event from the webapp to the UI instead.

```CSharp
string connectionId = reqState["connectionId"].ToString();
_log.LogTrace( $"SignalR - Sending {callback.requestStatus} to connectionId {connectionId}");
await _hubContext.Clients.Client( connectionId ).SendAsync( "RequestCallback"
                                                            , callback.state
                                                            , requestType.ToString().ToLowerInvariant()
                                                            , callback.requestStatus
                                                            , callback.error == null ? null : JsonConvert.SerializeObject( callback.error ) );
```

We only send the request id (state), type, status and potential error message here. SignalR has a limit in how much data you can push in SendAsync. 
In the `presentation_verified` callback, we get the actual details from the presented VC, but since this can be quite large (photo can be big), 
we only send the status so that the UI can call the API and collect the data later. 

The CallbackController.cs gets the hub context interface via dependency injection in the constructor.

```CSharp
private IHubContext<VerifiedIDHub> _hubContext;
public CallbackController(IConfiguration configuration, IMemoryCache memoryCache, ILogger<CallbackController> log, IHubContext<VerifiedIDHub> hubContext )
{
    _configuration = configuration;
    _cache = memoryCache;
    _log = log;
    _apiKey = System.Environment.GetEnvironmentVariable("API-KEY");
    _hubContext = hubContext;
}
```

### Javascript 

This sample has a cloned version of the javascript snippet called `verifiedid.requestservice.client.signalr.js` that doesn't include the polling code. 
Instead, it creates a SignalR connection and waits for the callbacks. Depending on the status, it calls the already existing handlers.

```javascript
var connection = new signalR.HubConnectionBuilder().withUrl("/verifiedIDHub").build();

connection.on("RequestCallback", function (id, requestType, requestStatus, error ) {
    requestService.log(`SignalR - requestType: ${requestType},  requestStatus: ${requestStatus}`);
    switch (requestStatus) {
        case "request_retrieved":
            requestService.onRequestRetrieved(requestType);
            break;
        case "presentation_error":
        case "issuance_error":
            requestService.onError(requestType, error);
            break;
        case "issuance_successful":
            requestService.onIssuanceSuccesful(id, "");
            break;
        // we don't receive the actual details in the SignalR callback here, so we have to call API and get it
        case "presentation_verified":
        case "selfie_taken":
            requestService.getRequestStatus(id, requestStatus);
            break;

    }
});
```

The create request API calls passes the SignalR connectionId in the HTTP header so the webapp have it.

```javascript
this.createRequest = async function (url) {
    const response = await fetch(url, { 
                            method: 'GET', 
                            headers: { 
                                'Accept': 'application/json', 
                                'rsid': this.uuid, 
                                'X-SignalR-ConnectionId': connection.connectionId 
                              } 
                            });
```

### HTML pages

The HTML pages that handles the issuing and presentation logic are updated to include the SignalR javascript library `signalr.js` and also to use the modified `verifiedid.requestservice.client.signalr.js`.
```HTML
<script src="qrcode.min.js"></script>
<script src="signalr.js"></script>
<script src="verifiedid.requestservice.client.signalr.js"></script>
<script src="verifiedid.uihandler.js"></script>
```