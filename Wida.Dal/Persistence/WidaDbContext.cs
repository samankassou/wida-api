using Microsoft.EntityFrameworkCore;
using Wida.Dal.Entities;
using Wida.Dal.Enums;
using Wida.Dal.Services.Interfaces;

namespace Wida.Dal.Persistence;

public class WidaDbContext(DbContextOptions<WidaDbContext> options, ICurrentUser currentUser) : DbContext(options)
{
    private Guid? CurrentUserId => currentUser.UserId;

    public DbSet<AppUser> Users => Set<AppUser>();

    public DbSet<Document> Documents => Set<Document>();

    public DbSet<Invoice> Invoices => Set<Invoice>();

    public DbSet<InvoiceLine> InvoiceLines => Set<InvoiceLine>();

    public DbSet<ProcessingRun> ProcessingRuns => Set<ProcessingRun>();

    public DbSet<ExtractedField> ExtractedFields => Set<ExtractedField>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(
            typeof(WidaDbContext).Assembly);

        // Each filter is also applied to direct child-entity queries, not just includes.
        modelBuilder.Entity<Document>().HasQueryFilter(document =>
            CurrentUserId != null && document.OwnerUserId == CurrentUserId);
        modelBuilder.Entity<Invoice>().HasQueryFilter(invoice =>
            CurrentUserId != null && invoice.Document.OwnerUserId == CurrentUserId);
        modelBuilder.Entity<InvoiceLine>().HasQueryFilter(line =>
            CurrentUserId != null && line.Invoice.Document.OwnerUserId == CurrentUserId);
        modelBuilder.Entity<ProcessingRun>().HasQueryFilter(run =>
            CurrentUserId != null && run.Document.OwnerUserId == CurrentUserId);
        modelBuilder.Entity<ExtractedField>().HasQueryFilter(field =>
            CurrentUserId != null && field.ProcessingRun.Document.OwnerUserId == CurrentUserId);

        base.OnModelCreating(modelBuilder);
    }

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        foreach (var check in PrepareOwnershipChecks())
        {
            EnsureAllOwned(check.Ids, check.VisibleIds.ToHashSet());
        }

        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override async Task<int> SaveChangesAsync(
        bool acceptAllChangesOnSuccess,
        CancellationToken cancellationToken = default)
    {
        foreach (var check in PrepareOwnershipChecks())
        {
            var visibleIds = await check.VisibleIds.ToListAsync(cancellationToken);
            EnsureAllOwned(check.Ids, visibleIds.ToHashSet());
        }

        try
        {
            return await base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
        }
        catch (DbUpdateConcurrencyException conflict)
        {
            // Invoice saving and extraction can finish in either order. Saved wins.
            // Only reconcile this status transition; other conflicts remain errors.
            foreach (var entry in conflict.Entries)
            {
                if (entry.Entity is not Document document || entry.State != EntityState.Modified)
                    throw;
                var persisted = await entry.GetDatabaseValuesAsync(cancellationToken);
                if (persisted is null
                    || persisted.GetValue<Guid>(nameof(Document.OwnerUserId)) != CurrentUserId)
                    throw;
                var status = persisted.GetValue<DocumentStatus>(nameof(Document.Status));
                if (status == entry.OriginalValues.GetValue<DocumentStatus>(nameof(Document.Status))
                    || (status != DocumentStatus.Saved && document.Status != DocumentStatus.Saved))
                    throw;
                document.Status = DocumentStatus.Saved;
                entry.Property(nameof(Document.Status)).OriginalValue = status;
            }
            // One retry is sufficient for the terminal Saved transition. A further
            // conflict is surfaced rather than retrying indefinitely.
            return await base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
        }
    }

    private List<(HashSet<Guid> Ids, IQueryable<Guid> VisibleIds)> PrepareOwnershipChecks()
    {
        ChangeTracker.DetectChanges();
        var documents = ChangeTracker.Entries<Document>().Where(entry => IsWrite(entry.State)).ToList();
        var invoices = ChangeTracker.Entries<Invoice>().Where(entry => IsWrite(entry.State)).ToList();
        var lines = ChangeTracker.Entries<InvoiceLine>().Where(entry => IsWrite(entry.State)).ToList();
        var runs = ChangeTracker.Entries<ProcessingRun>().Where(entry => IsWrite(entry.State)).ToList();
        var fields = ChangeTracker.Entries<ExtractedField>().Where(entry => IsWrite(entry.State)).ToList();

        if (documents.Count + invoices.Count + lines.Count + runs.Count + fields.Count == 0)
            return [];

        var userId = CurrentUserId ?? throw new UnauthorizedAccessException("An authenticated user is required.");
        foreach (var entry in documents)
        {
            if (entry.State == EntityState.Added)
            {
                if (entry.Entity.OwnerUserId != Guid.Empty && entry.Entity.OwnerUserId != userId)
                    throw new UnauthorizedAccessException("Document ownership cannot be assigned to another user.");
                entry.Entity.OwnerUserId = userId;
            }
            else if (entry.Entity.OwnerUserId != userId ||
                     entry.Property(document => document.OwnerUserId).OriginalValue != userId)
            {
                throw new UnauthorizedAccessException("Document ownership cannot be changed.");
            }
        }

        // Query filters protect reads. These checks also protect detached updates and
        // additions that reference another user's document, invoice or processing run.
        var newDocuments = documents.Where(entry => entry.State == EntityState.Added)
            .Select(entry => entry.Entity.Id).ToHashSet();
        var newInvoices = invoices.Where(entry => entry.State == EntityState.Added)
            .Select(entry => entry.Entity.Id).ToHashSet();
        var newRuns = runs.Where(entry => entry.State == EntityState.Added)
            .Select(entry => entry.Entity.Id).ToHashSet();

        var documentIds = documents.Where(entry => entry.State != EntityState.Added).Select(entry => entry.Entity.Id)
            .Concat(invoices.Select(entry => entry.Entity.DocumentId))
            .Concat(runs.Select(entry => entry.Entity.DocumentId)).Except(newDocuments).ToHashSet();
        var invoiceIds = invoices.Where(entry => entry.State != EntityState.Added).Select(entry => entry.Entity.Id)
            .Concat(lines.Select(entry => entry.Entity.InvoiceId)).Except(newInvoices).ToHashSet();
        var runIds = runs.Where(entry => entry.State != EntityState.Added).Select(entry => entry.Entity.Id)
            .Concat(fields.Select(entry => entry.Entity.ProcessingRunId)).Except(newRuns).ToHashSet();
        var lineIds = lines.Where(entry => entry.State != EntityState.Added).Select(entry => entry.Entity.Id).ToHashSet();
        var fieldIds = fields.Where(entry => entry.State != EntityState.Added).Select(entry => entry.Entity.Id).ToHashSet();

        List<(HashSet<Guid> Ids, IQueryable<Guid> VisibleIds)> checks = [];
        if (documentIds.Count > 0)
            checks.Add((documentIds, Documents.Where(document => documentIds.Contains(document.Id)).Select(document => document.Id)));
        if (invoiceIds.Count > 0)
            checks.Add((invoiceIds, Invoices.Where(invoice => invoiceIds.Contains(invoice.Id)).Select(invoice => invoice.Id)));
        if (runIds.Count > 0)
            checks.Add((runIds, ProcessingRuns.Where(run => runIds.Contains(run.Id)).Select(run => run.Id)));
        if (lineIds.Count > 0)
            checks.Add((lineIds, InvoiceLines.Where(line => lineIds.Contains(line.Id)).Select(line => line.Id)));
        if (fieldIds.Count > 0)
            checks.Add((fieldIds, ExtractedFields.Where(field => fieldIds.Contains(field.Id)).Select(field => field.Id)));
        return checks;
    }

    private static bool IsWrite(EntityState state) =>
        state is EntityState.Added or EntityState.Modified or EntityState.Deleted;

    private static void EnsureAllOwned(HashSet<Guid> requestedIds, HashSet<Guid> visibleIds)
    {
        if (!requestedIds.IsSubsetOf(visibleIds))
            throw new UnauthorizedAccessException("The requested data is not owned by the authenticated user.");
    }
}
