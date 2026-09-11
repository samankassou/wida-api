using Microsoft.EntityFrameworkCore;
using Wida.Dal.Entities;
using Wida.Dal.Enums;
using Wida.Dal.Persistence;
using Wida.Dal.Services.Interfaces;

namespace Wida.Bll.Services.Implementations;

// Called only by the RabbitMQ single active consumer while holding its delivery.
// Each step performs at most one Azure request, then persists its next action.
public sealed class InvoiceQueueExecutor(WidaDbContext db, IQueuedDocumentAnalyzer analyzer)
{
    public async Task StepAsync(Guid runId, CancellationToken cancellationToken)
    {
        var run = await db.ProcessingRuns.Include(x => x.Document).SingleAsync(x => x.Id == runId, cancellationToken);
        if (!run.IsBackgroundJob || run.Status is ProcessingStatus.Completed or ProcessingStatus.Failed
            || run.NextAttemptAt > DateTime.UtcNow) return;
        if (run.SubmissionStartedAt is { } started && DateTime.UtcNow - started > TimeSpan.FromHours(20))
        {
            Fail(run, "ANALYSIS_EXPIRED", "Analysis tracking expired. Review the document before requesting another analysis.");
        }
        else if (run.AzureOperationId is null && run.SubmissionStartedAt is not null)
        {
            // A previous process may have sent the POST. Never automatically submit it twice.
            Fail(run, "ANALYSIS_SUBMISSION_UNCERTAIN", "The analysis submission was interrupted. It may have consumed pages. Check before retrying.");
        }
        else
        {
            var submitting = run.AzureOperationId is null;
            if (submitting)
            {
                run.Status = ProcessingStatus.Running;
                run.SubmissionStartedAt = DateTime.UtcNow;
                SetDocumentStatus(run.Document, DocumentStatus.Processing);
                // Persist the uncertainty marker BEFORE the irreversible network request.
                await db.SaveChangesAsync(cancellationToken);
            }
            try
            {
                if (submitting)
                {
                    run.AzureOperationId = await analyzer.SubmitAsync(run.Document.StoragePath, cancellationToken);
                    run.RetryCount = 0;
                }
                else
                {
                    var result = await analyzer.PollAsync(run.AzureOperationId!, cancellationToken);
                    run.RetryCount = 0;
                    if (result is not null)
                    {
                        ProcessingService.ApplyResult(run, result, fields => db.ExtractedFields.AddRange(fields));
                        SetDocumentStatus(run.Document, DocumentStatus.ReviewRequired);
                    }
                }
                var nextDelay = analyzer.RetryAfter is { } retryAfter && retryAfter > TimeSpan.FromSeconds(2)
                    ? retryAfter : TimeSpan.FromSeconds(2);
                if (nextDelay > TimeSpan.FromHours(20))
                    throw new InvalidOperationException("Azure requested a delay beyond the tracking window.");
                run.NextAttemptAt = DateTime.UtcNow + nextDelay;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                // Leave durable state for the next worker; a shutdown does not fail a known operation.
                throw;
            }
            catch (Exception ex)
            {
                var response = ex as AnalysisRequestException;
                var safeToRetry = submitting ? response?.StatusCode == 429
                    : ex is HttpRequestException or OperationCanceledException
                        || response is { StatusCode: 429 or >= 500 };
                if (safeToRetry && ++run.RetryCount <= 5)
                {
                    var delay = TimeSpan.FromSeconds(Math.Min(60, Math.Pow(2, run.RetryCount + 1)));
                    if (response?.RetryAfter is { } requested && requested > delay) delay = requested;
                    // Azure Retry-After is normally short; beyond result retention, stop tracking.
                    if (delay > TimeSpan.FromHours(20))
                        Fail(run, "ANALYSIS_RETRY_EXHAUSTED", "Azure requested a delay beyond the analysis tracking window.");
                    else
                    {
                        run.NextAttemptAt = DateTime.UtcNow + delay;
                        if (submitting) { run.SubmissionStartedAt = null; run.Status = ProcessingStatus.Pending; }
                    }
                }
                else
                {
                    var uncertain = submitting && (response is null || response.StatusCode >= 500 || response.StatusCode == 408);
                    Fail(run, uncertain ? "ANALYSIS_SUBMISSION_UNCERTAIN" : "DOCUMENT_ANALYSIS_FAILED",
                        uncertain ? "The submission outcome is uncertain and may have consumed pages. Check before retrying."
                            : "Analysis could not be completed. You can enter the details manually or try again later.");
                }
            }
        }
        // If this save fails, the next step resumes GET or reports an uncertain POST.
        await db.SaveChangesAsync(cancellationToken);
    }

    private static void Fail(ProcessingRun run, string code, string message)
    {
        run.Status = ProcessingStatus.Failed;
        run.CompletedAt = DateTime.UtcNow;
        run.ErrorCode = code;
        run.ErrorMessage = message;
        SetDocumentStatus(run.Document, DocumentStatus.Failed);
    }

    private static void SetDocumentStatus(Document document, DocumentStatus status)
    {
        if (document.Status != DocumentStatus.Saved) document.Status = status;
        document.UpdatedAt = DateTime.UtcNow;
    }
}
