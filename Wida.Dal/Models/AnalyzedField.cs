using System.Text.Json;

namespace Wida.Dal.Models;

public class AnalyzedField
{
    public string Name { get; set; } = string.Empty;

    public string? RawValue { get; set; }

    public JsonElement? NormalizedValue { get; set; }

    public decimal? Confidence { get; set; }

    public int? PageNumber { get; set; }

    public JsonElement? BoundingBox { get; set; }
}
