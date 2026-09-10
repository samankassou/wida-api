namespace Wida.Dal.Entities;

public class Invoice
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid DocumentId { get; set; }

    public Document Document { get; set; } = null!;

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

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public ICollection<InvoiceLine> Lines { get; set; } = [];
}