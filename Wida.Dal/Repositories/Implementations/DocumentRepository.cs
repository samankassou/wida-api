using Microsoft.EntityFrameworkCore;
using Wida.Dal.Entities;
using Wida.Dal.Persistence;
using Wida.Dal.Repositories.Interfaces;

namespace Wida.Dal.Repositories.Implementations;

public class DocumentRepository : IDocumentRepository
{
    private readonly WidaDbContext _context;

    public DocumentRepository(WidaDbContext context)
    {
        _context = context;
    }

    public Task<Document?> GetByIdAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        return _context.Documents
            .FirstOrDefaultAsync(
                x => x.Id == id,
                cancellationToken);
    }

    public async Task<IReadOnlyList<Document>> GetAllAsync(
        CancellationToken cancellationToken = default)
    {
        return await _context.Documents
            .AsNoTracking()
            .OrderByDescending(x => x.UploadedAt)
            .ToListAsync(cancellationToken);
    }

    public Task AddAsync(
        Document document,
        CancellationToken cancellationToken = default)
    {
        return _context.Documents
            .AddAsync(document, cancellationToken)
            .AsTask();
    }

    public async Task<IReadOnlyList<Document>> GetWorkspaceAsync(
        int limit,
        CancellationToken cancellationToken = default)
    {
        return await _context.Documents
            .AsNoTracking()
            .OrderByDescending(document => document.UploadedAt)
            .ThenBy(document => document.Id)
            .Take(limit)
            .Include(document => document.Invoice)
                .ThenInclude(invoice => invoice!.Lines)
            .Include(document => document.ProcessingRuns
                .OrderByDescending(run => run.StartedAt)
                .ThenByDescending(run => run.Id)
                .Take(1))
                .ThenInclude(run => run.ExtractedFields)
            .AsSplitQuery()
            .ToListAsync(cancellationToken);
    }

    public Task SaveChangesAsync(
        CancellationToken cancellationToken = default)
    {
        return _context.SaveChangesAsync(cancellationToken);
    }
}
