using Microsoft.EntityFrameworkCore;
using Wida.Dal.Entities;

namespace Wida.Dal.Persistence;

public class WidaDbContext(DbContextOptions<WidaDbContext> options) : DbContext(options)
{
    public DbSet<Document> Documents => Set<Document>();

    public DbSet<Invoice> Invoices => Set<Invoice>();

    public DbSet<InvoiceLine> InvoiceLines => Set<InvoiceLine>();

    public DbSet<ProcessingRun> ProcessingRuns => Set<ProcessingRun>();

    public DbSet<ExtractedField> ExtractedFields => Set<ExtractedField>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(
            typeof(WidaDbContext).Assembly);

        base.OnModelCreating(modelBuilder);
    }
}
