using Microsoft.EntityFrameworkCore;
using Wida.Dal.Entities;
using Wida.Dal.Persistence;
using Wida.Dal.Repositories.Interfaces;

namespace Wida.Dal.Repositories.Implementations;

public class InvoiceRepository : IInvoiceRepository
{
    private readonly WidaDbContext _context;

    public InvoiceRepository(WidaDbContext context)
    {
        _context = context;
    }

    public Task<Invoice?> GetByIdAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        return _context.Invoices
            .Include(x => x.Lines)
            .FirstOrDefaultAsync(
                x => x.Id == id,
                cancellationToken);
    }

    public Task<Invoice?> GetByDocumentIdAsync(
        Guid documentId,
        CancellationToken cancellationToken = default)
    {
        return _context.Invoices
            .Include(x => x.Lines)
            .FirstOrDefaultAsync(
                x => x.DocumentId == documentId,
                cancellationToken);
    }

    public Task AddAsync(
        Invoice invoice,
        CancellationToken cancellationToken = default)
    {
        return _context.Invoices
            .AddAsync(invoice, cancellationToken)
            .AsTask();
    }

    public Task SaveChangesAsync(
        CancellationToken cancellationToken = default)
    {
        return _context.SaveChangesAsync(cancellationToken);
    }
}