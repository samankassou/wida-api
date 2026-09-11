using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Wida.Bll.Services.Implementations;
using Wida.Dal.Persistence;

namespace Wida.Api.Controllers;

[ApiController]
[Route("api/trial")]
public sealed class TrialController(WidaDbContext db, Wida.Api.Authentication.TrialChallenge challenge) : ControllerBase
{
    private Guid UserId => Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    [HttpGet]
    public async Task<IActionResult> Get(CancellationToken token)
    {
        var user = await db.Users.AsNoTracking().SingleAsync(x => x.Id == UserId, token);
        var used = await db.AnalysisBudgets.Where(x => x.Id == TrialBudget.Month).Select(x => x.PagesUsed).SingleOrDefaultAsync(token);
        if (user.Role == Wida.Dal.Enums.UserRole.Admin)
            return Ok(new { role = "Admin", unrestricted = true });
        return Ok(new { role = "User", unrestricted = false, captchaSiteKey = challenge.SiteKey, captchaVerified = challenge.IsVerified(HttpContext), pagesRemaining = Math.Max(0, user.AnalysisPagesGranted - user.AnalysisPagesUsed),
            pagesGranted = user.AnalysisPagesGranted, publicPagesRemaining = Math.Max(0, TrialBudget.MonthlyPages - used),
            creditRequested = user.CreditRequestedAt != null, maximumDocuments = 10, originalRetentionDays = 30 });
    }

    public record ChallengeRequest(string Token);

    [HttpPost("challenge")]
    public async Task<IActionResult> Verify(ChallengeRequest request, CancellationToken token)
    {
        await challenge.VerifyAsync(HttpContext, request.Token, token);
        return Ok(new { verified = true });
    }

    [HttpPost("credits")]
    public async Task<IActionResult> RequestCredits(CancellationToken token)
    {
        var user = await db.Users.SingleAsync(x => x.Id == UserId, token);
        user.CreditRequestedAt ??= DateTime.UtcNow;
        await db.SaveChangesAsync(token);
        return Ok(new { requested = true });
    }
}
