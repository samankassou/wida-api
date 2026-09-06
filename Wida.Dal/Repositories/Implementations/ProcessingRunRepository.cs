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
}