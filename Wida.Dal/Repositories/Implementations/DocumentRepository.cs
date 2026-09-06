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
            .AsNoTracking()
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

    public Task SaveChangesAsync(
        CancellationToken cancellationToken = default)
    {
        return _context.SaveChangesAsync(cancellationToken);
    }
}