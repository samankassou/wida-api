using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Wida.Dal.Entities;

namespace Wida.Dal.Configurations;

public class InvoiceConfiguration : IEntityTypeConfiguration<Invoice>
{
    public void Configure(EntityTypeBuilder<Invoice> builder)
    {
        builder.ToTable("Invoices");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.SupplierName)
            .HasMaxLength(255);

        builder.Property(x => x.SupplierAddress)
            .HasMaxLength(500);

        builder.Property(x => x.SupplierTaxId)
            .HasMaxLength(100);

        builder.Property(x => x.InvoiceNumber)
            .HasMaxLength(100);

        builder.Property(x => x.PurchaseOrderNumber)
            .HasMaxLength(100);

        builder.Property(x => x.Currency)
            .HasMaxLength(3);

        builder.Property(x => x.SubtotalAmount)
            .HasPrecision(18, 4);

        builder.Property(x => x.TaxAmount)
            .HasPrecision(18, 4);

        builder.Property(x => x.TotalAmount)
            .HasPrecision(18, 4);

        builder.HasOne(x => x.Document)
            .WithOne(x => x.Invoice)
            .HasForeignKey<Invoice>(x => x.DocumentId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(x => x.Lines)
            .WithOne(x => x.Invoice)
            .HasForeignKey(x => x.InvoiceId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(x => x.DocumentId)
            .IsUnique();

        builder.HasIndex(x => x.InvoiceNumber);
    }
}