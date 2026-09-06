using Wida.Bll.Dtos.Processing;

namespace Wida.Bll.Services.Interfaces;

public interface IProcessingService
{
    Task<ProcessingRunResponse> CreateAsync(
        Guid documentId,
        string processor,
        string? processorVersion = null,
        CancellationToken cancellationToken = default);

    Task<ProcessingRunResponse?> GetByIdAsync(
        Guid id,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ProcessingRunResponse>> GetByDocumentIdAsync(
        Guid documentId,
        CancellationToken cancellationToken = default);
}