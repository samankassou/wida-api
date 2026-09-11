using System.Security.Claims;
using Wida.Dal.Enums;
using Microsoft.EntityFrameworkCore;
using Wida.Dal.Entities;
using Wida.Dal.Persistence;

namespace Wida.Api.Authentication;

public sealed class GoogleAccountService(WidaDbContext db, PilotAccess access)
{
    public async Task<ClaimsPrincipal?> SignInAsync(ClaimsPrincipal google, CancellationToken cancellationToken)
    {
        // The OIDC handler validates issuer, audience, signature, lifetime, nonce and state first.
        var subject = google.FindFirstValue("sub");
        var email = google.FindFirstValue("email");
        if (string.IsNullOrWhiteSpace(subject) || subject.Length > 255
            || !bool.TryParse(google.FindFirstValue("email_verified"), out var verified) || !verified
            || string.IsNullOrWhiteSpace(email)) return null;

        var user = await db.Users.SingleOrDefaultAsync(x => x.GoogleSubject == subject, cancellationToken);
        if (user?.Role != UserRole.Admin && !access.IsInvited(email)) return null;
        if (user is null)
        {
            user = new AppUser { GoogleSubject = subject, Email = email!.Trim().ToLowerInvariant(),
                DisplayName = DisplayName(google, email!), CreatedAt = DateTime.UtcNow };
            db.Users.Add(user);
            try { await db.SaveChangesAsync(cancellationToken); }
            catch (DbUpdateException)
            {
                // Parallel first logins must converge on the same provider identity.
                db.Entry(user).State = EntityState.Detached;
                user = await db.Users.SingleOrDefaultAsync(x => x.GoogleSubject == subject, cancellationToken);
                if (user is null) throw;
            }
        }
        if (access.IsConfiguredAdmin(email)) user.Role = UserRole.Admin;
        user.Email = email!.Trim().ToLowerInvariant();
        user.DisplayName = DisplayName(google, email);
        await db.SaveChangesAsync(cancellationToken);

        return new ClaimsPrincipal(new ClaimsIdentity([
            new Claim(ClaimTypes.Role, user.Role.ToString()),
            new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new Claim(ClaimTypes.Email, user.Email),
            new Claim(ClaimTypes.Name, user.DisplayName)
        ], WidaAuthentication.CookieScheme));
    }

    private static string DisplayName(ClaimsPrincipal principal, string email)
    {
        var name = principal.FindFirstValue("name")?.Trim();
        var value = string.IsNullOrEmpty(name) ? email : name;
        return value[..Math.Min(value.Length, 200)];
    }
}
