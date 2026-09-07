using Wida.Dal.Models;

namespace Wida.Dal.Services.Interfaces;

public interface IDocumentAnalyzer
{
    Task<DocumentAnalysisResult> AnalyzeInvoiceAsync(
        string filePath,
        CancellationToken cancellationToken = default);
}
