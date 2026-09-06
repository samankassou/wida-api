using Wida.Dal.Enums;

namespace Wida.Dal.Entities;

public class ProcessingRun
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid DocumentId { get; set; }

    public Document Document { get; set; } = null!;

    public ProcessingStatus Status { get; set; } = ProcessingStatus.Pending;

    public string Processor { get; set; } = string.Empty;

    public string? ProcessorVersion { get; set; }

    public string? RawResult { get; set; }

    public DateTime StartedAt { get; set; } = DateTime.UtcNow;

    public DateTime? CompletedAt { get; set; }

    public string? ErrorCode { get; set; }

    public string? ErrorMessage { get; set; }

    public ICollection<ExtractedField> ExtractedFields { get; set; } = [];
}