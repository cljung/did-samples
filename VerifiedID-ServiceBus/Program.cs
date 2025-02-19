using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.AspNetCore.Builder;
using VerifiedIDServiceBus.Hubs;
using Azure.Messaging.ServiceBus;
using Azure.Identity;
using System.Diagnostics;
using VerifiedIDServiceBus;
using Microsoft.Extensions.Logging;

var builder = WebApplication.CreateBuilder(args);

builder.Services.Configure<KestrelServerOptions>(options => {
    options.AllowSynchronousIO = true;
});
builder.Services.Configure<IISServerOptions>( options => {
    options.AllowSynchronousIO = true;
} );


builder.Services.AddCors(o => o.AddPolicy("MyPolicy", b => {
    b.AllowAnyOrigin()
            .AllowAnyMethod()
            .AllowAnyHeader();
        // .WithOrigins("http://example.com","http://www.contoso.com");
}));

builder.Services.AddDistributedMemoryCache();
builder.Services.AddSession(options => {
    options.IdleTimeout = TimeSpan.FromMinutes(1);//You can set Time   
    options.Cookie.IsEssential = true;
    options.Cookie.HttpOnly = true;
} );
builder.Services.Configure<CookiePolicyOptions>(options => {
    // This lambda determines whether user consent for non-essential cookies is needed for a given request.
    options.CheckConsentNeeded = context => false;
    options.MinimumSameSitePolicy = SameSiteMode.None;
});

builder.Services.AddRazorPages();
builder.Services.AddHttpClient();  // use iHttpFactory as best practice, should be easy to use extra retry and hold off policies in the future

// SignalR
builder.Services.AddSignalR();

var app = builder.Build();

app.UseForwardedHeaders( new ForwardedHeadersOptions {
    ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto | ForwardedHeaders.XForwardedHost
} );

if (app.Environment.IsDevelopment())
{
    app.UseDeveloperExceptionPage();
}
else
{
    app.UseExceptionHandler("/Error");
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}
app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseRouting();
app.UseSession();
app.UseCors("MyPolicy");

app.MapControllerRoute( name: "default", pattern: "{controller=Home}/{action=Index}/{id?}" );
app.MapRazorPages();

// generate an api-key on startup that we can use to validate callbacks
System.Environment.SetEnvironmentVariable("API-KEY", Guid.NewGuid().ToString());

// SignalR
app.MapHub<VerifiedIDHub>( "/verifiedIDHub" );

var clientOptions = new ServiceBusClientOptions() { TransportType = ServiceBusTransportType.AmqpTcp };
ServiceBusClient client = new ServiceBusClient( app.Configuration["VerifiedID:SbConnectionString"].ToString(), clientOptions );
string queueName = app.Configuration["VerifiedID:SbQueueName"].ToString();
ServiceBusProcessor processor = client.CreateProcessor( queueName, new ServiceBusProcessorOptions() );
processor.ProcessMessageAsync += ReceivedMessageHandler;
processor.ProcessErrorAsync += ErrorHandler;
await processor.StartProcessingAsync();

app.Run();

await processor.StopProcessingAsync();

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

// handle any errors when receiving messages
Task ErrorHandler( ProcessErrorEventArgs args ) {
    app.Logger.LogTrace( args.Exception.ToString() );
    return Task.CompletedTask;
}
