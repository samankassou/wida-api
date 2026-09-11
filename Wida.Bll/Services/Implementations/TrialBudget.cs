using Microsoft.EntityFrameworkCore;
using Wida.Bll.Exceptions;
using Wida.Dal.Entities;
using Wida.Dal.Persistence;

namespace Wida.Bll.Services.Implementations;

public static class TrialBudget
{
    public const int MonthlyPages = 400;
    public static string Month => DateTime.UtcNow.ToString("yyyy-MM", System.Globalization.CultureInfo.InvariantCulture);

    // Caller holds the shared admission transaction lock until commit.
    public static async Task ReserveMonthAsync(WidaDbContext db, int pages, CancellationToken token, bool enforceLimit = true)
    {
        var month = Month;
        var budget = await db.AnalysisBudgets.SingleOrDefaultAsync(x => x.Id == month, token);
        if (enforceLimit && (budget?.PagesUsed ?? 0) + pages > MonthlyPages)
            throw new TrialLimitException("Le budget mensuel de la bêta est épuisé. La consultation et la saisie manuelle restent disponibles.");
        if (budget is null) { budget = new AnalysisBudget { Id = month }; db.AnalysisBudgets.Add(budget); }
        budget.PagesUsed += pages;
    }
}
