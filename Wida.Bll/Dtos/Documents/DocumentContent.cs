namespace Wida.Bll.Dtos.Documents;

// Internal service contract. Controllers stream the file; this is never serialized.
public record DocumentContent(string OriginalFileName, string ContentType, string StoragePath);
