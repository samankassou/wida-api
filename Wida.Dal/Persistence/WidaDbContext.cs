using Microsoft.EntityFrameworkCore;
using Wida.Dal.Entities;

namespace Wida.Dal.Persistence;

public class WidaDbContext(DbContextOptions<WidaDbContext> options) : DbContext(options)
{
    public DbSet<Document> Documents => Set<Document>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(
            typeof(WidaDbContext).Assembly);

        base.OnModelCreating(modelBuilder);
    }
}
