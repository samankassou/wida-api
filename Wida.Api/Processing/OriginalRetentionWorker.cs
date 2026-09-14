using Microsoft.EntityFrameworkCore;
using Wida.Dal.Persistence;
using Wida.Dal.Enums;
using Wida.Dal.Storage;

namespace Wida.Api.Processing;

// Only originals expire: invoice records, credit counters and audit history survive.
public sealed class OriginalRetentionWorker(IServiceScopeFactory scopes,
    ILogger<OriginalRetentionWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            try
            {
                using var scope = scopes.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<WidaDbContext>();
                await using var transaction = await db.Database.BeginTransactionAsync(token);
                if (db.Database.IsNpgsql())
                    await db.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(73190421)", token);
                var cutoff = DateTime.UtcNow.AddDays(-30);
                var originals = await db.Documents.IgnoreQueryFilters().AsNoTracking()
                    .Where(x => x.OwnerUser.Role != UserRole.Admin && x.UploadedAt <= cutoff && !db.ProcessingRuns.IgnoreQueryFilters().Any(r =>
                        r.DocumentId == x.Id && r.IsBackgroundJob && (r.Status == ProcessingStatus.Pending || r.Status == ProcessingStatus.Running)))
                    .Select(x => x.StoragePath).ToListAsync(token);
                var storage = scope.ServiceProvider.GetRequiredService<IDocumentStorage>();
                foreach (var path in originals)
                    await storage.DeleteAsync(path, token);
                await transaction.CommitAsync(token);
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { break; }
            catch (Exception ex) { logger.LogError(ex, "Original retention cleanup failed; will retry."); }
            await Task.Delay(TimeSpan.FromHours(1), token);
        }
    }
}
