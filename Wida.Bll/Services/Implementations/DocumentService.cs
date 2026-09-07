using Wida.Bll.Dtos.Documents;
using Wida.Bll.Services.Interfaces;
using Wida.Dal.Entities;
using Wida.Dal.Enums;
using Wida.Dal.Repositories.Interfaces;

namespace Wida.Bll.Services.Implementations;

public class DocumentService : IDocumentService
{
    private readonly IDocumentRepository _documentRepository;

    public DocumentService(IDocumentRepository documentRepository)
    {
        _documentRepository = documentRepository;
    }

    public async Task<DocumentResponse> CreateAsync(
        string fileName,
        string contentType,
        string storagePath,
        CancellationToken cancellationToken = default)
    {
        var document = new Document
        {
            OriginalFileName = fileName,
            ContentType = contentType,
            StoragePath = storagePath,
            DocumentType = DocumentType.Unknown,
            Status = DocumentStatus.Uploaded
        };

        await _documentRepository.AddAsync(document, cancellationToken);
        await _documentRepository.SaveChangesAsync(cancellationToken);

        return Map(document);
    }

    public async Task<DocumentContent?> GetContentAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        var document = await _documentRepository.GetByIdAsync(id, cancellationToken);
        return document is null ? null : new DocumentContent(
            document.OriginalFileName, document.ContentType, document.StoragePath);
    }

    public async Task<IReadOnlyList<DocumentWorkspaceResponse>> GetWorkspaceAsync(
        int limit = 100,
        CancellationToken cancellationToken = default)
    {
        var documents = await _documentRepository.GetWorkspaceAsync(limit, cancellationToken);
        return documents.Select(document => new DocumentWorkspaceResponse(
            Map(document),
            document.Invoice is null ? null : InvoiceService.Map(document.Invoice),
            document.ProcessingRuns.OrderByDescending(run => run.StartedAt)
                .ThenByDescending(run => run.Id).FirstOrDefault() is { } run
                ? ProcessingService.Map(run) : null)).ToList();
    }

    public async Task<DocumentResponse?> GetByIdAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        var document = await _documentRepository.GetByIdAsync(
            id,
            cancellationToken);

        return document is null
            ? null
            : Map(document);
    }

    public async Task<IReadOnlyList<DocumentResponse>> GetAllAsync(
        CancellationToken cancellationToken = default)
    {
        var documents = await _documentRepository.GetAllAsync(
            cancellationToken);

        return documents
            .Select(Map)
            .ToList();
    }

    private static DocumentResponse Map(Document document)
    {
        return new DocumentResponse(
            document.Id,
            document.OriginalFileName,
            document.ContentType,
            document.DocumentType,
            document.Status,
            document.UploadedAt
        );
    }
}
