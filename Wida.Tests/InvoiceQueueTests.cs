using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Wida.Bll.Exceptions;
using Wida.Bll.Services.Implementations;
using Wida.Dal.Entities;
using Wida.Dal.Enums;
using Wida.Dal.Models;
using Wida.Dal.Persistence;
using Wida.Dal.Services.Interfaces;

namespace Wida.Tests;

public sealed class InvoiceQueueTests
{
    [Fact]
    public async Task Enqueue_is_durable_and_duplicate_requests_return_the_same_run()
    {
        await using var f = new Fixture();
        var publisher = new RecordingJobPublisher();
        var first = await new InvoiceQueue(f.Db, publisher).EnqueueAsync(f.Document.Id);
        await using var reopened = f.Open();
        var second = await new InvoiceQueue(reopened, publisher).EnqueueAsync(f.Document.Id);
        Assert.Single(publisher.Published);
        Assert.Equal(first.Id, second.Id);
        Assert.Equal(ProcessingStatus.Pending, first.Status);
        Assert.Equal(DocumentStatus.Queued, (await reopened.Documents.SingleAsync()).Status);
        Assert.Single(await reopened.ProcessingRuns.ToListAsync());
    }

    [Fact]
    public async Task Another_owner_cannot_enqueue_or_execute_a_document()
    {
        await using var f = new Fixture();
        var run = await new InvoiceQueue(f.Db, new RecordingJobPublisher()).EnqueueAsync(f.Document.Id);
        await using var other = f.Open(Guid.NewGuid());
        await Assert.ThrowsAsync<DocumentNotFoundException>(() => new InvoiceQueue(other, new RecordingJobPublisher()).EnqueueAsync(f.Document.Id));
        var azure = new FakeAzure();
        await Assert.ThrowsAsync<InvalidOperationException>(() => new InvoiceQueueExecutor(other, azure).StepAsync(run.Id, default));
        Assert.Equal(0, azure.Submissions);
    }

    [Fact]
    public async Task Admission_limits_each_user_to_three_jobs_but_allows_idempotent_retries()
    {
        await using var f = new Fixture();
        var queue = new InvoiceQueue(f.Db, new RecordingJobPublisher());
        var first = await queue.EnqueueAsync(f.Document.Id);
        for (var i = 0; i < 2; i++)
        {
            var doc = new Document(); f.Db.Documents.Add(doc); await f.Db.SaveChangesAsync();
            await queue.EnqueueAsync(doc.Id);
        }
        Assert.Equal(first.Id, (await queue.EnqueueAsync(f.Document.Id)).Id);
        var fourth = new Document(); f.Db.Documents.Add(fourth); await f.Db.SaveChangesAsync();
        await Assert.ThrowsAsync<QueueCapacityException>(() => queue.EnqueueAsync(fourth.Id));
        Assert.Equal(3, await f.Db.ProcessingRuns.CountAsync());
    }

    [Fact]
    public async Task A_restarted_worker_polls_the_saved_operation_without_resubmitting_and_preserves_saved_status()
    {
        await using var f = new Fixture();
        f.Document.Status = DocumentStatus.Saved; await f.Db.SaveChangesAsync();
        var run = await new InvoiceQueue(f.Db, new RecordingJobPublisher()).EnqueueAsync(f.Document.Id);
        var azure = new FakeAzure();
        await new InvoiceQueueExecutor(f.Db, azure).StepAsync(run.Id, default);
        Assert.Equal(1, azure.Submissions);
        Assert.Equal(azure.OperationId, (await f.Db.ProcessingRuns.SingleAsync()).AzureOperationId);
        await f.MakeDue(run.Id);
        await using var restarted = f.Open();
        azure.Result = new DocumentAnalysisResult { RawResult = "{}", Fields = [new AnalyzedField { Name = "VendorName", RawValue = "Supplier", Confidence = 0.9m }] };
        await new InvoiceQueueExecutor(restarted, azure).StepAsync(run.Id, default);
        Assert.Equal(1, azure.Submissions); Assert.Equal(1, azure.Polls);
        await using var check = f.Open();
        Assert.Equal(ProcessingStatus.Completed, (await check.ProcessingRuns.SingleAsync()).Status);
        Assert.Equal(DocumentStatus.Saved, (await check.Documents.SingleAsync()).Status);
        Assert.Single(await check.ExtractedFields.ToListAsync());
    }

    [Fact]
    public async Task Crash_between_submission_marker_and_operation_save_never_resubmits()
    {
        await using var f = new Fixture();
        var response = await new InvoiceQueue(f.Db, new RecordingJobPublisher()).EnqueueAsync(f.Document.Id);
        var run = await f.Db.ProcessingRuns.SingleAsync();
        run.SubmissionStartedAt = DateTime.UtcNow; run.Status = ProcessingStatus.Running;
        await f.Db.SaveChangesAsync();
        var azure = new FakeAzure();
        await using var restarted = f.Open();
        await new InvoiceQueueExecutor(restarted, azure).StepAsync(response.Id, default);
        Assert.Equal(0, azure.Submissions);
        Assert.Equal("ANALYSIS_SUBMISSION_UNCERTAIN", (await restarted.ProcessingRuns.SingleAsync()).ErrorCode);
    }

    [Fact]
    public async Task Rate_limited_submission_honours_retry_after_and_retries_are_bounded()
    {
        await using var f = new Fixture();
        var response = await new InvoiceQueue(f.Db, new RecordingJobPublisher()).EnqueueAsync(f.Document.Id);
        var azure = new FakeAzure { SubmitError = new AnalysisRequestException(429, TimeSpan.FromSeconds(120)) };
        await new InvoiceQueueExecutor(f.Db, azure).StepAsync(response.Id, default);
        var run = await f.Db.ProcessingRuns.SingleAsync();
        Assert.Null(run.SubmissionStartedAt);
        Assert.Equal(ProcessingStatus.Pending, run.Status);
        Assert.True(run.NextAttemptAt > DateTime.UtcNow.AddSeconds(110));
        for (var i = 0; i < 5; i++)
        {
            await f.MakeDue(response.Id);
            await using var retry = f.Open();
            await new InvoiceQueueExecutor(retry, azure).StepAsync(response.Id, default);
        }
        await using var check = f.Open();
        Assert.Equal(6, azure.Submissions);
        Assert.Equal(ProcessingStatus.Failed, (await check.ProcessingRuns.SingleAsync()).Status);
    }

    [Fact]
    public async Task Network_error_during_submission_is_not_retried()
    {
        await using var f = new Fixture();
        var response = await new InvoiceQueue(f.Db, new RecordingJobPublisher()).EnqueueAsync(f.Document.Id);
        var azure = new FakeAzure { SubmitError = new HttpRequestException("lost response") };
        await new InvoiceQueueExecutor(f.Db, azure).StepAsync(response.Id, default);
        await new InvoiceQueueExecutor(f.Db, azure).StepAsync(response.Id, default);
        Assert.Equal(1, azure.Submissions);
        Assert.Equal("ANALYSIS_SUBMISSION_UNCERTAIN", (await f.Db.ProcessingRuns.SingleAsync()).ErrorCode);
    }

    [Fact]
    public async Task Shutdown_during_poll_preserves_the_known_operation_for_restart()
    {
        await using var f = new Fixture();
        var response = await new InvoiceQueue(f.Db, new RecordingJobPublisher()).EnqueueAsync(f.Document.Id);
        var azure = new FakeAzure();
        await new InvoiceQueueExecutor(f.Db, azure).StepAsync(response.Id, default);
        await f.MakeDue(response.Id);
        using var shutdown = new CancellationTokenSource();
        azure.OnPoll = () => { shutdown.Cancel(); throw new OperationCanceledException(shutdown.Token); };
        await using (var restarting = f.Open())
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new InvoiceQueueExecutor(restarting, azure).StepAsync(response.Id, shutdown.Token));
        await using var check = f.Open();
        var run = await check.ProcessingRuns.SingleAsync();
        Assert.Equal(ProcessingStatus.Running, run.Status);
        Assert.Equal(azure.OperationId, run.AzureOperationId);
        Assert.Equal(1, azure.Submissions);
    }

    [Fact]
    public async Task Transient_poll_failure_retries_get_only()
    {
        await using var f = new Fixture();
        var response = await new InvoiceQueue(f.Db, new RecordingJobPublisher()).EnqueueAsync(f.Document.Id);
        var azure = new FakeAzure();
        await new InvoiceQueueExecutor(f.Db, azure).StepAsync(response.Id, default);
        await f.MakeDue(response.Id);
        azure.OnPoll = () => throw new AnalysisRequestException(503);
        await using (var retry = f.Open()) await new InvoiceQueueExecutor(retry, azure).StepAsync(response.Id, default);
        await using var check = f.Open();
        var run = await check.ProcessingRuns.SingleAsync();
        Assert.Equal(ProcessingStatus.Running, run.Status);
        Assert.Equal(1, run.RetryCount);
        Assert.Equal(azure.OperationId, run.AzureOperationId);
        Assert.Equal(1, azure.Submissions);
    }

    [Fact]
    public async Task Broker_unavailability_rolls_back_admission_without_losing_the_document()
    {
        await using var f = new Fixture();
        await Assert.ThrowsAsync<QueueUnavailableException>(() =>
            new InvoiceQueue(f.Db, new UnavailablePublisher()).EnqueueAsync(f.Document.Id));
        await using var persisted = f.Open();
        Assert.Empty(await persisted.ProcessingRuns.ToListAsync());
        Assert.Equal(DocumentStatus.Uploaded, (await persisted.Documents.SingleAsync()).Status);
    }

    private sealed class UnavailablePublisher : Wida.Bll.Services.Interfaces.IAnalysisJobPublisher
    {
        public Task PublishAsync(Guid runId, CancellationToken cancellationToken = default) =>
            throw new QueueUnavailableException(new IOException("Simulated broker outage"));
    }

    private sealed class FakeAzure : IQueuedDocumentAnalyzer
    {
        public string OperationId { get; } = Guid.NewGuid().ToString();
        public int Submissions { get; private set; }
        public int Polls { get; private set; }
        public Exception? SubmitError { get; init; }
        public Action? OnPoll { get; set; }
        public DocumentAnalysisResult? Result { get; set; }
        public Task<string> SubmitAsync(string filePath, CancellationToken token)
        { Submissions++; if (SubmitError is not null) throw SubmitError; return Task.FromResult(OperationId); }
        public Task<DocumentAnalysisResult?> PollAsync(string operationId, CancellationToken token)
        { Assert.Equal(OperationId, operationId); Polls++; OnPoll?.Invoke(); return Task.FromResult(Result); }
    }

    private sealed class Fixture : IAsyncDisposable
    {
        private readonly SqliteConnection connection = new("Data Source=:memory:");
        private readonly DbContextOptions<WidaDbContext> options;
        public WidaDbContext Db { get; }
        public Document Document { get; } = new() { StoragePath = "/test/invoice.pdf" };
        public Fixture()
        {
            connection.Open();
            options = new DbContextOptionsBuilder<WidaDbContext>().UseSqlite(connection).Options;
            Db = Open(); Db.Database.EnsureCreated();
            Db.Users.Add(new AppUser { Id = TestCurrentUser.Default.UserId!.Value, GoogleSubject = "queue-user" });
            Db.Documents.Add(Document); Db.SaveChanges();
        }
        public WidaDbContext Open(Guid? userId = null) => new(options, new TestCurrentUser(userId ?? TestCurrentUser.Default.UserId));
        public async Task MakeDue(Guid runId)
        {
            await using var db = Open();
            (await db.ProcessingRuns.SingleAsync(x => x.Id == runId)).NextAttemptAt = DateTime.UtcNow.AddSeconds(-1);
            await db.SaveChangesAsync();
        }
        public async ValueTask DisposeAsync() { await Db.DisposeAsync(); await connection.DisposeAsync(); }
    }
}
