using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Wida.Dal.Entities;

namespace Wida.Dal.Configurations;

public class AppUserConfiguration : IEntityTypeConfiguration<AppUser>
{
    public void Configure(EntityTypeBuilder<AppUser> builder)
    {
        builder.ToTable("Users");
        builder.HasKey(user => user.Id);
        builder.Property(user => user.GoogleSubject).IsRequired().HasMaxLength(255);
        builder.HasIndex(user => user.GoogleSubject).IsUnique();
        builder.Property(user => user.Email).IsRequired().HasMaxLength(320);
        builder.Property(user => user.DisplayName).IsRequired().HasMaxLength(255);
        builder.Property(user => user.CreatedAt).IsRequired();
    }
}
