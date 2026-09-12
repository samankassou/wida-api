using Microsoft.EntityFrameworkCore;
using Wida.Dal.Entities;
using Wida.Dal.Persistence;
using Wida.Dal.Repositories.Interfaces;

namespace Wida.Dal.Repositories.Implementations;

public class ProcessingRunRepository : IProcessingRunRepository
{
    private readonly WidaDbContext _context;

    public ProcessingRunRepository(WidaDbContext context)
    {
        _context = context;
    }

    public async Task<ProcessingRun> GetOrCreateManualAsync(ProcessingRun run, CancellationToken cancellationToken = default)
    {
        if (run.IsBackgroundJob) throw new ArgumentException("Expected a manual run.", nameof(run));
        await using var transaction = await _context.Database.BeginTransactionAsync(cancellationToken);
        // Serialize across API replicas, using the same admission lock as uploads/queueing.
        // Query after acquiring the lock so concurrent requests see the committed winner.
        if (_context.Database.IsNpgsql())
            await _context.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(73190421)", cancellationToken);
        var existing = await _context.ProcessingRuns.Include(x => x.ExtractedFields)
            .Where(x => x.DocumentId == run.DocumentId && !x.IsBackgroundJob && x.Processor == run.Processor)
            .OrderBy(x => x.StartedAt).ThenBy(x => x.Id).FirstOrDefaultAsync(cancellationToken);
        if (existing is null)
        {
            _context.ProcessingRuns.Add(run);
            await _context.SaveChangesAsync(cancellationToken);
        }
        await transaction.CommitAsync(cancellationToken);
        return existing ?? run;
    }

    public Task<ProcessingRun?> GetByIdAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        return _context.ProcessingRuns
            .Include(x => x.ExtractedFields)
            .FirstOrDefaultAsync(
                x => x.Id == id,
                cancellationToken);
    }

    public async Task<IReadOnlyList<ProcessingRun>> GetByDocumentIdAsync(
        Guid documentId,
        CancellationToken cancellationToken = default)
    {
        return await _context.ProcessingRuns
            .Include(x => x.ExtractedFields)
            .Where(x => x.DocumentId == documentId)
            .OrderByDescending(x => x.StartedAt)
            .ToListAsync(cancellationToken);
    }

    public Task AddAsync(
        ProcessingRun processingRun,
        CancellationToken cancellationToken = default)
    {
        return _context.ProcessingRuns
            .AddAsync(processingRun, cancellationToken)
            .AsTask();
    }

    public Task SaveChangesAsync(
        CancellationToken cancellationToken = default)
    {
        return _context.SaveChangesAsync(cancellationToken);
    }

    public void AddExtractedFields(IEnumerable<ExtractedField> extractedFields)
    {
        // These fields have application-assigned IDs and belong to an already saved run.
        // Explicitly mark them Added so EF does not infer that they already exist.
        _context.ExtractedFields.AddRange(extractedFields);
    }
}
