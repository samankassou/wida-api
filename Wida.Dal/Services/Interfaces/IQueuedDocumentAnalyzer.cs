using Wida.Dal.Models;

namespace Wida.Dal.Services.Interfaces;

public interface IQueuedDocumentAnalyzer
{
    TimeSpan? RetryAfter => null;
    Task<string> SubmitAsync(string filePath, CancellationToken cancellationToken);
    Task<DocumentAnalysisResult?> PollAsync(string operationId, CancellationToken cancellationToken);
}

public sealed class AnalysisRequestException(int statusCode, TimeSpan? retryAfter = null)
    : Exception($"Azure analysis request failed (HTTP {statusCode}).")
{
    public int StatusCode { get; } = statusCode;
    public TimeSpan? RetryAfter { get; } = retryAfter;
}
