namespace Wida.Bll.Dtos.Invoices;

public class CreateInvoiceLineRequest
{
    public int? LineNumber { get; set; }

    public string? Description { get; set; }

    public decimal? Quantity { get; set; }

    public string? UnitOfMeasure { get; set; }

    public decimal? UnitPrice { get; set; }

    public decimal? TaxRate { get; set; }

    public decimal? TaxAmount { get; set; }

    public decimal? LineAmount { get; set; }
}