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

1. The other [app](../VerifiedID-ServiceBus) making the request to Verified ID passes a callback URL to the ServiceBusForwarder app (this app).
2. Verified ID makes HTP POSTs to the ServiceBusForwardwer app.
3. The ServiceBusForwarder app takes the HTTP POST body and sends it on a ServiceBus message queue
4. The other [app](../VerifiedID-ServiceBus) listens to the ServiceBus message queue and receives the forwarded callback.

The thing that makes is works is that listening to a ServiceBus queue is made via outgoing HTTPS calls. This means you don't have to reconfigure your firewall 
to accept incoming network traffic from external parts, like Azure.

## Deploy to Azure

[Create](https://learn.microsoft.com/en-us/azure/service-bus-messaging/service-bus-quickstart-portal) your ServiceBus queue first and have the connectiong string and queue name ready.

[![Deploy to Azure](https://aka.ms/deploytoazurebutton)](https://portal.azure.com/#create/Microsoft.Template/uri/https%3A%2F%2Fraw.githubusercontent.com%2Fcljung%2Fdid-samples%2Fmain%2FServiceBusForwarder%2FARMTemplate%2Ftemplate.json)

### Running the app locally

You can run the ServiceBusForwarder locally for testing. In that case you update the `appsettings.json` file with the connection string and queue name and then start `ngrok`.

```Powershell
ngrok http 5010
```

## How the ServiceBusForwarder work

It is a really simple program and everything happens in `app.MapPost` in [Program.cs](Program.cs).

