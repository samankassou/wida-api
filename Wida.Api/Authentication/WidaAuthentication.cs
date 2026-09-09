using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Wida.Dal.Services.Interfaces;

namespace Wida.Api.Authentication;

public static class WidaAuthentication
{
    public const string CookieScheme = "Wida";
    public const string GoogleScheme = "Google";

    public static IServiceCollection AddWidaAuthentication(this IServiceCollection services, IConfiguration configuration, IWebHostEnvironment environment)
    {
        var access = new PilotAccess(configuration, environment);
        _ = access.PublicOrigin; // Fail early if the externally visible origin is unsafe or absent.
        services.AddSingleton(access);
        services.AddHttpContextAccessor();
        services.AddScoped<ICurrentUser, HttpCurrentUser>();
        services.AddScoped<GoogleAccountService>();
        services.AddScoped<WidaCookieEvents>();
        var protection = services.AddDataProtection().SetApplicationName("Wida.Api");
        var keyPath = configuration["Authentication:DataProtectionKeysPath"];
        if (!string.IsNullOrWhiteSpace(keyPath)) protection.PersistKeysToFileSystem(new DirectoryInfo(keyPath));
        else if (!environment.IsDevelopment())
            throw new InvalidOperationException("Configure Authentication:DataProtectionKeysPath on durable protected storage for deployed sessions.");

        var secure = environment.IsDevelopment() && access.PublicOrigin.StartsWith("http:", StringComparison.Ordinal)
            ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always;
        services.AddAntiforgery(options =>
        {
            options.HeaderName = "X-CSRF-TOKEN";
            options.Cookie.Name = "Wida.Antiforgery";
            options.Cookie.Path = "/";
            options.Cookie.HttpOnly = true;
            options.Cookie.SameSite = SameSiteMode.Strict;
            options.Cookie.SecurePolicy = secure;
        });
        var auth = services.AddAuthentication(CookieScheme).AddCookie(CookieScheme, options =>
        {
            options.Cookie.Name = "Wida.Session";
            options.Cookie.Path = "/";
            options.Cookie.HttpOnly = true;
            options.Cookie.SameSite = SameSiteMode.Lax;
            options.Cookie.SecurePolicy = secure;
            options.ExpireTimeSpan = TimeSpan.FromHours(8);
            options.SlidingExpiration = true;
            options.EventsType = typeof(WidaCookieEvents);
        });
        if (access.GoogleConfigured)
        {
            auth.AddOpenIdConnect(GoogleScheme, options =>
            {
                options.Authority = "https://accounts.google.com";
                options.ClientId = configuration["Authentication:Google:ClientId"]!;
                options.ClientSecret = configuration["Authentication:Google:ClientSecret"]!;
                options.SignInScheme = CookieScheme;
                options.CallbackPath = "/api/auth/callback";
                options.ResponseType = OpenIdConnectResponseType.Code;
                options.ResponseMode = OpenIdConnectResponseMode.Query;
                options.UsePkce = true;
                options.MapInboundClaims = false;
                options.SaveTokens = false;
                options.Scope.Clear();
                options.Scope.Add("openid"); options.Scope.Add("email"); options.Scope.Add("profile");
                options.CorrelationCookie.Name = "Wida.Correlation.";
                options.CorrelationCookie.Path = "/";
                options.CorrelationCookie.SameSite = SameSiteMode.Lax;
                options.CorrelationCookie.SecurePolicy = secure;
                options.NonceCookie.Name = "Wida.Nonce.";
                options.NonceCookie.Path = "/";
                options.NonceCookie.SameSite = SameSiteMode.Lax;
                options.NonceCookie.SecurePolicy = secure;
                options.Events = new OpenIdConnectEvents
                {
                    OnRedirectToIdentityProvider = context =>
                    {
                        context.ProtocolMessage.RedirectUri = access.CallbackUrl;
                        context.ProtocolMessage.Prompt = "select_account";
                        return Task.CompletedTask;
                    },
                    OnAuthorizationCodeReceived = context =>
                    {
                        if (context.TokenEndpointRequest is not null) context.TokenEndpointRequest.RedirectUri = access.CallbackUrl;
                        return Task.CompletedTask;
                    },
                    OnTicketReceived = async context =>
                    {
                        var accountService = context.HttpContext.RequestServices.GetRequiredService<GoogleAccountService>();
                        var principal = await accountService.SignInAsync(context.Principal!, context.HttpContext.RequestAborted);
                        if (principal is null)
                        {
                            context.HandleResponse();
                            context.Response.Redirect(access.LoginError("not_invited"));
                        }
                        else context.Principal = principal;
                    },
                    OnRemoteFailure = context =>
                    {
                        context.HandleResponse();
                        context.Response.Redirect(access.LoginError("authentication_failed"));
                        return Task.CompletedTask;
                    },
                    OnAccessDenied = context =>
                    {
                        context.HandleResponse();
                        context.Response.Redirect(access.LoginError("authentication_failed"));
                        return Task.CompletedTask;
                    }
                };
            });
        }
        services.AddAuthorizationBuilder().SetFallbackPolicy(new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build());
        return services;
    }
}
