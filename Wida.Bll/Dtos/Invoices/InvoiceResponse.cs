namespace Wida.Bll.Dtos.Invoices;

public record InvoiceResponse(
    Guid Id,
    Guid DocumentId,
    string? SupplierName,
    string? SupplierAddress,
    string? SupplierTaxId,
    string? InvoiceNumber,
    DateOnly? InvoiceDate,
    DateOnly? DueDate,
    string? PurchaseOrderNumber,
    string? Currency,
    decimal? SubtotalAmount,
    decimal? ShippingAmount,
    decimal? DiscountAmount,
    decimal? TaxAmount,
    decimal? TotalAmount,
    IReadOnlyList<InvoiceLineResponse> Lines,
    DateTime CreatedAt,
    DateTime UpdatedAt
);