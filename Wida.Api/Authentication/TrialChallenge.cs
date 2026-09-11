using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Wida.Bll.Exceptions;

namespace Wida.Api.Authentication;

public sealed class TrialChallenge(IConfiguration configuration, IDataProtectionProvider protection,
    IHttpClientFactory clients, PilotAccess access)
{
    public string? SiteKey => configuration["Turnstile:SiteKey"];
    public bool Enabled => !string.IsNullOrWhiteSpace(SiteKey) || !string.IsNullOrWhiteSpace(configuration["Turnstile:SecretKey"]);
    private IDataProtector Protector => protection.CreateProtector("Wida.TrialChallenge.v1");

    public bool IsVerified(HttpContext context)
    {
        if (context.User.IsInRole("Admin") || !Enabled) return true;
        try
        {
            var value = Protector.Unprotect(context.Request.Cookies["Wida.Challenge"] ?? "").Split('|');
            return value.Length == 2 && value[0] == context.User.FindFirstValue(ClaimTypes.NameIdentifier)
                && long.TryParse(value[1], out var expires) && expires > DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        }
        catch (System.Security.Cryptography.CryptographicException) { return false; }
    }

    public async Task VerifyAsync(HttpContext context, string token, CancellationToken cancellationToken)
    {
        if (!Enabled) return;
        if (string.IsNullOrWhiteSpace(token) || token.Length > 2048)
            throw new TrialLimitException("Complétez la vérification anti-robot.", 403);
        using var client = clients.CreateClient();
        client.Timeout = TimeSpan.FromSeconds(15);
        using var response = await client.PostAsync("https://challenges.cloudflare.com/turnstile/v0/siteverify",
            new FormUrlEncodedContent(new Dictionary<string, string> {
                ["secret"] = configuration["Turnstile:SecretKey"] ?? "", ["response"] = token
            }), cancellationToken);
        response.EnsureSuccessStatusCode();
        using var result = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
        var root = result.RootElement;
        if (!root.TryGetProperty("success", out var success) || !success.GetBoolean()
            || !root.TryGetProperty("action", out var action) || action.GetString() != "trial"
            || !root.TryGetProperty("hostname", out var hostname) || hostname.GetString() != new Uri(access.PublicOrigin).Host)
            throw new TrialLimitException("La vérification a expiré ou échoué. Réessayez.", 403);
        var expires = DateTimeOffset.UtcNow.AddMinutes(10);
        var value = context.User.FindFirstValue(ClaimTypes.NameIdentifier) + "|" + expires.ToUnixTimeSeconds();
        context.Response.Cookies.Append("Wida.Challenge", Protector.Protect(value), new CookieOptions {
            HttpOnly = true, Secure = context.Request.IsHttps, SameSite = SameSiteMode.Lax, Path = "/", Expires = expires
        });
    }
}
