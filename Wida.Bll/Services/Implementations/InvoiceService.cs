using Wida.Bll.Dtos.Invoices;
using Wida.Bll.Services.Interfaces;
using Wida.Dal.Entities;
using Wida.Dal.Repositories.Interfaces;
using Wida.Bll.Validators;
using Wida.Bll.Exceptions;
using Wida.Dal.Enums;

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
        Validate(request);

        var document = await _documentRepository.GetByIdAsync(
            request.DocumentId,
            cancellationToken);

        if (document is null)
        {
            throw new DocumentNotFoundException(request.DocumentId);
        }

        var existingInvoice = await _invoiceRepository.GetByDocumentIdAsync(
            request.DocumentId,
            cancellationToken);

        if (existingInvoice is not null)
        {
            throw new InvoiceConflictException(
                "An invoice already exists for this document.");
        }

        var invoice = new Invoice { DocumentId = request.DocumentId };
        Apply(invoice, request);
        invoice.Lines = CreateLines(invoice.Id, request);
        MarkSaved(document);

        await _invoiceRepository.AddAsync(invoice, cancellationToken);
        await _invoiceRepository.SaveChangesAsync(cancellationToken);

        return Map(invoice);
    }

    public async Task<IReadOnlyList<InvoiceResponse>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        var invoices = await _invoiceRepository.GetAllAsync(cancellationToken);
        return invoices.Select(Map).ToList();
    }

    public async Task<InvoiceResponse> UpdateAsync(Guid id, CreateInvoiceRequest request,
        CancellationToken cancellationToken = default)
    {
        var invoice = await _invoiceRepository.GetByIdAsync(id, cancellationToken)
            ?? throw new InvoiceNotFoundException(id);
        if (invoice.DocumentId != request.DocumentId)
            throw new InvoiceConflictException("An invoice cannot be moved to another document.");
        Validate(request);
        var document = await _documentRepository.GetByIdAsync(invoice.DocumentId, cancellationToken)
            ?? throw new DocumentNotFoundException(invoice.DocumentId);
        Apply(invoice, request);
        _invoiceRepository.ReplaceLines(invoice, CreateLines(invoice.Id, request));
        invoice.UpdatedAt = DateTime.UtcNow;
        MarkSaved(document);
        await _invoiceRepository.SaveChangesAsync(cancellationToken);
        return Map(invoice);
    }

    private static void Validate(CreateInvoiceRequest request)
    {
        var errors = InvoiceValidator.ValidateFields(request);
        if (errors.Count > 0) throw new InvoiceValidationException(errors);
    }

    private static void MarkSaved(Document document)
    {
        document.DocumentType = DocumentType.Invoice;
        document.Status = DocumentStatus.Saved;
        document.UpdatedAt = DateTime.UtcNow;
    }

    private static void Apply(Invoice invoice, CreateInvoiceRequest request)
    {
        invoice.SupplierName = request.SupplierName;
        invoice.SupplierAddress = request.SupplierAddress;
        invoice.SupplierTaxId = request.SupplierTaxId;
        invoice.InvoiceNumber = request.InvoiceNumber;
        invoice.InvoiceDate = request.InvoiceDate;
        invoice.DueDate = request.DueDate;
        invoice.PurchaseOrderNumber = request.PurchaseOrderNumber;
        invoice.Currency = request.Currency;
        invoice.SubtotalAmount = request.SubtotalAmount;
        invoice.ShippingAmount = request.ShippingAmount;
        invoice.DiscountAmount = request.DiscountAmount;
        invoice.TaxAmount = request.TaxAmount;
        invoice.TotalAmount = request.TotalAmount;
    }

    private static List<InvoiceLine> CreateLines(Guid invoiceId, CreateInvoiceRequest request) =>
        request.Lines.Select(line => new InvoiceLine
        {
            InvoiceId = invoiceId,
            LineNumber = line.LineNumber,
            Description = line.Description,
            Quantity = line.Quantity,
            UnitOfMeasure = line.UnitOfMeasure,
            UnitPrice = line.UnitPrice,
            TaxRate = line.TaxRate,
            TaxAmount = line.TaxAmount,
            LineAmount = line.LineAmount
        }).ToList();

    internal static InvoiceResponse Map(Invoice invoice)
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
            invoice.ShippingAmount,
            invoice.DiscountAmount,
            invoice.TaxAmount,
            invoice.TotalAmount,
            invoice.Lines
                .OrderBy(line => line.LineNumber)
                .ThenBy(line => line.Id)
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
