using Wida.Bll.Dtos.Documents;

namespace Wida.Bll.Services.Interfaces;

public interface IDocumentService
{
    Task<DocumentContent?> GetContentAsync(Guid id, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<DocumentWorkspaceResponse>> GetWorkspaceAsync(
        int limit = 100,
        CancellationToken cancellationToken = default);

    Task<DocumentResponse> CreateAsync(
        string fileName,
        string contentType,
        string storagePath,
        CancellationToken cancellationToken = default);

    Task<DocumentResponse?> GetByIdAsync(
        Guid id,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<DocumentResponse>> GetAllAsync(
        CancellationToken cancellationToken = default);
}
