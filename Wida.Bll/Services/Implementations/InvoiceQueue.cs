using Microsoft.EntityFrameworkCore;
using Wida.Bll.Dtos.Processing;
using Wida.Bll.Exceptions;
using Wida.Bll.Services.Interfaces;
using Wida.Dal.Entities;
using Wida.Dal.Enums;
using Wida.Dal.Persistence;

namespace Wida.Bll.Services.Implementations;

public sealed class InvoiceQueue(WidaDbContext db, IAnalysisJobPublisher publisher) : IInvoiceQueue
{
    public async Task<ProcessingRunResponse> EnqueueAsync(Guid documentId, CancellationToken cancellationToken = default)
    {
        var document = await db.Documents.SingleOrDefaultAsync(x => x.Id == documentId, cancellationToken)
            ?? throw new DocumentNotFoundException(documentId);
        // Short transaction serializes admission/capacity checks across API replicas.
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        if (db.Database.IsNpgsql())
            await db.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(73190421)", cancellationToken);
        var active = db.ProcessingRuns.Where(x => x.IsBackgroundJob
            && (x.Status == ProcessingStatus.Pending || x.Status == ProcessingStatus.Running));
        var existing = await active.SingleOrDefaultAsync(x => x.DocumentId == documentId, cancellationToken);
        if (existing is not null)
        {
            // Its original publication was confirmed before admission committed.
            // Repeated HTTP requests must not flood RabbitMQ with duplicate messages.
            await transaction.CommitAsync(cancellationToken);
            return ProcessingService.Map(existing);
        }
        if (await active.CountAsync(cancellationToken) >= 3)
            throw new QueueCapacityException("You already have three analyses waiting or running. Please wait for one to finish.");
        // Only a count is read across owners; no other user's data is returned.
        if (await active.IgnoreQueryFilters().CountAsync(cancellationToken) >= 100)
            throw new QueueCapacityException("The analysis queue is full. Please try again later.");
        var run = new ProcessingRun
        {
            DocumentId = document.Id, IsBackgroundJob = true,
            Processor = "AzureDocumentIntelligence", ProcessorVersion = "prebuilt-invoice",
            Status = ProcessingStatus.Pending
        };
        db.ProcessingRuns.Add(run);
        document.DocumentType = DocumentType.Invoice;
        if (document.Status != DocumentStatus.Saved) document.Status = DocumentStatus.Queued;
        document.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
        // Publish BEFORE commit. A broker outage rolls this admission back. The consumer
        // takes the admission transaction lock before reading the ID, so it cannot mistake
        // an uncommitted run for an orphan. A rolled-back admission is dead-lettered.
        await publisher.PublishAsync(run.Id, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return ProcessingService.Map(run);
    }
}
