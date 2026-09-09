using System.Security.Claims;
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
        if (user is null || !access.IsInvited(user.Email)
            || !string.Equals(user.Email, context.Principal?.FindFirstValue(ClaimTypes.Email), StringComparison.OrdinalIgnoreCase))
        {
            context.RejectPrincipal();
            await context.HttpContext.SignOutAsync(WidaAuthentication.CookieScheme);
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
