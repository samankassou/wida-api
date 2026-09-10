using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Wida.Api.Controllers;
using Wida.Bll.Dtos.Invoices;
using Wida.Bll.Exceptions;
using Wida.Bll.Services.Implementations;
using Wida.Dal.Entities;
using Wida.Dal.Enums;
using Wida.Dal.Persistence;
using Wida.Dal.Repositories.Implementations;

namespace Wida.Tests;

public class InvoiceWorkspaceTests
{
    [Fact]
    public async Task Tax_inclusive_lines_with_net_unit_prices_preserve_source_amounts()
    {
        await using var fixture = await Fixture.CreateAsync();
        var request = ValidInvoice(fixture.Document.Id);
        request.SubtotalAmount = 1507.50m;
        request.TaxAmount = 150.75m;
        request.TotalAmount = 1658.25m;
        request.Lines = [
            new() { Quantity = 5m, UnitPrice = 22.50m, TaxRate = 10m, LineAmount = 123.75m },
            new() { Quantity = 5m, UnitPrice = 279m, TaxRate = 10m, LineAmount = 1534.50m }
        ];
        var service = fixture.InvoiceService();
        var saved = await service.CreateAsync(request);
        Assert.Contains(saved.Lines, line => line.LineAmount == 123.75m);
        request.Lines[0].TaxRate = null;
        await Assert.ThrowsAsync<InvoiceValidationException>(() => service.UpdateAsync(saved.Id, request));
        request.Lines[0].TaxAmount = 11.25m;
        await service.UpdateAsync(saved.Id, request);
        request.Lines[0].TaxRate = 20m;
        await Assert.ThrowsAsync<InvoiceValidationException>(() => service.UpdateAsync(saved.Id, request));
        request.Lines[0].TaxRate = 10m;
        request.Lines[0].LineAmount = 124m;
        await Assert.ThrowsAsync<InvoiceValidationException>(() => service.UpdateAsync(saved.Id, request));
    }

    [Fact]
    public void Adjustment_migration_matches_the_PostgreSql_model()
    {
        using var context = new WidaDbContext(new DbContextOptionsBuilder<WidaDbContext>()
            .UseNpgsql("Host=localhost;Database=migration_check;Username=test;Password=test")
            .Options, TestCurrentUser.Default);
        Assert.False(context.Database.HasPendingModelChanges());
        Assert.Contains("20260910120000_AddInvoiceAdjustments", context.Database.GetMigrations());
    }

    [Fact]
    public async Task Shipping_and_discount_are_validated_persisted_and_updated()
    {
        await using var fixture = await Fixture.CreateAsync();
        var request = ValidInvoice(fixture.Document.Id);
        request.SubtotalAmount = 3312.82m;
        request.TaxAmount = 0m;
        request.ShippingAmount = 50m;
        request.DiscountAmount = 0m;
        request.TotalAmount = 3362.82m;
        request.Lines = [];
        var service = fixture.InvoiceService();
        var created = await service.CreateAsync(request);
        Assert.Equal(50m, created.ShippingAmount);
        request.DiscountAmount = 12.82m;
        request.TotalAmount = 3350m;
        var updated = await service.UpdateAsync(created.Id, request);
        Assert.Equal(12.82m, updated.DiscountAmount);
        await using var context = fixture.OpenContext();
        var saved = await context.Invoices.SingleAsync();
        Assert.Equal(50m, saved.ShippingAmount);
        Assert.Equal(12.82m, saved.DiscountAmount);
        Assert.Equal(3350m, saved.TotalAmount);
        request.TotalAmount = 3312.82m;
        await Assert.ThrowsAsync<InvoiceValidationException>(() => service.UpdateAsync(created.Id, request));
        request.ShippingAmount = null;
        request.DiscountAmount = null;
        var cleared = await service.UpdateAsync(created.Id, request);
        Assert.Null(cleared.ShippingAmount);
        Assert.Null(cleared.DiscountAmount);
    }

    [Fact]
    public async Task Creating_an_invoice_persists_its_lines_and_marks_the_source_document_saved()
    {
        await using var fixture = await Fixture.CreateAsync();
        var request = ValidInvoice(fixture.Document.Id);

        var response = await fixture.InvoiceService().CreateAsync(request);

        await using var persistedContext = fixture.OpenContext();
        var saved = await persistedContext.Invoices.Include(invoice => invoice.Lines).SingleAsync();
        Assert.Equal(response.Id, saved.Id);
        Assert.Equal(fixture.Document.Id, saved.DocumentId);
        Assert.Equal("Example Supplier", saved.SupplierName);
        Assert.Equal(120m, saved.TotalAmount);
        Assert.Equal(2, saved.Lines.Count);
        Assert.All(saved.Lines, line => Assert.Equal(saved.Id, line.InvoiceId));
        Assert.Equal(response.Lines.Select(line => line.Id).Order(), saved.Lines.Select(line => line.Id).Order());
        var document = await persistedContext.Documents.SingleAsync();
        Assert.Equal(DocumentStatus.Saved, document.Status);
        Assert.Equal(DocumentType.Invoice, document.DocumentType);
    }

    [Fact]
    public async Task Updating_an_invoice_replaces_persisted_lines_and_preserves_its_identity_and_creation_time()
    {
        await using var fixture = await Fixture.CreateAsync();
        var service = fixture.InvoiceService();
        var original = await service.CreateAsync(ValidInvoice(fixture.Document.Id));
        var createdAt = new DateTime(2026, 1, 1, 10, 0, 0, DateTimeKind.Utc);
        var previousUpdate = createdAt.AddDays(1);
        var entity = await fixture.Context.Invoices.SingleAsync();
        entity.CreatedAt = createdAt;
        entity.UpdatedAt = previousUpdate;
        await fixture.Context.SaveChangesAsync();
        fixture.Context.ChangeTracker.Clear();
        var request = ValidInvoice(fixture.Document.Id);
        request.SupplierName = "Corrected Supplier";
        request.InvoiceNumber = "INV-CORRECTED";
        request.SupplierAddress = "10 Market Street";
        request.SupplierTaxId = "TAX-42";
        request.PurchaseOrderNumber = "PO-25";
        request.Currency = "EUR";
        request.SubtotalAmount = 200m;
        request.TaxAmount = 40m;
        request.TotalAmount = 240m;
        request.Lines = [new() { LineNumber = 1, Description = "Replacement line", Quantity = 4m,
            UnitOfMeasure = "hours", UnitPrice = 50m, TaxRate = 20m, TaxAmount = 40m, LineAmount = 200m }];

        var updated = await service.UpdateAsync(original.Id, request);

        Assert.Equal(original.Id, updated.Id);
        Assert.Equal(original.DocumentId, updated.DocumentId);
        Assert.Equal(createdAt, updated.CreatedAt);
        Assert.True(updated.UpdatedAt > previousUpdate);
        Assert.Equal("Corrected Supplier", updated.SupplierName);
        Assert.Equal("INV-CORRECTED", updated.InvoiceNumber);
        Assert.Equal("10 Market Street", updated.SupplierAddress);
        Assert.Equal("TAX-42", updated.SupplierTaxId);
        Assert.Equal("PO-25", updated.PurchaseOrderNumber);
        Assert.Equal("EUR", updated.Currency);
        Assert.Equal(240m, updated.TotalAmount);
        var replacement = Assert.Single(updated.Lines);
        Assert.DoesNotContain(replacement.Id, original.Lines.Select(line => line.Id));
        Assert.Equal("hours", replacement.UnitOfMeasure);
        Assert.Equal(20m, replacement.TaxRate);

        await using var persistedContext = fixture.OpenContext();
        var persisted = await persistedContext.Invoices.Include(invoice => invoice.Lines).SingleAsync();
        Assert.Equal(createdAt, persisted.CreatedAt);
        Assert.Equal(updated.UpdatedAt, persisted.UpdatedAt);
        Assert.Equal("Corrected Supplier", persisted.SupplierName);
        Assert.Equal(replacement.Id, Assert.Single(persisted.Lines).Id);
        Assert.Equal(replacement.Id, (await persistedContext.InvoiceLines.SingleAsync()).Id);
        var document = await persistedContext.Documents.SingleAsync();
        Assert.Equal(DocumentStatus.Saved, document.Status);
        Assert.Equal(DocumentType.Invoice, document.DocumentType);
    }

    [Fact]
    public async Task Invalid_update_returns_field_errors_without_mutating_tracked_or_persisted_values()
    {
        await using var fixture = await Fixture.CreateAsync();
        var service = fixture.InvoiceService();
        var original = await service.CreateAsync(ValidInvoice(fixture.Document.Id));
        var invalid = ValidInvoice(fixture.Document.Id);
        invalid.SupplierName = "Must never be persisted";
        invalid.DueDate = invalid.InvoiceDate!.Value.AddDays(-1);
        invalid.TotalAmount = 999m;
        invalid.Lines = [new() { LineNumber = 1, Quantity = 2m, UnitPrice = 10m, LineAmount = 30m }];

        var exception = await Assert.ThrowsAsync<InvoiceValidationException>(
            () => service.UpdateAsync(original.Id, invalid));

        Assert.Contains("dueDate", exception.Errors.Keys);
        Assert.Contains("totalAmount", exception.Errors.Keys);
        Assert.Contains("lines[0].lineAmount", exception.Errors.Keys);
        Assert.Contains("subtotalAmount", exception.Errors.Keys);
        Assert.All(exception.Errors.Values, errors => Assert.NotEmpty(errors));
        var tracked = await fixture.Context.Invoices.Include(invoice => invoice.Lines).SingleAsync();
        Assert.Equal(original.SupplierName, tracked.SupplierName);
        Assert.Equal(original.TotalAmount, tracked.TotalAmount);
        Assert.Equal(original.UpdatedAt, tracked.UpdatedAt);
        Assert.Equal(original.Lines.Select(line => line.Id).Order(), tracked.Lines.Select(line => line.Id).Order());

        // Saving the same unit of work must not leak a rejected edit into storage.
        await fixture.Context.SaveChangesAsync();
        await using var persistedContext = fixture.OpenContext();
        var persisted = await persistedContext.Invoices.Include(invoice => invoice.Lines).SingleAsync();
        Assert.Equal(original.SupplierName, persisted.SupplierName);
        Assert.Equal(original.TotalAmount, persisted.TotalAmount);
        Assert.Equal(original.DueDate, persisted.DueDate);
        Assert.Equal(original.UpdatedAt, persisted.UpdatedAt);
        Assert.Equal(original.Lines.Select(line => line.Id).Order(), persisted.Lines.Select(line => line.Id).Order());
    }

    [Fact]
    public async Task Missing_required_values_return_individual_field_errors_without_creating_an_invoice()
    {
        await using var fixture = await Fixture.CreateAsync();
        var request = new CreateInvoiceRequest { DocumentId = fixture.Document.Id, SupplierName = "  " };

        var exception = await Assert.ThrowsAsync<InvoiceValidationException>(
            () => fixture.InvoiceService().CreateAsync(request));

        Assert.Equal(new[] { "invoiceDate", "invoiceNumber", "supplierName", "totalAmount" },
            exception.Errors.Keys.Order());
        await using var persistedContext = fixture.OpenContext();
        Assert.Empty(await persistedContext.Invoices.ToListAsync());
        Assert.Empty(await persistedContext.InvoiceLines.ToListAsync());
        var document = await persistedContext.Documents.SingleAsync();
        Assert.Equal(DocumentStatus.Uploaded, document.Status);
        Assert.Equal(DocumentType.Unknown, document.DocumentType);
    }

    [Fact]
    public async Task Duplicate_creation_and_document_reassociation_are_conflicts_and_preserve_original_links()
    {
        await using var fixture = await Fixture.CreateAsync();
        var otherDocument = new Document { OriginalFileName = "other.pdf", StoragePath = "/test/other.pdf" };
        fixture.Context.Documents.Add(otherDocument);
        await fixture.Context.SaveChangesAsync();
        var service = fixture.InvoiceService();
        var original = await service.CreateAsync(ValidInvoice(fixture.Document.Id));

        await Assert.ThrowsAsync<InvoiceConflictException>(
            () => service.CreateAsync(ValidInvoice(fixture.Document.Id)));
        await Assert.ThrowsAsync<InvoiceConflictException>(
            () => service.UpdateAsync(original.Id, ValidInvoice(otherDocument.Id)));

        await fixture.Context.SaveChangesAsync();
        await using var persistedContext = fixture.OpenContext();
        var persisted = await persistedContext.Invoices.SingleAsync();
        Assert.Equal(original.Id, persisted.Id);
        Assert.Equal(fixture.Document.Id, persisted.DocumentId);
        Assert.Equal(DocumentStatus.Uploaded,
            (await persistedContext.Documents.SingleAsync(document => document.Id == otherDocument.Id)).Status);
        Assert.Equal(2, await persistedContext.InvoiceLines.CountAsync());
    }

    [Fact]
    public async Task Missing_invoice_or_document_produces_specific_not_found_errors_without_inserting_data()
    {
        await using var fixture = await Fixture.CreateAsync();
        var service = fixture.InvoiceService();

        await Assert.ThrowsAsync<InvoiceNotFoundException>(
            () => service.UpdateAsync(Guid.NewGuid(), ValidInvoice(fixture.Document.Id)));
        await Assert.ThrowsAsync<DocumentNotFoundException>(
            () => service.CreateAsync(ValidInvoice(Guid.NewGuid())));

        await using var persistedContext = fixture.OpenContext();
        Assert.Empty(await persistedContext.Invoices.ToListAsync());
        Assert.Empty(await persistedContext.InvoiceLines.ToListAsync());
    }

    [Fact]
    public async Task Invoice_listing_includes_persisted_line_details()
    {
        await using var fixture = await Fixture.CreateAsync();
        var saved = await fixture.InvoiceService().CreateAsync(ValidInvoice(fixture.Document.Id));
        await using var readContext = fixture.OpenContext();
        var reader = new InvoiceService(new InvoiceRepository(readContext), new DocumentRepository(readContext));

        var listed = Assert.Single(await reader.GetAllAsync());

        Assert.Equal(saved.Id, listed.Id);
        Assert.Equal(saved.DocumentId, listed.DocumentId);
        Assert.Equal(saved.TotalAmount, listed.TotalAmount);
        Assert.Equal(saved.Lines.OrderBy(line => line.Id), listed.Lines.OrderBy(line => line.Id));
    }

    [Fact]
    public async Task Workspace_returns_newest_documents_with_optional_invoice_and_only_the_latest_run()
    {
        await using var fixture = await Fixture.CreateAsync();
        var start = new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc);
        fixture.Document.UploadedAt = start;
        var invoiceDocument = new Document { OriginalFileName = "saved.pdf", StoragePath = "/test/saved.pdf",
            UploadedAt = start.AddDays(1) };
        var rerunDocument = new Document { OriginalFileName = "rerun.pdf", StoragePath = "/test/rerun.pdf",
            UploadedAt = start.AddDays(2) };
        fixture.Context.Documents.AddRange(invoiceDocument, rerunDocument);
        var olderRun = new ProcessingRun { DocumentId = rerunDocument.Id, Processor = "old",
            Status = ProcessingStatus.Failed, StartedAt = start.AddHours(3), CompletedAt = start.AddHours(4) };
        var latestRun = new ProcessingRun { DocumentId = rerunDocument.Id, Processor = "latest",
            Status = ProcessingStatus.Completed, StartedAt = start.AddHours(5), CompletedAt = start.AddHours(6),
            ExtractedFields = [new() { FieldName = "InvoiceTotal", RawValue = "120.00",
                NormalizedValue = "120.00", Confidence = 0.75m, RequiresReview = true,
                Source = ExtractionSource.DocumentIntelligence, PageNumber = 1 }] };
        fixture.Context.ProcessingRuns.AddRange(latestRun, olderRun);
        await fixture.Context.SaveChangesAsync();
        var saved = await fixture.InvoiceService().CreateAsync(ValidInvoice(invoiceDocument.Id));
        await using var readContext = fixture.OpenContext();
        var service = new DocumentService(new DocumentRepository(readContext));

        var workspace = await service.GetWorkspaceAsync();

        Assert.Equal(new[] { rerunDocument.Id, invoiceDocument.Id, fixture.Document.Id },
            workspace.Select(row => row.Document.Id));
        var extraction = workspace[0];
        Assert.Null(extraction.Invoice);
        Assert.NotNull(extraction.LatestRun);
        Assert.Equal(latestRun.Id, extraction.LatestRun.Id);
        Assert.Equal("latest", extraction.LatestRun.Processor);
        var field = Assert.Single(extraction.LatestRun.ExtractedFields);
        Assert.Equal(120m, field.NormalizedValue!.Value.GetDecimal());
        Assert.True(field.RequiresReview);
        Assert.Equal(1, field.PageNumber);
        var completed = workspace[1];
        Assert.Equal(DocumentStatus.Saved, completed.Document.Status);
        Assert.NotNull(completed.Invoice);
        Assert.Equal(saved.Id, completed.Invoice.Id);
        Assert.Equal(saved.Lines.OrderBy(line => line.Id), completed.Invoice.Lines.OrderBy(line => line.Id));
        Assert.Null(completed.LatestRun);
        Assert.Null(workspace[2].Invoice);
        Assert.Null(workspace[2].LatestRun);

        var limited = await service.GetWorkspaceAsync(2);
        Assert.Equal(new[] { rerunDocument.Id, invoiceDocument.Id }, limited.Select(row => row.Document.Id));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Invoice_write_routes_return_validation_problem_details_with_field_errors(bool update)
    {
        await using var fixture = await Fixture.CreateAsync();
        var service = fixture.InvoiceService();
        var existing = update ? await service.CreateAsync(ValidInvoice(fixture.Document.Id)) : null;
        var controller = new InvoicesController(service)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
        };
        var invalid = ValidInvoice(fixture.Document.Id);
        invalid.SupplierName = " ";
        invalid.DueDate = invalid.InvoiceDate!.Value.AddDays(-1);
        invalid.TotalAmount = 999m;

        var result = update
            ? await controller.Update(existing!.Id, invalid, CancellationToken.None)
            : await controller.Create(invalid, CancellationToken.None);

        var response = Assert.IsType<BadRequestObjectResult>(result);
        Assert.Equal(StatusCodes.Status400BadRequest, response.StatusCode);
        Assert.Contains("application/problem+json", response.ContentTypes);
        var problem = Assert.IsType<ValidationProblemDetails>(response.Value);
        Assert.Equal(StatusCodes.Status400BadRequest, problem.Status);
        Assert.Equal(new[] { "dueDate", "supplierName", "totalAmount" }, problem.Errors.Keys.Order());
        Assert.All(problem.Errors.Values, errors => Assert.NotEmpty(errors));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Invoice_write_routes_map_missing_document_and_invoice_to_not_found(bool update)
    {
        await using var fixture = await Fixture.CreateAsync();
        var controller = new InvoicesController(fixture.InvoiceService())
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
        };
        var missingId = Guid.NewGuid();

        var result = update
            ? await controller.Update(missingId, ValidInvoice(fixture.Document.Id), CancellationToken.None)
            : await controller.Create(ValidInvoice(missingId), CancellationToken.None);

        var response = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status404NotFound, response.StatusCode);
        var problem = Assert.IsType<ProblemDetails>(response.Value);
        Assert.Equal(StatusCodes.Status404NotFound, problem.Status);
        Assert.Contains(missingId.ToString(), problem.Detail);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Invoice_write_routes_map_duplicate_and_reassociation_to_conflict(bool reassociate)
    {
        await using var fixture = await Fixture.CreateAsync();
        var otherDocument = new Document { OriginalFileName = "other.pdf", StoragePath = "/test/other.pdf" };
        fixture.Context.Documents.Add(otherDocument);
        await fixture.Context.SaveChangesAsync();
        var service = fixture.InvoiceService();
        var existing = await service.CreateAsync(ValidInvoice(fixture.Document.Id));
        var controller = new InvoicesController(service)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
        };

        var result = reassociate
            ? await controller.Update(existing.Id, ValidInvoice(otherDocument.Id), CancellationToken.None)
            : await controller.Create(ValidInvoice(fixture.Document.Id), CancellationToken.None);

        var response = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status409Conflict, response.StatusCode);
        var problem = Assert.IsType<ProblemDetails>(response.Value);
        Assert.Equal(StatusCodes.Status409Conflict, problem.Status);
        Assert.False(string.IsNullOrWhiteSpace(problem.Detail));
    }

    private static CreateInvoiceRequest ValidInvoice(Guid documentId) => new()
    {
        DocumentId = documentId,
        SupplierName = "Example Supplier",
        InvoiceNumber = "INV-001",
        InvoiceDate = new DateOnly(2026, 9, 1),
        DueDate = new DateOnly(2026, 9, 30),
        Currency = "USD",
        SubtotalAmount = 100m,
        TaxAmount = 20m,
        TotalAmount = 120m,
        Lines =
        [
            new() { LineNumber = 1, Description = "First line", Quantity = 2m, UnitPrice = 20m, LineAmount = 40m },
            new() { LineNumber = 2, Description = "Second line", Quantity = 3m, UnitPrice = 20m, LineAmount = 60m }
        ]
    };

    private sealed class Fixture : IAsyncDisposable
    {
        private readonly DbContextOptions<WidaDbContext> _options = new DbContextOptionsBuilder<WidaDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;

        public WidaDbContext Context { get; }
        public Document Document { get; } = new() { StoragePath = "/test/invoice.pdf", OriginalFileName = "invoice.pdf" };

        private Fixture() => Context = OpenContext();

        public static async Task<Fixture> CreateAsync()
        {
            var fixture = new Fixture();
            fixture.Context.Users.Add(new AppUser { Id = TestCurrentUser.Default.UserId!.Value,
                GoogleSubject = "invoice-test-user", Email = "invoice@example.test", DisplayName = "Invoice Test" });
            fixture.Context.Documents.Add(fixture.Document);
            await fixture.Context.SaveChangesAsync();
            return fixture;
        }

        public WidaDbContext OpenContext() => new(_options, TestCurrentUser.Default);

        public InvoiceService InvoiceService() => new(new InvoiceRepository(Context), new DocumentRepository(Context));

        public ValueTask DisposeAsync() => Context.DisposeAsync();
    }
}
