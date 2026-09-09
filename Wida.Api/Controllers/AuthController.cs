using System.Security.Claims;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Wida.Api.Authentication;

namespace Wida.Api.Controllers;

[ApiController]
[Route("api/auth")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class AuthController(PilotAccess access, IAntiforgery antiforgery) : ControllerBase
{
    [AllowAnonymous]
    [HttpGet("session")]
    public IActionResult Session()
    {
        var authenticated = User.Identity?.IsAuthenticated == true;
        var token = antiforgery.GetAndStoreTokens(HttpContext);
        return Ok(new
        {
            authenticated,
            googleConfigured = access.GoogleConfigured,
            user = authenticated ? new { id = User.FindFirstValue(ClaimTypes.NameIdentifier), email = User.FindFirstValue(ClaimTypes.Email), displayName = User.FindFirstValue(ClaimTypes.Name) } : null,
            csrfToken = token.RequestToken
        });
    }

    [AllowAnonymous]
    [HttpGet("login")]
    public IActionResult Login([FromQuery] string? returnUrl = null)
    {
        if (!access.GoogleConfigured) return Redirect(access.LoginError("configuration"));
        return Challenge(new AuthenticationProperties { RedirectUri = access.ReturnUrl(returnUrl), IsPersistent = false }, WidaAuthentication.GoogleScheme);
    }

    [HttpPost("logout")]
    public async Task<IActionResult> Logout()
    {
        await HttpContext.SignOutAsync(WidaAuthentication.CookieScheme);
        Response.Cookies.Delete("Wida.Antiforgery", new CookieOptions { Path = "/" });
        return Ok(new { signedOut = true });
    }
}
