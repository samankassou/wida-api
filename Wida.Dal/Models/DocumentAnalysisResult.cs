namespace Wida.Dal.Models;

public class DocumentAnalysisResult
{
    public string RawResult { get; set; } = string.Empty;

    public List<AnalyzedField> Fields { get; set; } = [];
}
