namespace Wida.Dal.Entities;

public class InvoiceLine
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid InvoiceId { get; set; }

    public Invoice Invoice { get; set; } = null!;

    public int? LineNumber { get; set; }

    public string? Description { get; set; }

    public decimal? Quantity { get; set; }

    public string? UnitOfMeasure { get; set; }

    public decimal? UnitPrice { get; set; }

    public decimal? TaxRate { get; set; }

    public decimal? TaxAmount { get; set; }

    public decimal? LineAmount { get; set; }
}