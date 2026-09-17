using Wida.Bll.Dtos.Invoices;

namespace Wida.Bll.Exceptions;

public sealed class DuplicateInvoiceException(IReadOnlyList<DuplicateInvoiceResponse> matches)
    : Exception("A saved invoice has the same supplier and invoice number. Review it before saving.")
{
    public IReadOnlyList<DuplicateInvoiceResponse> Matches { get; } = matches;
}
