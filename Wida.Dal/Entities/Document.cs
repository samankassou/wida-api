using Wida.Dal.Enums;

namespace Wida.Dal.Entities;

public class Document
{
    public Guid Id { get; set; } = Guid.NewGuid();

    // Existing documents remain unassigned until explicitly migrated by an administrator.
    public Guid? OwnerUserId { get; set; }

    public AppUser? OwnerUser { get; set; }

    public string OriginalFileName { get; set; } = string.Empty;

    public string ContentType { get; set; } = string.Empty;

    public string StoragePath { get; set; } = string.Empty;

    public ICollection<ProcessingRun> ProcessingRuns { get; set; } = [];

    public DocumentType DocumentType { get; set; } = DocumentType.Unknown;

    public DocumentStatus Status { get; set; } = DocumentStatus.Uploaded;

    public Invoice? Invoice { get; set; }

    public DateTime UploadedAt { get; set; } = DateTime.UtcNow;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
