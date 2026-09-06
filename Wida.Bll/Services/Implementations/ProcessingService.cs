using Wida.Bll.Dtos.Processing;
using Wida.Bll.Services.Interfaces;
using Wida.Dal.Entities;
using Wida.Dal.Enums;
using Wida.Dal.Repositories.Interfaces;

namespace Wida.Bll.Services.Implementations;

public class ProcessingService : IProcessingService
{
    private readonly IProcessingRunRepository _processingRunRepository;
    private readonly IDocumentRepository _documentRepository;

    public ProcessingService(
        IProcessingRunRepository processingRunRepository,
        IDocumentRepository documentRepository)
    {
        _processingRunRepository = processingRunRepository;
        _documentRepository = documentRepository;
    }

    public async Task<ProcessingRunResponse> CreateAsync(
        Guid documentId,
        string processor,
        string? processorVersion = null,
        CancellationToken cancellationToken = default)
    {
        var document = await _documentRepository.GetByIdAsync(
            documentId,
            cancellationToken);

        if (document is null)
        {
            throw new InvalidOperationException("Document not found.");
        }

        var processingRun = new ProcessingRun
        {
            DocumentId = documentId,
            Processor = processor,
            ProcessorVersion = processorVersion,
            Status = ProcessingStatus.Pending,
            StartedAt = DateTime.UtcNow
        };

        await _processingRunRepository.AddAsync(
            processingRun,
            cancellationToken);

        await _processingRunRepository.SaveChangesAsync(
            cancellationToken);

        return Map(processingRun);
    }

    public async Task<ProcessingRunResponse?> GetByIdAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        var run = await _processingRunRepository.GetByIdAsync(
            id,
            cancellationToken);

        return run is null
            ? null
            : Map(run);
    }

    public async Task<IReadOnlyList<ProcessingRunResponse>> GetByDocumentIdAsync(
        Guid documentId,
        CancellationToken cancellationToken = default)
    {
        var runs = await _processingRunRepository.GetByDocumentIdAsync(
            documentId,
            cancellationToken);

        return runs
            .Select(Map)
            .ToList();
    }

    private static ProcessingRunResponse Map(
        ProcessingRun processingRun)
    {
        return new ProcessingRunResponse(
            processingRun.Id,
            processingRun.DocumentId,
            processingRun.Status,
            processingRun.Processor,
            processingRun.ProcessorVersion,
            processingRun.StartedAt,
            processingRun.CompletedAt,
            processingRun.ErrorCode,
            processingRun.ErrorMessage
        );
    }
}