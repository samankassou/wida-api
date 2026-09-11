using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Wida.Bll.Dtos.Invoices;
using Wida.Bll.Exceptions;
using Wida.Bll.Services.Implementations;
using Wida.Dal.Entities;
using Wida.Dal.Models;
using Wida.Dal.Persistence;
using Wida.Dal.Repositories.Implementations;
using Wida.Dal.Services.Interfaces;

namespace Wida.Tests;

public class DocumentOwnershipTests
{
    [Fact]
    public void PostgreSql_translates_ownership_filters_for_direct_queries_and_workspace_includes()
    {
        var userId = Guid.NewGuid();
        using var context = new WidaDbContext(new DbContextOptionsBuilder<WidaDbContext>()
            .UseNpgsql("Host=localhost;Database=translation_only").Options, new TestCurrentUser(userId));
        IQueryable[] queries =
        [
            context.Documents, context.Invoices, context.InvoiceLines, context.ProcessingRuns, context.ExtractedFields,
            context.Documents.Include(document => document.Invoice).ThenInclude(invoice => invoice!.Lines)
                .Include(document => document.ProcessingRuns).ThenInclude(run => run.ExtractedFields)
        ];

        // Translation does not open a database connection. It catches relational join
        // and filter expressions that the in-memory service tests cannot validate.
        foreach (var query in queries)
        {
            var sql = query.ToQueryString();
            Assert.Contains("\"OwnerUserId\"", sql);
            Assert.Contains(userId.ToString(), sql);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Every_document_and_child_read_is_scoped_to_its_owner(bool useBob)
    {
        var fixture = await Fixture.CreateAsync();
        var mine = useBob ? fixture.BobDocument : fixture.AliceDocument;
        var other = useBob ? fixture.AliceDocument : fixture.BobDocument;
        await using var context = fixture.Open(mine.OwnerUserId);
        var documents = new DocumentService(new DocumentRepository(context));
        var invoices = new InvoiceService(new InvoiceRepository(context), new DocumentRepository(context));
        var processing = new ProcessingService(new ProcessingRunRepository(context),
            new DocumentRepository(context), new NeverCalledAnalyzer());

        Assert.Equal(mine.Id, Assert.Single(await documents.GetAllAsync()).Id);
        var workspace = Assert.Single(await documents.GetWorkspaceAsync());
        Assert.Equal(mine.Id, workspace.Document.Id);
        Assert.Equal(mine.Invoice!.Id, workspace.Invoice!.Id);
        Assert.Equal(mine.ProcessingRuns.Single().Id, workspace.LatestRun!.Id);
        Assert.Null(await documents.GetByIdAsync(other.Id));
        Assert.Null(await documents.GetContentAsync(other.Id));
        Assert.Equal(mine.StoragePath, (await documents.GetContentAsync(mine.Id))!.StoragePath);
        Assert.Equal(mine.Invoice.Id, Assert.Single(await invoices.GetAllAsync()).Id);
        Assert.Null(await invoices.GetByIdAsync(other.Invoice!.Id));
        Assert.Null(await invoices.GetByDocumentIdAsync(other.Id));
        Assert.Null(await processing.GetByIdAsync(other.ProcessingRuns.Single().Id));
        Assert.Empty(await processing.GetByDocumentIdAsync(other.Id));

        Assert.Equal(mine.Id, (await context.Documents.SingleAsync()).Id);
        Assert.Equal(mine.Invoice.Id, (await context.Invoices.SingleAsync()).Id);
        Assert.Equal(mine.Invoice.Lines.Single().Id, (await context.InvoiceLines.SingleAsync()).Id);
        Assert.Equal(mine.ProcessingRuns.Single().Id, (await context.ProcessingRuns.SingleAsync()).Id);
        Assert.Equal(mine.ProcessingRuns.Single().ExtractedFields.Single().Id,
            (await context.ExtractedFields.SingleAsync()).Id);
    }

    [Fact]
    public async Task Foreign_document_cannot_be_saved_or_analyzed_and_no_analyzer_is_called()
    {
        var fixture = await Fixture.CreateAsync();
        await using var context = fixture.Open(fixture.AliceDocument.OwnerUserId);
        var documents = new DocumentRepository(context);
        var invoices = new InvoiceService(new InvoiceRepository(context), documents);
        var analyzer = new NeverCalledAnalyzer();
        var processing = new ProcessingService(new ProcessingRunRepository(context), documents, analyzer);
        var foreign = fixture.BobDocument;
        var request = new CreateInvoiceRequest
        {
            DocumentId = foreign.Id, SupplierName = "Changed", InvoiceNumber = "CHANGED",
            InvoiceDate = new DateOnly(2026, 9, 8), TotalAmount = 120m
        };

        await Assert.ThrowsAsync<DocumentNotFoundException>(() => invoices.CreateAsync(request));
        await Assert.ThrowsAsync<InvoiceNotFoundException>(() => invoices.UpdateAsync(foreign.Invoice!.Id, request));
        await Assert.ThrowsAsync<DocumentNotFoundException>(() => processing.CreateAsync(foreign.Id, "Manual"));
        await Assert.ThrowsAsync<DocumentNotFoundException>(() => processing.ProcessInvoiceAsync(foreign.Id));
        Assert.Equal(0, analyzer.CallCount);

        await context.SaveChangesAsync();
        await using var bob = fixture.Open(foreign.OwnerUserId);
        Assert.Equal("Bob supplier", (await bob.Invoices.SingleAsync()).SupplierName);
        Assert.Equal(foreign.ProcessingRuns.Single().Id, (await bob.ProcessingRuns.SingleAsync()).Id);
    }

    [Fact]
    public async Task Upload_assigns_current_user_and_cannot_select_another_owner()
    {
        var fixture = await Fixture.CreateAsync();
        await using var context = fixture.Open(fixture.AliceDocument.OwnerUserId);
        var service = new DocumentService(new DocumentRepository(context));

        var uploaded = await service.CreateAsync("new.pdf", "application/pdf", "/private/new.pdf");

        await using var persisted = fixture.Open(fixture.AliceDocument.OwnerUserId);
        Assert.Equal(fixture.AliceDocument.OwnerUserId,
            (await persisted.Documents.SingleAsync(document => document.Id == uploaded.Id)).OwnerUserId);
        context.Documents.Add(new Document { OwnerUserId = fixture.BobDocument.OwnerUserId });
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => context.SaveChangesAsync());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Existing_document_ownership_cannot_change_through_normal_save(bool synchronous)
    {
        var fixture = await Fixture.CreateAsync();
        await using var context = fixture.Open(fixture.AliceDocument.OwnerUserId);
        var document = await context.Documents.SingleAsync();
        document.OwnerUserId = fixture.BobDocument.OwnerUserId;

        if (synchronous)
            Assert.Throws<UnauthorizedAccessException>(() => context.SaveChanges());
        else
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => context.SaveChangesAsync());

        await using var persisted = fixture.Open(fixture.AliceDocument.OwnerUserId);
        Assert.Equal(fixture.AliceDocument.OwnerUserId, (await persisted.Documents.SingleAsync()).OwnerUserId);
    }

    [Theory]
    [InlineData("document")]
    [InlineData("invoice")]
    [InlineData("line")]
    [InlineData("run")]
    [InlineData("field")]
    public async Task Detached_foreign_updates_cannot_bypass_query_filters(string entityType)
    {
        var fixture = await Fixture.CreateAsync();
        await using var context = fixture.Open(fixture.AliceDocument.OwnerUserId);
        var mine = fixture.AliceDocument;
        var foreign = fixture.BobDocument;
        object entity = entityType switch
        {
            "document" => new Document { Id = foreign.Id, OwnerUserId = mine.OwnerUserId },
            "invoice" => new Invoice { Id = foreign.Invoice!.Id, DocumentId = mine.Id },
            "line" => new InvoiceLine { Id = foreign.Invoice!.Lines.Single().Id, InvoiceId = mine.Invoice!.Id },
            "run" => new ProcessingRun { Id = foreign.ProcessingRuns.Single().Id, DocumentId = mine.Id },
            "field" => new ExtractedField { Id = foreign.ProcessingRuns.Single().ExtractedFields.Single().Id,
                ProcessingRunId = mine.ProcessingRuns.Single().Id },
            _ => throw new ArgumentOutOfRangeException(nameof(entityType))
        };
        context.Update(entity);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => context.SaveChangesAsync());
    }

    [Theory]
    [InlineData("invoice")]
    [InlineData("line")]
    [InlineData("run")]
    [InlineData("field")]
    public async Task New_children_cannot_reference_foreign_parents(string entityType)
    {
        var fixture = await Fixture.CreateAsync();
        await using var context = fixture.Open(fixture.AliceDocument.OwnerUserId);
        var foreign = fixture.BobDocument;
        object entity = entityType switch
        {
            "invoice" => new Invoice { DocumentId = foreign.Id },
            "line" => new InvoiceLine { InvoiceId = foreign.Invoice!.Id },
            "run" => new ProcessingRun { DocumentId = foreign.Id },
            "field" => new ExtractedField { ProcessingRunId = foreign.ProcessingRuns.Single().Id },
            _ => throw new ArgumentOutOfRangeException(nameof(entityType))
        };
        context.Add(entity);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => context.SaveChangesAsync());
    }

    [Fact]
    public async Task Anonymous_context_cannot_read_or_create_documents()
    {
        var fixture = await Fixture.CreateAsync();
        await using var anonymous = fixture.Open(null);
        Assert.Empty(await anonymous.Documents.ToListAsync());
        Assert.Empty(await anonymous.Invoices.ToListAsync());
        Assert.Empty(await anonymous.InvoiceLines.ToListAsync());
        Assert.Empty(await anonymous.ProcessingRuns.ToListAsync());
        Assert.Empty(await anonymous.ExtractedFields.ToListAsync());
        var upload = new DocumentService(new DocumentRepository(anonymous));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => upload.CreateAsync("anonymous.pdf", "application/pdf", "/private/anonymous.pdf"));


    }

    private sealed class NeverCalledAnalyzer : IDocumentAnalyzer
    {
        public int CallCount { get; private set; }

        public Task<DocumentAnalysisResult> AnalyzeInvoiceAsync(string filePath, CancellationToken cancellationToken = default)
        {
            CallCount++;
            throw new InvalidOperationException("An unauthorized document must never reach Azure.");
        }
    }

    private sealed class Fixture
    {
        private readonly string _databaseName = Guid.NewGuid().ToString();
        private readonly InMemoryDatabaseRoot _databaseRoot = new();

        public Document AliceDocument { get; private set; } = null!;
        public Document BobDocument { get; private set; } = null!;

        public WidaDbContext Open(Guid? userId) => new(new DbContextOptionsBuilder<WidaDbContext>()
            .UseInMemoryDatabase(_databaseName, _databaseRoot).Options, new TestCurrentUser(userId));

        public static async Task<Fixture> CreateAsync()
        {
            var fixture = new Fixture();
            fixture.AliceDocument = await fixture.SeedUserAsync("Alice");
            fixture.BobDocument = await fixture.SeedUserAsync("Bob");
            return fixture;
        }

        private async Task<Document> SeedUserAsync(string name)
        {
            var user = new AppUser { GoogleSubject = name, DisplayName = name, Email = $"{name}@example.test" };
            await using var context = Open(user.Id);
            context.Users.Add(user);
            var document = new Document
            {
                OriginalFileName = $"{name}.pdf", ContentType = "application/pdf", StoragePath = $"/private/{name}.pdf",
                Invoice = new Invoice { SupplierName = $"{name} supplier", Lines = [new InvoiceLine { Description = name }] },
                ProcessingRuns = [new ProcessingRun { Processor = "Test", RawResult = $"{name} raw result",
                    ExtractedFields = [new ExtractedField { FieldName = "VendorName", RawValue = name }] }]
            };
            context.Documents.Add(document);
            await context.SaveChangesAsync();
            return document;
        }
    }
}
