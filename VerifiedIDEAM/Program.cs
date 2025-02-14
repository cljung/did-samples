using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Configuration;
using VerifiedIDEAM.Helpers;

namespace VerifiedIDEAM {
    public class Program {
        public static void Main( string[] args ) {
            var builder = WebApplication.CreateBuilder( args );

            // Add services to the container.
            builder.Services.AddControllersWithViews();

            builder.Services.Configure<ForwardedHeadersOptions>( options => {
                options.ForwardedHeaders =
                    ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto | ForwardedHeaders.XForwardedHost;
            } );

            builder.Services.AddMemoryCache();
            // Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
            builder.Services.AddEndpointsApiExplorer();
            builder.Services.AddSwaggerGen();
            builder.Services.AddHttpClient();

            var app = builder.Build();

            // Configure the HTTP request pipeline.
            if (!app.Environment.IsDevelopment()) {
                app.UseExceptionHandler( "/Home/Error" );
                // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
                app.UseHsts();
                app.UseHttpsRedirection();
            } else {
                app.UseSwagger();
                app.UseSwaggerUI();
            }

            //app.UseHttpsRedirection();
            app.UseStaticFiles();

            app.UseRouting();

            app.UseAuthorization();

            app.MapControllerRoute(
                name: "default",
            pattern: "{controller=Home}/{action=Index}/{id?}" );

            System.Environment.SetEnvironmentVariable( "API-KEY", Guid.NewGuid().ToString() );

            app.Run();
        }
    }
}