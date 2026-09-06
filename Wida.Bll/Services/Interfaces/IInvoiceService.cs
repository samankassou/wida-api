using Wida.Bll.Dtos.Invoices;

namespace Wida.Bll.Services.Interfaces;

public interface IInvoiceService
{
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