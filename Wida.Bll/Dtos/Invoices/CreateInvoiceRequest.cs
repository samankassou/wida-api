namespace Wida.Bll.Dtos.Invoices;

public class CreateInvoiceRequest
{
    public Guid DocumentId { get; set; }

    public string? SupplierName { get; set; }

    public string? SupplierAddress { get; set; }

    public string? SupplierTaxId { get; set; }

    public string? InvoiceNumber { get; set; }

    public DateOnly? InvoiceDate { get; set; }

    public DateOnly? DueDate { get; set; }

    public string? PurchaseOrderNumber { get; set; }

    public string? Currency { get; set; }

    public decimal? SubtotalAmount { get; set; }

    public decimal? ShippingAmount { get; set; }

    public decimal? DiscountAmount { get; set; }

    public decimal? TaxAmount { get; set; }

    public decimal? TotalAmount { get; set; }

    public List<CreateInvoiceLineRequest> Lines { get; set; } = [];
}