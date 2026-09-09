using System.Security.Claims;
using Wida.Dal.Services.Interfaces;

namespace Wida.Api.Authentication;

public sealed class HttpCurrentUser(IHttpContextAccessor accessor) : ICurrentUser
{
    public Guid? UserId => accessor.HttpContext?.User.Identity?.IsAuthenticated == true
        && Guid.TryParse(accessor.HttpContext.User.FindFirstValue(ClaimTypes.NameIdentifier), out var id)
        ? id : null;
}
