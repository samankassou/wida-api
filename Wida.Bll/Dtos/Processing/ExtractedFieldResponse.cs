using System.Text.Json;
using Wida.Dal.Enums;

namespace Wida.Bll.Dtos.Processing;

public record ExtractedFieldResponse(
    Guid Id,
    string FieldName,
    string? RawValue,
    JsonElement? NormalizedValue,
    decimal? Confidence,
    ExtractionSource Source,
    int? PageNumber,
    JsonElement? BoundingBox,
    bool RequiresReview
);
