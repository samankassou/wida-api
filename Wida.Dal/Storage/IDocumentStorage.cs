namespace Wida.Dal.Storage;

// Stored locations are opaque to callers. Reads must be seekable for PDF byte ranges.
public interface IDocumentStorage
{
    Task<string> PersistAsync(string temporaryPath, string contentType, CancellationToken token);
    Task<Stream> OpenReadAsync(string location, CancellationToken token);
    Task<bool> ExistsAsync(string location, CancellationToken token);
    Task DeleteAsync(string location, CancellationToken token);
}
