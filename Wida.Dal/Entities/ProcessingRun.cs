using Wida.Dal.Enums;

namespace Wida.Dal.Entities;

public class ProcessingRun
{
    public bool IsQuotaExempt { get; set; }
    public int ReservedPages { get; set; }
    public string? BudgetMonth { get; set; }

    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid DocumentId { get; set; }

    public Document Document { get; set; } = null!;

    public ProcessingStatus Status { get; set; } = ProcessingStatus.Pending;

    public string Processor { get; set; } = string.Empty;

    public string? ProcessorVersion { get; set; }

    public bool IsBackgroundJob { get; set; }

    public string? AzureOperationId { get; set; }

    public DateTime? SubmissionStartedAt { get; set; }

    public DateTime? NextAttemptAt { get; set; }

    public int RetryCount { get; set; }

    public string? RawResult { get; set; }

    public DateTime StartedAt { get; set; } = DateTime.UtcNow;

    public DateTime? CompletedAt { get; set; }

    public string? ErrorCode { get; set; }

    public string? ErrorMessage { get; set; }

    public ICollection<ExtractedField> ExtractedFields { get; set; } = [];
}