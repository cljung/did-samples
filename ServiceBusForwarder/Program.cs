using System;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.AspNetCore.Builder;
using Azure.Messaging.ServiceBus;
using System.IO;
using System.Text;
using Microsoft.Extensions.Logging;

var builder = WebApplication.CreateBuilder(args);

builder.Services.Configure<KestrelServerOptions>(options => {
    options.AllowSynchronousIO = true;
});
builder.Services.Configure<IISServerOptions>( options => {
    options.AllowSynchronousIO = true;
} );

var app = builder.Build();

app.UseHttpsRedirection();

var clientOptions = new ServiceBusClientOptions() { TransportType = ServiceBusTransportType.AmqpTcp };
ServiceBusClient client = new ServiceBusClient( app.Configuration["AppSettings:SbConnectionString"].ToString(), clientOptions );

app.MapPost("/api/callback", async delegate(HttpContext context)
{
    string body = null;
    using (StreamReader reader = new StreamReader(context.Request.Body, Encoding.UTF8))
    {
        body = await reader.ReadToEndAsync();
    }
    string queueName = app.Configuration["AppSettings:SbQueueName"].ToString();
    if (context.Request.Query.ContainsKey( "queue" )) {
        queueName = context.Request.Query["queue"].ToString();
    }
    ServiceBusSender sender = client.CreateSender( queueName );
    ServiceBusMessage message = new ServiceBusMessage( body ) {
        ContentType = context.Request.ContentType,
        TimeToLive = new TimeSpan( 0, 0, 5, 0, 0 ) // days, hours, min, secs, ms
    };
    if (context.Request.Query.ContainsKey( "type" )) {
        message.ApplicationProperties.Add( "requestType", context.Request.Query["type"].ToString() );
    }
    if (context.Request.Headers.ContainsKey( "api-key" )) {
        message.ApplicationProperties.Add( "api-key", context.Request.Headers["api-key"].ToString() );
    }
    app.Logger.LogTrace( $"[SB] Sending to queue {queueName}:\n{body}" );
    await sender.SendMessageAsync( message );
    await sender.DisposeAsync();

    context.Response.StatusCode = 200;
});

app.Logger.LogTrace( "ServiceBusForwarder started" );

app.Run();

await client.DisposeAsync();

