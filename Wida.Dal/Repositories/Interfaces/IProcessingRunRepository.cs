using Wida.Dal.Entities;

namespace Wida.Dal.Repositories.Interfaces;

public interface IProcessingRunRepository
{
    Task<ProcessingRun?> GetByIdAsync(
        Guid id,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ProcessingRun>> GetByDocumentIdAsync(
        Guid documentId,
        CancellationToken cancellationToken = default);

    Task AddAsync(
        ProcessingRun processingRun,
        CancellationToken cancellationToken = default);

    Task SaveChangesAsync(
        CancellationToken cancellationToken = default);

    void AddExtractedFields(IEnumerable<ExtractedField> extractedFields);
}
