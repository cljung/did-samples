using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Server.Kestrel.Core;

public class Program {
    public static void Main( string[] args ) {
        var builder = WebApplication.CreateBuilder( args );
        builder.Services.AddControllersWithViews();
        builder.Services.AddHttpClient();
        builder.Services.Configure<KestrelServerOptions>( options => {
            options.AllowSynchronousIO = true;
        } );
        builder.Services.Configure<IISServerOptions>( options => {
            options.AllowSynchronousIO = true;
        } );

        var app = builder.Build();
        app.UseForwardedHeaders( new ForwardedHeadersOptions {
            ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto | ForwardedHeaders.XForwardedHost
        } );
        if (!app.Environment.IsDevelopment()) {
            app.UseExceptionHandler( "/Home/Error" );
            // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
            app.UseHsts();
        }
        app.UseHttpsRedirection();
        app.UseRouting();
        app.MapControllerRoute( name: "default", pattern: "{controller=Home}/{action=Index}/{id?}" );

        app.Run();
    }
}
