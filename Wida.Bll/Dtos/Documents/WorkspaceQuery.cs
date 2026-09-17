using System.ComponentModel.DataAnnotations;

namespace Wida.Bll.Dtos.Documents;

public sealed class WorkspaceQuery
{
    [Range(1, int.MaxValue)] public int Page { get; set; } = 1;
    [Range(1, 100)] public int PageSize { get; set; } = 10;
    [MaxLength(500)] public string? Search { get; set; } = "";
    [RegularExpression("^(all|review|processing|saved|failed|uploaded)$")] public string Filter { get; set; } = "all";
    [RegularExpression("^(documents|invoices)$")] public string View { get; set; } = "documents";
    [RegularExpression("^([A-Za-z]{3})?$")] public string? Currency { get; set; } = "";
    [RegularExpression("^(all|7|30)$")] public string Period { get; set; } = "all";
    [RegularExpression("^(newest|oldest|supplier)$")] public string Sort { get; set; } = "newest";
}

public record WorkspaceMonth(int Year, int Month, int Uploaded, int Saved);
public record WorkspaceSummary(int Total, Dictionary<string, int> Counts, int Active, IReadOnlyList<string> Currencies,
    IReadOnlyList<WorkspaceMonth> Months);
public record WorkspacePage(IReadOnlyList<DocumentWorkspaceResponse> Items, int Total, int Page, int PageSize,
    WorkspaceSummary Summary);
