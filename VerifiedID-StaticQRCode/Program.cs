using System;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.AspNetCore.Builder;
using VerifiedIDSignalR.Hubs;

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

app.Run();
