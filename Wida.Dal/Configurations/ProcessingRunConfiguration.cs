using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Wida.Dal.Entities;

namespace Wida.Dal.Configurations;

public class ProcessingRunConfiguration
    : IEntityTypeConfiguration<ProcessingRun>
{
    public void Configure(EntityTypeBuilder<ProcessingRun> builder)
    {
        builder.ToTable("ProcessingRuns");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Processor)
            .IsRequired()
            .HasMaxLength(100);

        builder.Property(x => x.ProcessorVersion)
            .HasMaxLength(100);

        builder.Property(x => x.ErrorCode)
            .HasMaxLength(100);

        builder.Property(x => x.ErrorMessage)
            .HasMaxLength(2000);

        builder.Property(x => x.RawResult)
            .HasColumnType("jsonb");

        builder.HasOne(x => x.Document)
            .WithMany(x => x.ProcessingRuns)
            .HasForeignKey(x => x.DocumentId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(x => x.ExtractedFields)
            .WithOne(x => x.ProcessingRun)
            .HasForeignKey(x => x.ProcessingRunId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(x => x.DocumentId);
    }
}