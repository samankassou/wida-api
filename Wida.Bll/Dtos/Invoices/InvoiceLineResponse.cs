namespace Wida.Bll.Dtos.Invoices;

public record InvoiceLineResponse(
    Guid Id,
    int? LineNumber,
    string? Description,
    decimal? Quantity,
    string? UnitOfMeasure,
    decimal? UnitPrice,
    decimal? TaxRate,
    decimal? TaxAmount,
    decimal? LineAmount
);