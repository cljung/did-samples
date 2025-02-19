# Verified ID sample using ServiceBus and SignalR

This code sample demonstrates how to use [Azure ServiceBus](https://learn.microsoft.com/en-us/azure/service-bus-messaging/service-bus-messaging-overview) 
as forwarder for the Microsoft Entra Verified ID callbacks. 
It is an exact copy of the offical sample [1-asp-net-core-api-idtokenhint](https://github.com/Azure-Samples/active-directory-verifiable-credentials-dotnet/tree/main/1-asp-net-core-api-idtokenhint) 
with the exception that it uses SignalR instead of the standard polling mechanism and also ServiceBus to relay Verified ID callbacks..

## About this sample

Learn more about the sample in the official README. For the SignalR part, see README in [SignalR sample](../VerifiedID-SignalR/README.md). 

### ServiceBus - why?

Verified ID request Service API makes [callbacks](https://learn.microsoft.com/en-us/entra/verified-id/presentation-request-api#callback-events) 
to notify status and progress of issuance or presentation requests. These callbacks are made from Azure infrastructure. 
If your environment can't accept incoming HTTPS calls from an external part, such as Azure, you have a problem as you will not see the presented Verified ID credential, etc. 
This sample uses Azure ServiceBus to mitigate this problem and here's how it works:

1. This app makes an API request to Verified ID an passes a callback URL to the [ServiceBusForwarder](../ServiceBusForwarder) app (other app).
2. Verified ID makes HTP POSTs to the [ServiceBusForwarder](../ServiceBusForwarder) app.
3. The [ServiceBusForwarder](../ServiceBusForwarder) app takes the HTTP POST body and sends it on a ServiceBus message queue
4. This app listens to the ServiceBus message queue and receives the forwarded callback and processes it just as it would have come from Verified ID.

The thing that makes is works is that listening to a ServiceBus queue is made via outgoing HTTPS calls. This means you don't have to reconfigure your firewall 
to accept incoming network traffic from external parts, like Azure.

## Deploy

In order to simulate that your are in an internal, isolated, network, you really should run this app locally and deploy [ServiceBusForwarded](../ServiceBusForwarder) to Azure. 

### Running the app locally

If you run the app on your local machine you need to register an app in Entra ID, grant the permission to the app, create a client secret and then update your appsettings.json with it. 
You also need a reverse proxy tool like ngrok as you can't use localhost. Please see instructions in official README.

## Changes made to get ServiceBus to work

There are not that many changes to get ServiceBus to work, but since the official samples are written all around HTTPS endpoints and REST APIs, some code refactoring 
was needed to make methods available if callbacks didn't originate from an HTTPS POST. One such place is in [CallBackController.cs](CallbackController.cs#L110) where it 
processes a callback event.

### Program.cs

In the startup code in [Program.cs](Program.cs), the app starts listening to the ServiceBus queue before hitting `app.Run();`.

```CSharp
var clientOptions = new ServiceBusClientOptions() { TransportType = ServiceBusTransportType.AmqpTcp };
ServiceBusClient client = new ServiceBusClient( app.Configuration["VerifiedID:SbConnectionString"].ToString(), clientOptions );
string queueName = app.Configuration["VerifiedID:SbQueueName"].ToString();
ServiceBusProcessor processor = client.CreateProcessor( queueName, new ServiceBusProcessorOptions() );
processor.ProcessMessageAsync += ReceivedMessageHandler;
processor.ProcessErrorAsync += ErrorHandler;
await processor.StartProcessingAsync();

app.Run();

await processor.StopProcessingAsync();
```

### Message handler 

The ReceivedMessageHandler unpacks the message, creates an instance of the `CallbackController` and calls the `ProcessRequestCallback` method. 
The `ActivatorUtilities.CreateInstance` is a way to create an instance of a class that needs dependency injection in its constructor.

```CSharp
async Task ReceivedMessageHandler( ProcessMessageEventArgs args ) {
    string body = args.Message.Body.ToString();
    string requestType = null;
    if (args.Message.ApplicationProperties.ContainsKey( "requestType" )) {
        requestType = args.Message.ApplicationProperties["requestType"].ToString();
    }
    string apiKey = null;
    if ( args.Message.ApplicationProperties.ContainsKey("api-key") ) {
        apiKey = args.Message.ApplicationProperties["api-key"].ToString();
    }
    app.Logger.LogTrace( $"[SB] Received:\n{body}" );    
    try {
        var callbackController = ActivatorUtilities.CreateInstance<CallbackController>( app.Services );
        callbackController.ProcessRequestCallback( Enum.Parse<CallbackController.RequestType>( requestType ), body, apiKey, out string errorMessage );
    } catch ( Exception ex ) { 
        Console.WriteLine( ex.ToString() );
    }
    await args.CompleteMessageAsync( args.Message );
}
```

### Issuer/VerifierCallback.cs

When creating the issuance or presentation requests, we need to alter the callback URL and point it to the [ServiceBusForwarder](../ServiceBusForwarder) app. 
This is done after we have created the request object and before we call the Verified ID API. 
The code passes the `type` query string parameter to indicate which type of request it is (remember that you get `request_retrieved` for both issuance and presentation). 
Then just to show some flexibility, the `queue` name is also added as a query string parameter so that ServiceBusForwarder could be serving multiple apps, each with their own queue.

```CSharp
request.callback.url = $"{_configuration["VerifiedID:SbCallbackURL"]}?type=Presentation&queue={_configuration["VerifiedID:SbQueueName"]}";
```

### CallbackController.cs

The code that handles the callback event is refactored so that the HTTP Endpoint calls the internal `ProcessRequestCallback` - which is also called when a 
ServiceBus message is received.

```CSharp
public bool ProcessRequestCallback( RequestType requestType, string body, string apiKey, out string errorMessage ) {
  ...
}

private async Task<ActionResult> HandleRequestCallback( RequestType requestType, string body ) {
  ...
  if (!ProcessRequestCallback( requestType, body, apiKey, out string errorMessage )) {
        return BadRequest( new { error = "400", error_description = errorMessage } );
  } else {
    return new OkResult();
  }
}

```

```