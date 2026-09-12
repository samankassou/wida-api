using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Wida.Bll.Services.Implementations;
using Wida.Dal.Enums;
using Wida.Dal.Persistence;

namespace Wida.Api.Controllers;

[ApiController]
[Authorize(Roles = "Admin")]
[Route("api/admin")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class AdminController(WidaDbContext db) : ControllerBase
{
    [HttpGet("metrics")]
    public async Task<IActionResult> Metrics(CancellationToken token)
    {
        // Global aggregates are intentionally restricted to this admin controller.
        var users = await db.Users.CountAsync(token);
        var creditRequests = await db.Users.CountAsync(x => x.CreditRequestedAt != null && x.Role != UserRole.Admin, token);
        var documents = await db.Documents.IgnoreQueryFilters().CountAsync(token);
        var invoices = await db.Invoices.IgnoreQueryFilters().CountAsync(token);
        var runs = await db.ProcessingRuns.IgnoreQueryFilters().GroupBy(x => x.Status)
            .Select(g => new { Status = g.Key, Count = g.Count() }).ToListAsync(token);
        var month = TrialBudget.Month;
        var monthlyPagesUsed = await db.AnalysisBudgets.Where(x => x.Id == month).Select(x => x.PagesUsed).SingleOrDefaultAsync(token);
        return Ok(new { users, creditRequests, documents, invoices,
            analysesCompleted = runs.Where(x => x.Status == ProcessingStatus.Completed).Sum(x => x.Count),
            analysesFailed = runs.Where(x => x.Status == ProcessingStatus.Failed).Sum(x => x.Count),
            analysesActive = runs.Where(x => x.Status == ProcessingStatus.Pending || x.Status == ProcessingStatus.Running).Sum(x => x.Count),
            month, monthlyPagesUsed, monthlyPagesLimit = TrialBudget.MonthlyPages });
    }

    [HttpGet("users")]
    public async Task<IActionResult> Users([FromQuery] string? search, [FromQuery, Range(1, int.MaxValue)] int page = 1, CancellationToken token = default)
    {
        var query = db.Users.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim().ToLowerInvariant();
            query = query.Where(x => x.Email.ToLower().Contains(term) || x.DisplayName.ToLower().Contains(term));
        }
        var total = await query.CountAsync(token);
        var currentPage = Math.Min(page, Math.Max(1, (int)Math.Ceiling(total / 25d)));
        var users = await query.OrderByDescending(x => x.CreditRequestedAt != null).ThenByDescending(x => x.CreatedAt).ThenBy(x => x.Id)
            .Skip((currentPage - 1) * 25).Take(25)
            .Select(x => new { x.Id, x.Email, x.DisplayName, role = x.Role.ToString(), x.CreatedAt,
                pagesGranted = x.AnalysisPagesGranted, pagesUsed = x.AnalysisPagesUsed, x.CreditRequestedAt }).ToListAsync(token);
        return Ok(new { users, total, page = currentPage, pageSize = 25 });
    }

    public sealed class UpdateTrialRequest
    {
        [System.Text.Json.Serialization.JsonRequired, Range(0, 1000000)]
        public int PagesGranted { get; init; }
        public bool ResolveCreditRequest { get; init; }
    }

    [HttpPut("users/{id:guid}/trial")]
    public async Task<IActionResult> UpdateTrial(Guid id, UpdateTrialRequest request, CancellationToken token)
    {
        var user = await db.Users.SingleOrDefaultAsync(x => x.Id == id, token);
        if (user is null) return NotFound();
        if (user.Role == UserRole.Admin) return BadRequest(new { detail = "Administrator accounts have no trial limit." });
        // EF updates only these properties, preserving concurrent consumption reservations.
        user.AnalysisPagesGranted = request.PagesGranted;
        if (request.ResolveCreditRequest) user.CreditRequestedAt = null;
        await db.SaveChangesAsync(token);
        return Ok(new { pagesGranted = user.AnalysisPagesGranted });
    }
}
