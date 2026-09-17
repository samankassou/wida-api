using Microsoft.EntityFrameworkCore;
using Wida.Dal.Entities;
using Wida.Dal.Models;
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

    public async Task<IReadOnlyList<Invoice>> FindDuplicatesAsync(string supplierName, string invoiceNumber,
        Guid documentId, CancellationToken cancellationToken = default)
    {
        var supplier = InvoiceIdentity.Normalize(supplierName);
        var number = InvoiceIdentity.Normalize(invoiceNumber);
        if (supplier.Length == 0 || number.Length == 0) return [];
        List<Invoice> matches = [];
        // Query filters scope this scan to the current owner, including for admins.
        // Stream only headers so checks cover records outside the workspace's 500-row limit.
        var candidates = _context.Invoices.AsNoTracking().Where(x => x.DocumentId != documentId)
            .OrderByDescending(x => x.CreatedAt).ThenBy(x => x.Id)
            .Select(x => new Invoice { Id = x.Id, DocumentId = x.DocumentId, SupplierName = x.SupplierName,
                InvoiceNumber = x.InvoiceNumber, InvoiceDate = x.InvoiceDate, Currency = x.Currency,
                TotalAmount = x.TotalAmount });
        await foreach (var invoice in candidates.AsAsyncEnumerable().WithCancellation(cancellationToken))
        {
            if (InvoiceIdentity.Normalize(invoice.SupplierName) != supplier
                || InvoiceIdentity.Normalize(invoice.InvoiceNumber) != number) continue;
            matches.Add(invoice);
            if (matches.Count == 10) break;
        }
        return matches;
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
