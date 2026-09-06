using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Wida.Dal.Entities;

namespace Wida.Dal.Configurations;

public class ExtractedFieldConfiguration
    : IEntityTypeConfiguration<ExtractedField>
{
    public void Configure(EntityTypeBuilder<ExtractedField> builder)
    {
        builder.ToTable("ExtractedFields");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.FieldName)
            .IsRequired()
            .HasMaxLength(150);

        builder.Property(x => x.RawValue)
            .HasMaxLength(4000);

        builder.Property(x => x.NormalizedValue)
            .HasColumnType("jsonb");

        builder.Property(x => x.BoundingBox)
            .HasColumnType("jsonb");

        builder.Property(x => x.Confidence)
            .HasPrecision(5, 4);

        builder.HasIndex(x => x.ProcessingRunId);
        builder.HasIndex(x => x.FieldName);
    }
}