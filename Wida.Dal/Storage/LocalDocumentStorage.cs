namespace Wida.Dal.Storage;

public sealed class LocalDocumentStorage(string uploadsPath) : IDocumentStorage
{
    private string Validate(string location)
    {
        if (!Path.IsPathFullyQualified(location)
            || Path.GetDirectoryName(Path.GetFullPath(location)) != Path.GetFullPath(uploadsPath)
            || new FileInfo(location).LinkTarget is not null)
            throw new FileNotFoundException("Original is not in the configured storage.");
        return location;
    }

    public Task<string> PersistAsync(string temporaryPath, string contentType, CancellationToken token)
        => Task.FromResult(Validate(temporaryPath));
    public Task<Stream> OpenReadAsync(string location, CancellationToken token)
        => Task.FromResult<Stream>(new FileStream(Validate(location), FileMode.Open, FileAccess.Read, FileShare.Read, 4096, true));
    public Task<bool> ExistsAsync(string location, CancellationToken token)
    {
        try { return Task.FromResult(File.Exists(Validate(location))); }
        catch (FileNotFoundException) { return Task.FromResult(false); }
    }
    public Task DeleteAsync(string location, CancellationToken token)
    {
        try { File.Delete(Validate(location)); }
        catch (FileNotFoundException) { }
        catch (DirectoryNotFoundException) { }
        return Task.CompletedTask;
    }
}
