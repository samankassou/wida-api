using Microsoft.EntityFrameworkCore;

namespace Wida.Api.Data;

public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    // Add DbSet<TEntity> properties here as domain models are introduced.
}
