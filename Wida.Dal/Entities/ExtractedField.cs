using Wida.Dal.Enums;

namespace Wida.Dal.Entities;

public class ExtractedField
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid ProcessingRunId { get; set; }

    public ProcessingRun ProcessingRun { get; set; } = null!;

    public string FieldName { get; set; } = string.Empty;

    public string? RawValue { get; set; }

    public string? NormalizedValue { get; set; }

    public decimal? Confidence { get; set; }

    public ExtractionSource Source { get; set; }

    public int? PageNumber { get; set; }

    public string? BoundingBox { get; set; }

    public bool RequiresReview { get; set; }
}