using Wida.Dal.Entities;

namespace Wida.Dal.Repositories.Interfaces;

public interface IInvoiceRepository
{
    Task<IReadOnlyList<Invoice>> GetAllAsync(CancellationToken cancellationToken = default);

    void ReplaceLines(Invoice invoice, IEnumerable<InvoiceLine> lines);

    Task<Invoice?> GetByIdAsync(
        Guid id,
        CancellationToken cancellationToken = default);

    Task<Invoice?> GetByDocumentIdAsync(
        Guid documentId,
        CancellationToken cancellationToken = default);

    Task AddAsync(
        Invoice invoice,
        CancellationToken cancellationToken = default);

    Task SaveChangesAsync(
        CancellationToken cancellationToken = default);
}
