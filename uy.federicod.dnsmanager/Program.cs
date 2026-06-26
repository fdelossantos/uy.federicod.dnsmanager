using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Mvc.Authorization;
using Microsoft.Identity.Web;
using Microsoft.Identity.Web.UI;
using uy.federicod.dnsmanager.Security;

namespace uy.federicod.dnsmanager
{
    public class Program
    {
        public static void Main(string[] args)
        {
            var builder = WebApplication.CreateBuilder(args);

            // Add services to the container.
            var authProvider = builder.Configuration["Authentication:Provider"];
            if (string.Equals(authProvider, "Test", StringComparison.OrdinalIgnoreCase))
            {
                builder.Services.AddAuthentication(TestAuthenticationDefaults.AuthenticationScheme)
                    .AddScheme<TestAuthenticationOptions, TestAuthenticationHandler>(
                        TestAuthenticationDefaults.AuthenticationScheme,
                        options =>
                        {
                            builder.Configuration.GetSection("Authentication:Test").Bind(options);
                        });
            }
            else
            {
                builder.Services.AddAuthentication(OpenIdConnectDefaults.AuthenticationScheme)
                    .AddMicrosoftIdentityWebApp(builder.Configuration.GetSection("AzureAd"));
            }

            builder.Services.AddControllersWithViews(options =>
            {
                var policy = new AuthorizationPolicyBuilder()
                    .RequireAuthenticatedUser()
                    .Build();
                options.Filters.Add(new AuthorizeFilter(policy));
            });
            builder.Services.AddAuthorization(options =>
            {
                var authenticatedUserPolicy = new AuthorizationPolicyBuilder()
                    .RequireAuthenticatedUser()
                    .Build();
                options.DefaultPolicy = authenticatedUserPolicy;
                options.FallbackPolicy = authenticatedUserPolicy;
            });
            builder.Services.AddRazorPages()
                .AddMicrosoftIdentityUI();
            builder.Services.Configure<ForwardedHeadersOptions>(options =>
            {
                options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
                options.KnownNetworks.Clear();
                options.KnownProxies.Clear();
            });

            var app = builder.Build();

            // Configure the HTTP request pipeline.
            app.UseForwardedHeaders();

            if (!app.Environment.IsDevelopment())
            {
                app.UseExceptionHandler("/Home/Error");
                // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
                app.UseHsts();
            }

            app.UseHttpsRedirection();
            app.UseStaticFiles();

            app.UseRouting();

            app.UseAuthentication();
            app.UseAuthorization();

            app.MapGet("/healthz", () => Results.Ok("ok")).AllowAnonymous();

            app.MapControllerRoute(
                name: "default",
                pattern: "{controller=Home}/{action=Index}/{id?}")
                .RequireAuthorization();
            app.MapRazorPages()
                .RequireAuthorization();

            app.Run();
        }
    }
}
