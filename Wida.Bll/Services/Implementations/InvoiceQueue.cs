using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Wida.Bll.Dtos.Processing;
using Wida.Bll.Exceptions;
using Wida.Bll.Services.Interfaces;
using Wida.Dal.Entities;
using Wida.Dal.Enums;
using Wida.Dal.Persistence;

namespace Wida.Bll.Services.Implementations;

public sealed class InvoiceQueue(WidaDbContext db, IAnalysisJobPublisher publisher, IConfiguration? configuration = null) : IInvoiceQueue
{
    public async Task<ProcessingRunResponse> EnqueueAsync(Guid documentId, CancellationToken cancellationToken = default, bool reanalyze = false)
    {
        var document = await db.Documents.SingleOrDefaultAsync(x => x.Id == documentId, cancellationToken)
            ?? throw new DocumentNotFoundException(documentId);
        // Short transaction serializes admission/capacity checks across API replicas.
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        if (db.Database.IsNpgsql())
            await db.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(73190421)", cancellationToken);
        var user = await db.Users.SingleAsync(x => x.Id == document.OwnerUserId, cancellationToken);
        var isAdmin = user.Role == UserRole.Admin;
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
        if (!reanalyze)
        {
            var completed = await db.ProcessingRuns.Include(x => x.ExtractedFields)
                .Where(x => x.DocumentId == documentId && x.IsBackgroundJob && x.Status == ProcessingStatus.Completed)
                .OrderByDescending(x => x.StartedAt).FirstOrDefaultAsync(cancellationToken);
            if (completed is not null) { await transaction.CommitAsync(cancellationToken); return ProcessingService.Map(completed); }
        }
        if (!isAdmin && await active.CountAsync(cancellationToken) >= 1)
            throw new QueueCapacityException("Une analyse est déjà en attente ou en cours. Attendez sa fin.");
        // Only a count is read across owners; no other user's data is returned.
        if (!isAdmin && await active.IgnoreQueryFilters().CountAsync(cancellationToken) >= 100)
            throw new QueueCapacityException("The analysis queue is full. Please try again later.");
        if (document.PageCount < 1 || (!isAdmin && document.PageCount > 2))
            throw new TrialLimitException("Ce document doit être importé à nouveau : seuls les fichiers de 1 ou 2 pages sont acceptés.", 400);
        if (!isAdmin && document.UploadedAt <= DateTime.UtcNow.AddDays(-30))
            throw new TrialLimitException("L’original a expiré après 30 jours. Importez à nouveau votre fichier.", 400);
        if (!isAdmin && user.AnalysisPagesGranted - user.AnalysisPagesUsed < document.PageCount)
            throw new TrialLimitException("Vos crédits sont insuffisants. Demandez plus de crédits ou utilisez la saisie manuelle.");
        if (isAdmin && !string.Equals(configuration?["AzureDocumentIntelligence:Tier"], "S0", StringComparison.OrdinalIgnoreCase)
            && (document.PageCount > 2 || (File.Exists(document.StoragePath) && new FileInfo(document.StoragePath).Length > 4 * 1024 * 1024)))
            throw new TrialLimitException("Votre compte admin est sans quota Wida, mais Azure F0 ne traite que 2 pages et 4 Mio. Configurez Azure S0 pour analyser ce document ; la saisie manuelle reste disponible.", 400);
        await TrialBudget.ReserveMonthAsync(db, document.PageCount, cancellationToken, enforceLimit: !isAdmin);
        if (!isAdmin) user.AnalysisPagesUsed += document.PageCount;
        var run = new ProcessingRun
        {
            IsQuotaExempt = isAdmin, ReservedPages = document.PageCount, BudgetMonth = TrialBudget.Month,
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
