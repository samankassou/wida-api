using Wida.Bll.Dtos.Invoices;

namespace Wida.Bll.Validators;

public static class InvoiceValidator
{
    public static IReadOnlyList<string> Validate(CreateInvoiceRequest invoice) =>
        ValidateFields(invoice).Values.SelectMany(messages => messages).ToList();

    public static IReadOnlyDictionary<string, string[]> ValidateFields(CreateInvoiceRequest invoice)
    {
        var errors = new Dictionary<string, List<string>>();
        void Add(string field, string message)
        {
            if (!errors.TryGetValue(field, out var messages)) errors[field] = messages = [];
            messages.Add(message);
        }
        void Text(string field, string? value, int maximum, bool required = false)
        {
            if (required && string.IsNullOrWhiteSpace(value)) Add(field, "This field is required.");
            if (value?.Length > maximum) Add(field, $"Use {maximum} characters or fewer.");
        }
        void Number(string field, decimal? value, decimal maximum = 99999999999999.9999m)
        {
            if (value is not { } amount) return;
            if (amount < -maximum || amount > maximum) Add(field, "This number exceeds the supported range.");
            if (decimal.Round(amount, 4) != amount) Add(field, "Use no more than 4 decimal places.");
        }

        if (invoice.DocumentId == Guid.Empty) Add("documentId", "Choose an uploaded document.");
        Text("supplierName", invoice.SupplierName, 255, true);
        Text("supplierAddress", invoice.SupplierAddress, 500);
        Text("supplierTaxId", invoice.SupplierTaxId, 100);
        Text("invoiceNumber", invoice.InvoiceNumber, 100, true);
        Text("purchaseOrderNumber", invoice.PurchaseOrderNumber, 100);
        Text("currency", invoice.Currency, 3);
        if (invoice.InvoiceDate is null) Add("invoiceDate", "Invoice date is required.");
        if (invoice.TotalAmount is null) Add("totalAmount", "Total amount is required.");
        Number("subtotalAmount", invoice.SubtotalAmount);
        Number("shippingAmount", invoice.ShippingAmount);
        Number("discountAmount", invoice.DiscountAmount);
        Number("taxAmount", invoice.TaxAmount);
        Number("totalAmount", invoice.TotalAmount);
        if (invoice.InvoiceDate is { } date && invoice.DueDate is { } due && due < date)
            Add("dueDate", "Due date cannot be earlier than invoice date.");

        if (invoice.Lines is null)
            Add("lines", "Provide an array of invoice lines, or an empty array.");
        else
            for (var index = 0; index < invoice.Lines.Count; index++)
            {
                var line = invoice.Lines[index];
                var prefix = $"lines[{index}]";
                if (line is null) { Add(prefix, "Provide an invoice line object."); continue; }
                Text($"{prefix}.description", line.Description, 1000);
                Text($"{prefix}.unitOfMeasure", line.UnitOfMeasure, 50);
                Number($"{prefix}.quantity", line.Quantity);
                Number($"{prefix}.unitPrice", line.UnitPrice);
                Number($"{prefix}.taxRate", line.TaxRate, 9999.9999m);
                Number($"{prefix}.taxAmount", line.TaxAmount);
                Number($"{prefix}.lineAmount", line.LineAmount);
                if (line.Quantity is { } quantity && line.UnitPrice is { } price && line.LineAmount is { } amount)
                {
                    try
                    {
                        if (!AreAmountsEqual(quantity * price, amount) && TaxInclusiveLineNet(line) is null)
                            Add($"{prefix}.lineAmount", "Quantity × unit price does not match line amount, with or without the specified tax.");
                    }
                    catch (OverflowException) { Add($"{prefix}.lineAmount", "Quantity × unit price exceeds the supported range."); }
                }
            }

        try
        {
            if (invoice.SubtotalAmount is { } subtotal && invoice.TaxAmount is { } tax && invoice.TotalAmount is { } total
                && !AreAmountsEqual(subtotal + tax + (invoice.ShippingAmount ?? 0m) - (invoice.DiscountAmount ?? 0m), total))
                Add("totalAmount", "Subtotal + tax + shipping − discount does not match total amount.");
            if (invoice.SubtotalAmount is { } lineSubtotal && invoice.Lines is { Count: > 0 } lines
                && lines.All(line => line?.LineAmount is not null)
                && !AreAmountsEqual(lines.Sum(line => TaxInclusiveLineNet(line) ?? line.LineAmount!.Value), lineSubtotal))
                Add("subtotalAmount", "Sum of invoice lines does not match subtotal amount.");
        }
        catch (OverflowException) { Add("totalAmount", "The combined amounts exceed the supported range."); }

        return errors.ToDictionary(pair => pair.Key, pair => pair.Value.ToArray());
    }

    private static decimal? TaxInclusiveLineNet(CreateInvoiceLineRequest line)
    {
        if (line.Quantity is not { } quantity || line.UnitPrice is not { } price || line.LineAmount is not { } amount)
            return null;
        var net = quantity * price;
        if (AreAmountsEqual(net, amount)) return null;
        if (line.TaxAmount is null && line.TaxRate is null) return null;
        if (line.TaxAmount is { } tax && !AreAmountsEqual(net + tax, amount)) return null;
        if (line.TaxRate is { } rate && !AreAmountsEqual(net + net * rate / 100m, amount)) return null;
        return net;
    }

    private static bool AreAmountsEqual(decimal first, decimal second) => Math.Abs(first - second) <= 0.01m;
}
