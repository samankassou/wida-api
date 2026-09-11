using System.Security.Claims;
using Wida.Dal.Enums;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.EntityFrameworkCore;
using Wida.Dal.Persistence;

namespace Wida.Api.Authentication;

public sealed class WidaCookieEvents(WidaDbContext db, PilotAccess access) : CookieAuthenticationEvents
{
    public override async Task ValidatePrincipal(CookieValidatePrincipalContext context)
    {
        var validId = Guid.TryParse(context.Principal?.FindFirstValue(ClaimTypes.NameIdentifier), out var userId);
        var user = validId ? await db.Users.AsNoTracking().SingleOrDefaultAsync(x => x.Id == userId, context.HttpContext.RequestAborted) : null;
        if (user is null || (user.Role != UserRole.Admin && !access.IsInvited(user.Email))
            || !string.Equals(user.Email, context.Principal?.FindFirstValue(ClaimTypes.Email), StringComparison.OrdinalIgnoreCase))
        {
            context.RejectPrincipal();
            await context.HttpContext.SignOutAsync(WidaAuthentication.CookieScheme);
            return;
        }
        // Never trust an old role in a long-lived cookie after a promotion/demotion.
        if (context.Principal?.FindFirstValue(ClaimTypes.Role) != user.Role.ToString())
        {
            var identity = (ClaimsIdentity)context.Principal!.Identity!;
            foreach (var claim in identity.FindAll(ClaimTypes.Role).ToList()) identity.RemoveClaim(claim);
            identity.AddClaim(new Claim(ClaimTypes.Role, user.Role.ToString()));
            context.ShouldRenew = true;
        }
    }

    public override Task RedirectToLogin(RedirectContext<CookieAuthenticationOptions> context)
    {
        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        return context.Response.WriteAsJsonAsync(new { title = "Authentication required", detail = "Your session has expired. Sign in to continue." });
    }

    public override Task RedirectToAccessDenied(RedirectContext<CookieAuthenticationOptions> context)
    {
        context.Response.StatusCode = StatusCodes.Status403Forbidden;
        return context.Response.WriteAsJsonAsync(new { title = "Access denied" });
    }
}
