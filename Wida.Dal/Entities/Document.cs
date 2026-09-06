using Wida.Dal.Enums;

namespace Wida.Dal.Entities;

public class Document
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public string OriginalFileName { get; set; } = string.Empty;

    public string ContentType { get; set; } = string.Empty;

    public string StoragePath { get; set; } = string.Empty;

    public DocumentType DocumentType { get; set; } = DocumentType.Unknown;

    public DocumentStatus Status { get; set; } = DocumentStatus.Uploaded;

    public DateTime UploadedAt { get; set; } = DateTime.UtcNow;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}