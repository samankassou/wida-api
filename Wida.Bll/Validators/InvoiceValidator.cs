using Wida.Bll.Dtos.Invoices;

namespace Wida.Bll.Validators;

public static class InvoiceValidator
{
    public static IReadOnlyList<string> Validate(CreateInvoiceRequest invoice)
    {
        var issues = new List<string>();

        if (string.IsNullOrWhiteSpace(invoice.SupplierName))
        {
            issues.Add("Supplier name is required.");
        }

        if (string.IsNullOrWhiteSpace(invoice.InvoiceNumber))
        {
            issues.Add("Invoice number is required.");
        }

        if (invoice.InvoiceDate is null)
        {
            issues.Add("Invoice date is required.");
        }

        if (invoice.TotalAmount is null)
        {
            issues.Add("Total amount is required.");
        }

        if (
            invoice.InvoiceDate is not null &&
            invoice.DueDate is not null &&
            invoice.DueDate < invoice.InvoiceDate)
        {
            issues.Add("Due date cannot be earlier than invoice date.");
        }

        if (
            invoice.SubtotalAmount is not null &&
            invoice.TaxAmount is not null &&
            invoice.TotalAmount is not null)
        {
            var expectedTotal =
                invoice.SubtotalAmount.Value +
                invoice.TaxAmount.Value;

            if (!AreAmountsEqual(expectedTotal, invoice.TotalAmount.Value))
            {
                issues.Add(
                    "Subtotal + tax amount does not match total amount.");
            }
        }

        foreach (var line in invoice.Lines)
        {
            if (
                line.Quantity is not null &&
                line.UnitPrice is not null &&
                line.LineAmount is not null)
            {
                var expectedLineAmount =
                    line.Quantity.Value *
                    line.UnitPrice.Value;

                if (!AreAmountsEqual(
                        expectedLineAmount,
                        line.LineAmount.Value))
                {
                    issues.Add(
                        $"Invoice line {line.LineNumber ?? 0}: " +
                        "quantity × unit price does not match line amount.");
                }
            }
        }

        if (
            invoice.SubtotalAmount is not null &&
            invoice.Lines.Count > 0 &&
            invoice.Lines.All(x => x.LineAmount is not null))
        {
            var linesTotal = invoice.Lines
                .Sum(x => x.LineAmount!.Value);

            if (!AreAmountsEqual(
                    linesTotal,
                    invoice.SubtotalAmount.Value))
            {
                issues.Add(
                    "Sum of invoice lines does not match subtotal amount.");
            }
        }

        return issues;
    }

    private static bool AreAmountsEqual(
        decimal first,
        decimal second)
    {
        const decimal tolerance = 0.01m;

        return Math.Abs(first - second) <= tolerance;
    }
}