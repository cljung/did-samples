# Verified ID sample using static QR codes and SignalR

This code sample demonstrates two ways of using a static QR code. 

## About this sample

This code sample demonstrates two ways of using a static QR code. The QR code in Microsoft Entra Verified ID samples contains a URI for retriving a request from Verified ID. 
That request was created with a call to [createPresentationRequest](https://learn.microsoft.com/en-us/entra/verified-id/presentation-request-api) API. A request created this way expires 
after 5 minutes, meaning the user must scan it withing this timeframe. A static QR code have no expiry and can live forever. It could even be printed on a sticker.

This sample is a clone of the offical sample [1-asp-net-core-api-idtokenhint](https://github.com/Azure-Samples/active-directory-verifiable-credentials-dotnet/tree/main/1-asp-net-core-api-idtokenhint) 
but it has been modified to use [SignalR](learn.microsoft.com/en-us/aspnet/core/tutorials/signalr) instead of the standard polling mechanism and in addition of using a static QR code.

## Deploy to Azure

Complete the [setup](#Setup) before deploying to Azure so that you have all the required parameters.

[![Deploy to Azure](https://aka.ms/deploytoazurebutton)](https://portal.azure.com/#create/Microsoft.Template/uri/https%3A%2F%2Fraw.githubusercontent.com%2FCLJUNG%2FDID-SAMPLES%2Fmain%2FVerifiedID-StaticQRCode%2FARMTemplate%2Ftemplate.json)

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

## SignalR - how does it work?

Please start with looking at the [VerifiedID-SignalR](../VerifiedID-SignalR) sample for detailed explanation on switching from polling to using SignalR.
This sample adds another use of SignalR and that is to broadcast events to multiple subscribers. In the StationMonitor page (explained further down), each monitor page opened 
subscribes to presentation events for a certain ***station***. When presentation of a credential happens at that station, the app broadcasts that event to all subscribers so they can 
update their page.

## Static QR code - how does it work?

In normal use of Verified ID, the response to calling createPresentationRequest is like the below JSON. Apps then uses the `url` to generate a QR code from.
The `request_uri` points back to `verifiedid.did.msidentity.com` and when the Authenticator scans the QR code, it makes a HTTP GET request to retrieve the request.  

```JSON
{
    "requestId": "e4ef27ca-eb8c-4b63-823b-3b95140eac11",
    "url": "openid-vc://?request_uri=https://verifiedid.did.msidentity.com/v1.0/00001111-aaaa-2222-bbbb-3333cccc4444/verifiableCredentials/presentationRequests/e4ef27ca-eb8c-4b63-823b-3b95140eac11",
    "expiry": 1633017751
}
```

In the static QR code example, the app calls it's own API `api/verifier/get-static-link` which but returns a similar JSON structure but with 
the difference that the `url` points back to the app itself. 

```JSON
{
    "requestId": "1a5212a0-cb60-43a0-8960-129f80bc82ea",
    "url": "openid-vc://?request_uri=https://1111-222-333-44-55.ngrok-free.app/api/verifier/presentation-request-proxy/1a5212a0-cb60-43a0-8960-129f80bc82ea/",
    "expiry": 1742646511
}
```

Microsoft Authenticator will make a call to this endpoint when it scans the QR code. What the `api/verifier/presentation-request-proxy` API then does 
is to then call Verified ID `createPresentationrequest` and return whatever response it gets to the Authenticator. The Authenticator is then 
happy as it was returned a proper request.

### VerifierCallback.cs

The `get-static-link` endpoint generates the static QR code. It can be called with or without a `stationId`. If called with a `stationId`, it is appended to the request_uri.

```CSharp
[AllowAnonymous]
[HttpGet( "/api/verifier/get-static-link" )]
[HttpGet( "/api/verifier/get-static-link/{stationId}" )]
public async Task<ActionResult> GetStaticLink(string stationId ) {
```

The `presentation-request-proxy` endpoint acts as a proxy between Authenticator and Verified ID. It is called when the Authenticator scans the QR code. It calls Verified ID `createPresentationRequest` to create a request just-in-time, then passes the Verified ID response back with the request JWT. If the static QR code was created with a `stationId`, it is part of the URL and will be cached with the request. When presentation callback events are received, the app uses SignalR to broadcast to all listening StationMonitors.

```CSharp
[AllowAnonymous]
[HttpGet( "/api/verifier/presentation-request-proxy/{correlationId}" )]
[HttpGet( "/api/verifier/presentation-request-proxy/{correlationId}/{stationId}" )]
public async Task<ActionResult> StaticPresentationReferenceProxy( string stationId, string correlationId ) {
```

The `subscribeToStation` endpoint is called by StationMonitor when the page loads to subscribe to a `stationId`.

```CSharp
[AllowAnonymous]
[HttpPost( "/api/subscribeToStation/{stationId}" )]
public async Task<ActionResult> SubscribeToStation( string stationId ) {
```

### CallbackController.cs

When we get the callback events from VerifiedID, the implementation in the official sample code is just to cache the data for the UI to pick up during its polling. 
Using SignalR, the UI doesn't poll and we send a SignalR event from the webapp to the UI instead. 

The `connectionId` comes from the page that displays the static QR code so that it can update the UI when events occur. Even the Station page makes use of this to 
show some progress. If the cached presentation data contains a `stationId`, then there is a list of subscribers that should be notified via a SignalR broadcast.  

```CSharp
if ( reqState.ContainsKey("connectionId") ) {
    string connectionId = reqState["connectionId"].ToString();
    _log.LogTrace( $"SignalR - Sending {callback.requestStatus} to connectionId {connectionId}" );
    await _hubContext.Clients.Client( connectionId ).SendAsync( "RequestCallback"
                                                                , callback.state
                                                                , requestType.ToString().ToLowerInvariant()
                                                                , callback.requestStatus
                                                                , callback.error == null ? null : JsonConvert.SerializeObject( callback.error ) );
}
if (reqState.ContainsKey("stationId") && callback.requestStatus == presentationStatus[1] ) {
    string stationId = reqState["stationId"].ToString();
    if (_cache.TryGetValue<List<string>>( stationId, out List<string> connections )) {
        _log.LogTrace( $"SignalR - Sending {callback.requestStatus} to stationId {stationId}" );
        await _hubContext.Clients.Clients( connections ).SendAsync( "StationEvent"
                                                                , stationId
                                                                , callback.state
                                                                , callback.requestStatus
                                                                , callback.error == null ? null : JsonConvert.SerializeObject( callback.error ) );
    }
}
```

### Index.cshtml 

The `Index` page calls the `get-static-link/` API (without `stationId`) to generate a static QR code. The QR code has no expiry and the scanning can happen several hours after the page was loaded. Once you scan the QR code with the Authenticator, the page redirects to `PresentationVerified` page to display the results. This page is very similar to the official samples with the 

### Station.cshtml 

The `Station` page generates a random `stationId` number and then calls the `get-static-link/{stationId}` API to generate a static QR code. The QR code stays the same until you close or reload the page, but since it is a static QR code, you can scan it multiple times. Each presentation result in an update in the `StationMonitor` page.

### StationMonitor.cshtml 

`StationMonitor` page is opened via a link in the `Station` page. It starts off by subscribing to the stationId passed as a query string parameter. Then it simply awaits SignalR broadcast events for its `stationId`. When an event is received, it calls the `request-status` API to retrieve the presentation result and adds a row in the list.

