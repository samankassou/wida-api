using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Wida.Bll;
using Wida.Bll.Exceptions;
using Wida.Bll.Services.Implementations;
using Wida.Bll.Services.Interfaces;
using Wida.Dal;
using Wida.Dal.Entities;
using Wida.Dal.Enums;
using Wida.Dal.Models;
using Wida.Dal.Persistence;
using Wida.Dal.Repositories.Implementations;
using Wida.Dal.Services.Interfaces;

namespace Wida.Tests;

public class ProcessingServiceTests
{
    [Fact]
    public async Task Analysis_persists_running_state_then_inserts_fields_and_returns_typed_values()
    {
        await using var fixture = await Fixture.CreateAsync();
        var analyzer = new StubAnalyzer(async (path, token) =>
        {
            Assert.Equal(fixture.Document.StoragePath, path);
            await using var runningContext = fixture.OpenContext();
            var running = await runningContext.ProcessingRuns.SingleAsync(token);
            Assert.Equal(ProcessingStatus.Running, running.Status);
            Assert.Null(running.CompletedAt);

            return new DocumentAnalysisResult
            {
                RawResult = "{\"modelId\":\"prebuilt-invoice\"}",
                Fields =
                [
                    Field("VendorName", "Example Supplier", 0.80m),
                    Field("InvoiceDate", "2026-09-07", 0.79m),
                    Field("InvoiceTotal", new { amount = 110.50m, currencyCode = "USD" }, null),
                    Field("TotalTax", 10.50m, 0.99m)
                ]
            };
        });
        var service = fixture.Service(analyzer);

        var response = await service.ProcessInvoiceAsync(fixture.Document.Id);

        Assert.Equal(ProcessingStatus.Completed, response.Status);
        Assert.NotNull(response.CompletedAt);
        Assert.Null(response.ErrorCode);
        Assert.Equal(4, response.ExtractedFields.Count);
        await using var persistedContext = fixture.OpenContext();
        var saved = await persistedContext.ProcessingRuns.Include(run => run.ExtractedFields).SingleAsync();
        Assert.Equal(ProcessingStatus.Completed, saved.Status);
        Assert.Equal("{\"modelId\":\"prebuilt-invoice\"}", saved.RawResult);
        Assert.Equal(4, await persistedContext.ExtractedFields.CountAsync());
        Assert.All(saved.ExtractedFields, field => Assert.Equal(saved.Id, field.ProcessingRunId));

        var reader = new ProcessingService(new ProcessingRunRepository(persistedContext),
            new DocumentRepository(persistedContext), analyzer);
        var read = Assert.IsType<Wida.Bll.Dtos.Processing.ProcessingRunResponse>(await reader.GetByIdAsync(response.Id));
        var supplier = Assert.Single(read.ExtractedFields, field => field.FieldName == "VendorName");
        Assert.False(supplier.RequiresReview);
        Assert.Equal("Example Supplier", supplier.NormalizedValue!.Value.GetString());
        Assert.Equal(ExtractionSource.DocumentIntelligence, supplier.Source);
        Assert.Equal(2, supplier.PageNumber);
        Assert.Equal(JsonValueKind.Array, supplier.BoundingBox!.Value.ValueKind);
        Assert.True(Assert.Single(read.ExtractedFields, field => field.FieldName == "InvoiceDate").RequiresReview);
        var total = Assert.Single(read.ExtractedFields, field => field.FieldName == "InvoiceTotal");
        Assert.True(total.RequiresReview);
        Assert.Equal(110.50m, total.NormalizedValue!.Value.GetProperty("amount").GetDecimal());
        Assert.Equal(10.50m, Assert.Single(read.ExtractedFields, field => field.FieldName == "TotalTax")
            .NormalizedValue!.Value.GetDecimal());
        Assert.Equal(4, Assert.Single(await reader.GetByDocumentIdAsync(fixture.Document.Id)).ExtractedFields.Count);
    }

    [Fact]
    public async Task Analysis_failure_is_saved_with_a_bounded_error_message()
    {
        await using var fixture = await Fixture.CreateAsync();
        var service = fixture.Service(new StubAnalyzer((_, _) => throw new IOException(new string('x', 2500))));

        var response = await service.ProcessInvoiceAsync(fixture.Document.Id);

        Assert.Equal(ProcessingStatus.Failed, response.Status);
        Assert.Equal("DOCUMENT_ANALYSIS_FAILED", response.ErrorCode);
        Assert.Equal(2000, response.ErrorMessage!.Length);
        Assert.NotNull(response.CompletedAt);
        Assert.Empty(response.ExtractedFields);
        await using var persistedContext = fixture.OpenContext();
        Assert.Equal(ProcessingStatus.Failed, (await persistedContext.ProcessingRuns.SingleAsync()).Status);
    }

    [Fact]
    public async Task Oversized_extracted_text_records_failure_instead_of_breaking_terminal_persistence()
    {
        await using var fixture = await Fixture.CreateAsync();
        var field = Field("VendorName", "Example Supplier", 1m);
        field.RawValue = new string('x', 4001);
        var service = fixture.Service(new StubAnalyzer((_, _) => Task.FromResult(
            new DocumentAnalysisResult { RawResult = "{}", Fields = [field] })));

        var response = await service.ProcessInvoiceAsync(fixture.Document.Id);

        Assert.Equal(ProcessingStatus.Failed, response.Status);
        Assert.Equal("DOCUMENT_ANALYSIS_FAILED", response.ErrorCode);
        Assert.Contains("4000", response.ErrorMessage!);
        await using var persistedContext = fixture.OpenContext();
        Assert.Equal(ProcessingStatus.Failed, (await persistedContext.ProcessingRuns.SingleAsync()).Status);
        Assert.Empty(await persistedContext.ExtractedFields.ToListAsync());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Cancellation_persists_terminal_failure_before_propagating(bool returnsAfterCancellation)
    {
        await using var fixture = await Fixture.CreateAsync();
        using var cancellation = new CancellationTokenSource();
        var service = fixture.Service(new StubAnalyzer((_, token) =>
        {
            cancellation.Cancel();
            return returnsAfterCancellation
                ? Task.FromResult(new DocumentAnalysisResult { RawResult = "{}", Fields = [Field("VendorName", "Example", 1m)] })
                : Task.FromCanceled<DocumentAnalysisResult>(token);
        }));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            service.ProcessInvoiceAsync(fixture.Document.Id, cancellation.Token));

        await using var persistedContext = fixture.OpenContext();
        var saved = await persistedContext.ProcessingRuns.SingleAsync();
        Assert.Equal(ProcessingStatus.Failed, saved.Status);
        Assert.Equal("DOCUMENT_ANALYSIS_CANCELLED", saved.ErrorCode);
        Assert.NotNull(saved.CompletedAt);
        Assert.Empty(await persistedContext.ExtractedFields.ToListAsync());
    }

    [Fact]
    public async Task Missing_document_does_not_create_a_run_or_call_the_analyzer()
    {
        await using var fixture = await Fixture.CreateAsync();
        var analyzer = new StubAnalyzer((_, _) => throw new Exception("Analyzer must not run."));
        var service = fixture.Service(analyzer);
        var missingId = Guid.NewGuid();

        await Assert.ThrowsAsync<DocumentNotFoundException>(() => service.ProcessInvoiceAsync(missingId));
        await Assert.ThrowsAsync<DocumentNotFoundException>(() => service.CreateAsync(missingId, "Manual"));

        Assert.Equal(0, analyzer.CallCount);
        Assert.Empty(await fixture.Context.ProcessingRuns.ToListAsync());
    }

    [Fact]
    public async Task Registered_processing_service_supports_manual_and_read_routes_without_Azure_settings()
    {
        await using var fixture = await Fixture.CreateAsync();
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        services.AddDal(new ConfigurationBuilder().Build()).AddBll();
        services.AddScoped(_ => fixture.OpenContext());
        await using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        await using var scope = provider.CreateAsyncScope();
        var service = scope.ServiceProvider.GetRequiredService<IProcessingService>();

        var manual = await service.CreateAsync(fixture.Document.Id, "Manual", "v1");
        Assert.Equal(ProcessingStatus.Pending, manual.Status);
        Assert.Empty(manual.ExtractedFields);
        Assert.NotNull(await service.GetByIdAsync(manual.Id));
        Assert.Single(await service.GetByDocumentIdAsync(fixture.Document.Id));

        var analysis = await service.ProcessInvoiceAsync(fixture.Document.Id);
        Assert.Equal(ProcessingStatus.Failed, analysis.Status);
        Assert.Equal("DOCUMENT_ANALYSIS_FAILED", analysis.ErrorCode);
        Assert.Contains("endpoint", analysis.ErrorMessage!, StringComparison.OrdinalIgnoreCase);
    }

    private static AnalyzedField Field(string name, object value, decimal? confidence) => new()
    {
        Name = name,
        RawValue = "source text",
        NormalizedValue = JsonSerializer.SerializeToElement(value),
        Confidence = confidence,
        PageNumber = 2,
        BoundingBox = JsonSerializer.SerializeToElement(new[] { 1d, 2d, 3d, 4d })
    };

    private sealed class StubAnalyzer(Func<string, CancellationToken, Task<DocumentAnalysisResult>> analyze)
        : IDocumentAnalyzer
    {
        public int CallCount { get; private set; }

        public Task<DocumentAnalysisResult> AnalyzeInvoiceAsync(string filePath, CancellationToken cancellationToken = default)
        {
            CallCount++;
            return analyze(filePath, cancellationToken);
        }
    }

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
            fixture.Context.Documents.Add(fixture.Document);
            await fixture.Context.SaveChangesAsync();
            return fixture;
        }

        public WidaDbContext OpenContext() => new(_options);

        public ProcessingService Service(IDocumentAnalyzer analyzer) => new(
            new ProcessingRunRepository(Context), new DocumentRepository(Context), analyzer);

        public ValueTask DisposeAsync() => Context.DisposeAsync();
    }
}
