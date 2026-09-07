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

    public async Task<IReadOnlyList<Invoice>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        return await _context.Invoices
            .AsNoTracking()
            .Include(invoice => invoice.Lines)
            .OrderByDescending(invoice => invoice.CreatedAt)
            .ThenBy(invoice => invoice.Id)
            .AsSplitQuery()
            .ToListAsync(cancellationToken);
    }

    public void ReplaceLines(Invoice invoice, IEnumerable<InvoiceLine> lines)
    {
        _context.InvoiceLines.RemoveRange(invoice.Lines);
        invoice.Lines = lines.ToList();
        // Assigned GUIDs on replacement lines must be inserted, not treated as updates.
        _context.InvoiceLines.AddRange(invoice.Lines);
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
