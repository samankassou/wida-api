namespace Wida.Bll.Dtos.Invoices;

public record DuplicateInvoiceResponse(Guid Id, Guid DocumentId, string? SupplierName,
    string? InvoiceNumber, DateOnly? InvoiceDate, string? Currency, decimal? TotalAmount);
