using Wida.Bll.Dtos.Invoices;
using Wida.Bll.Services.Interfaces;
using Wida.Dal.Entities;
using Wida.Dal.Repositories.Interfaces;
using Wida.Bll.Validators;

namespace Wida.Bll.Services.Implementations;

public class InvoiceService : IInvoiceService
{
    private readonly IInvoiceRepository _invoiceRepository;
    private readonly IDocumentRepository _documentRepository;

    public InvoiceService(
        IInvoiceRepository invoiceRepository,
        IDocumentRepository documentRepository)
    {
        _invoiceRepository = invoiceRepository;
        _documentRepository = documentRepository;
    }

    public async Task<InvoiceResponse?> GetByIdAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        var invoice = await _invoiceRepository.GetByIdAsync(
            id,
            cancellationToken);

        return invoice is null
            ? null
            : Map(invoice);
    }

    public async Task<InvoiceResponse?> GetByDocumentIdAsync(
        Guid documentId,
        CancellationToken cancellationToken = default)
    {
        var invoice = await _invoiceRepository.GetByDocumentIdAsync(
            documentId,
            cancellationToken);

        return invoice is null
            ? null
            : Map(invoice);
    }

    public async Task<InvoiceResponse> CreateAsync(
        CreateInvoiceRequest request,
        CancellationToken cancellationToken = default)
    {
        var issues = InvoiceValidator.Validate(request);

        if (issues.Count > 0)
        {
            throw new InvalidOperationException(
                string.Join(" | ", issues));
        }

        var document = await _documentRepository.GetByIdAsync(
            request.DocumentId,
            cancellationToken);

        if (document is null)
        {
            throw new InvalidOperationException("Document not found.");
        }

        var existingInvoice = await _invoiceRepository.GetByDocumentIdAsync(
            request.DocumentId,
            cancellationToken);

        if (existingInvoice is not null)
        {
            throw new InvalidOperationException(
                "An invoice already exists for this document.");
        }

        var invoice = new Invoice
        {
            DocumentId = request.DocumentId,
            SupplierName = request.SupplierName,
            SupplierAddress = request.SupplierAddress,
            SupplierTaxId = request.SupplierTaxId,
            InvoiceNumber = request.InvoiceNumber,
            InvoiceDate = request.InvoiceDate,
            DueDate = request.DueDate,
            PurchaseOrderNumber = request.PurchaseOrderNumber,
            Currency = request.Currency,
            SubtotalAmount = request.SubtotalAmount,
            TaxAmount = request.TaxAmount,
            TotalAmount = request.TotalAmount,
            Lines = request.Lines
                .Select(line => new InvoiceLine
                {
                    LineNumber = line.LineNumber,
                    Description = line.Description,
                    Quantity = line.Quantity,
                    UnitOfMeasure = line.UnitOfMeasure,
                    UnitPrice = line.UnitPrice,
                    TaxRate = line.TaxRate,
                    TaxAmount = line.TaxAmount,
                    LineAmount = line.LineAmount
                })
                .ToList()
        };

        await _invoiceRepository.AddAsync(invoice, cancellationToken);
        await _invoiceRepository.SaveChangesAsync(cancellationToken);

        return Map(invoice);
    }

    private static InvoiceResponse Map(Invoice invoice)
    {
        return new InvoiceResponse(
            invoice.Id,
            invoice.DocumentId,
            invoice.SupplierName,
            invoice.SupplierAddress,
            invoice.SupplierTaxId,
            invoice.InvoiceNumber,
            invoice.InvoiceDate,
            invoice.DueDate,
            invoice.PurchaseOrderNumber,
            invoice.Currency,
            invoice.SubtotalAmount,
            invoice.TaxAmount,
            invoice.TotalAmount,
            invoice.Lines
                .Select(line => new InvoiceLineResponse(
                    line.Id,
                    line.LineNumber,
                    line.Description,
                    line.Quantity,
                    line.UnitOfMeasure,
                    line.UnitPrice,
                    line.TaxRate,
                    line.TaxAmount,
                    line.LineAmount
                ))
                .ToList(),
            invoice.CreatedAt,
            invoice.UpdatedAt
        );
    }
}