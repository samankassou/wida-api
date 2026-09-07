using Wida.Bll.Dtos.Invoices;

namespace Wida.Bll.Services.Interfaces;

public interface IInvoiceService
{
    Task<IReadOnlyList<InvoiceResponse>> GetAllAsync(CancellationToken cancellationToken = default);

    Task<InvoiceResponse> UpdateAsync(Guid id, CreateInvoiceRequest request,
        CancellationToken cancellationToken = default);

    Task<InvoiceResponse?> GetByIdAsync(
        Guid id,
        CancellationToken cancellationToken = default);

    Task<InvoiceResponse?> GetByDocumentIdAsync(
        Guid documentId,
        CancellationToken cancellationToken = default);

    Task<InvoiceResponse> CreateAsync(
        CreateInvoiceRequest request,
        CancellationToken cancellationToken = default);
}
