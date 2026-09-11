using Microsoft.EntityFrameworkCore;
using Wida.Dal.Persistence;
using Wida.Dal.Enums;

namespace Wida.Api.Processing;

// Only originals expire: invoice records, credit counters and audit history survive.
public sealed class OriginalRetentionWorker(IServiceScopeFactory scopes, IWebHostEnvironment environment,
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
                var uploads = Path.GetFullPath(Path.Combine(environment.ContentRootPath, "uploads"));
                foreach (var path in originals)
                {
                    token.ThrowIfCancellationRequested();
                    if (!Path.IsPathFullyQualified(path) || Path.GetDirectoryName(Path.GetFullPath(path)) != uploads) continue;
                    var info = new FileInfo(path);
                    if (info.Exists && info.LinkTarget is null) info.Delete();
                }
                await transaction.CommitAsync(token);
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { break; }
            catch (Exception ex) { logger.LogError(ex, "Original retention cleanup failed; will retry."); }
            await Task.Delay(TimeSpan.FromHours(1), token);
        }
    }
}
