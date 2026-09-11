using System.Net.Mail;

namespace Wida.Api.Authentication;

public sealed class PilotAccess(IConfiguration configuration, IWebHostEnvironment environment)
{
    public bool GoogleConfigured => !string.IsNullOrWhiteSpace(configuration["Authentication:Google:ClientId"])
        && !string.IsNullOrWhiteSpace(configuration["Authentication:Google:ClientSecret"]);

    public string PublicOrigin
    {
        get
        {
            var value = configuration["Authentication:PublicOrigin"]
                ?? (environment.IsDevelopment() ? "http://localhost:3000" : null);
            if (!Uri.TryCreate(value, UriKind.Absolute, out var uri)
                || !string.IsNullOrEmpty(uri.UserInfo) || uri.AbsolutePath != "/"
                || !string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment)
                || (uri.Scheme != "https" && !(environment.IsDevelopment() && uri.Scheme == "http" && uri.IsLoopback)))
                throw new InvalidOperationException("Configure Authentication:PublicOrigin as the frontend HTTPS origin (loopback HTTP is allowed in Development).");
            return uri.GetLeftPart(UriPartial.Authority);
        }
    }

    public string CallbackUrl => PublicOrigin + "/api/wida/auth/callback";

    public bool IsConfiguredAdmin(string? email) => !string.IsNullOrWhiteSpace(email)
        && string.Equals(email.Trim(), configuration["Authentication:AdminEmail"]?.Trim(), StringComparison.OrdinalIgnoreCase);

    public bool IsInvited(string? email)
    {
        if (string.IsNullOrWhiteSpace(email) || !MailAddress.TryCreate(email, out var address)
            || !string.Equals(address.Address, email.Trim(), StringComparison.OrdinalIgnoreCase)) return false;
        if (IsConfiguredAdmin(email) || configuration.GetValue("Authentication:PublicBeta", false)) return true;
        return configuration.GetSection("Authentication:AllowedEmails").Get<string[]>()?
            .Any(invited => string.Equals(invited?.Trim(), email.Trim(), StringComparison.OrdinalIgnoreCase)) == true;
    }

    // Only workspace URLs are accepted; never redirect to a supplied host or scheme.
    public string ReturnUrl(string? path) => PublicOrigin +
        (path is not null && (path == "/" || path.StartsWith("/?", StringComparison.Ordinal))
            && !path.Any(char.IsControl) && !path.Contains('\\') ? path : "/");

    public string LoginError(string code) => PublicOrigin + "/login?error=" + Uri.EscapeDataString(code);
}
