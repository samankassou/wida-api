using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using RabbitMQ.Client;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using Wida.Api.Processing;
using Wida.Bll.Services.Implementations;
using Wida.Dal.Entities;
using Wida.Dal.Enums;
using Wida.Dal.Models;
using Wida.Dal.Persistence;
using Wida.Dal.Services.Interfaces;

namespace Wida.Tests;

public sealed class PostgresQueueFactAttribute : FactAttribute
{
    public PostgresQueueFactAttribute()
    {
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("WIDA_QUEUE_TEST_POSTGRES")))
            Skip = "Set WIDA_QUEUE_TEST_POSTGRES to a disposable PostgreSQL server to run queue integration tests.";
    }
}

public sealed class RabbitMqQueueFactAttribute : FactAttribute
{
    public RabbitMqQueueFactAttribute()
    {
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("WIDA_QUEUE_TEST_POSTGRES"))
            || string.IsNullOrEmpty(Environment.GetEnvironmentVariable("WIDA_QUEUE_TEST_RABBITMQ")))
            Skip = "Set WIDA_QUEUE_TEST_POSTGRES and WIDA_QUEUE_TEST_RABBITMQ to disposable test services.";
    }
}

public sealed class PostgresQueueTests
{
    [PostgresQueueFact]
    public async Task Concurrent_admission_returns_one_durable_run_and_enforces_user_capacity()
    {
        await using var f = await Fixture.CreateAsync();
        var responses = await Task.WhenAll(Enumerable.Range(0, 8).Select(async _ =>
        {
            await using var db = f.Open();
            return await new InvoiceQueue(db, new RecordingJobPublisher()).EnqueueAsync(f.DocumentId);
        }));
        Assert.Single(responses.Select(x => x.Id).Distinct());
        var ids = new List<Guid>();
        await using (var db = f.Open())
        {
            for (var i = 0; i < 8; i++) { var doc = new Document { PageCount = 1 }; db.Documents.Add(doc); ids.Add(doc.Id); }
            await db.SaveChangesAsync();
        }
        var accepted = await Task.WhenAll(ids.Select(async id =>
        {
            await using var db = f.Open();
            try { await new InvoiceQueue(db, new RecordingJobPublisher()).EnqueueAsync(id); return true; }
            catch (Wida.Bll.Exceptions.QueueCapacityException) { return false; }
        }));
        Assert.Equal(0, accepted.Count(x => x));
        await using var check = f.Open();
        Assert.Equal(1, await check.ProcessingRuns.CountAsync());
    }

    [PostgresQueueFact]
    public async Task Concurrent_owners_cannot_overbook_the_last_monthly_page()
    {
        await using var f = await Fixture.CreateAsync();
        var otherOwner = Guid.NewGuid();
        var otherDocument = Guid.NewGuid();
        await using (var db = f.Open())
        {
            db.Users.Add(new AppUser { Id = otherOwner, GoogleSubject = "other-trial" });
            db.AnalysisBudgets.Add(new AnalysisBudget { Id = TrialBudget.Month, PagesUsed = 399 });
            await db.SaveChangesAsync();
        }
        await using (var db = new WidaDbContext(f.Options, new TestCurrentUser(otherOwner)))
        {
            db.Documents.Add(new Document { Id = otherDocument, PageCount = 1 });
            await db.SaveChangesAsync();
        }
        async Task<bool> Admit(bool other)
        {
            await using var db = other ? new WidaDbContext(f.Options, new TestCurrentUser(otherOwner)) : f.Open();
            try { await new InvoiceQueue(db, new RecordingJobPublisher()).EnqueueAsync(other ? otherDocument : f.DocumentId); return true; }
            catch (Wida.Bll.Exceptions.TrialLimitException) { return false; }
        }
        var results = await Task.WhenAll(Admit(false), Admit(true));
        Assert.Single(results, x => x);
        await using var check = f.Open();
        Assert.Equal(400, (await check.AnalysisBudgets.SingleAsync()).PagesUsed);
        Assert.Equal(1, await check.Users.SumAsync(x => x.AnalysisPagesUsed));
    }

    [RabbitMqQueueFact]
    public async Task Two_workers_coordinate_and_a_new_leader_resumes_the_saved_operation()
    {
        await using var f = await Fixture.CreateAsync();
        var transport = CreateTransport();
        await using (var db = f.Open())
        {
            var queue = new InvoiceQueue(db, transport);
            var run = await queue.EnqueueAsync(f.DocumentId);
            await transport.PublishAsync(run.Id); // Deliberate duplicate delivery/replay.
        }
        var azure = new FakeAzure();
        using var services = new ServiceCollection()
            .AddSingleton(f.Options)
            .AddSingleton<IQueuedDocumentAnalyzer>(azure)
            .BuildServiceProvider();
        using var first = new InvoiceQueueWorker(services.GetRequiredService<IServiceScopeFactory>(), transport, NullLogger<InvoiceQueueWorker>.Instance);
        using var second = new InvoiceQueueWorker(services.GetRequiredService<IServiceScopeFactory>(), transport, NullLogger<InvoiceQueueWorker>.Instance);
        await first.StartAsync(default);
        try
        {
            await WaitUntil(async () => { await using var db = f.Open(); return await db.ProcessingRuns.AnyAsync(x => x.AzureOperationId != null); });
            await second.StartAsync(default);
            // The second instance must not issue requests while RabbitMQ considers the first consumer active.
            await Task.Delay(2500);
            Assert.Equal(1, azure.Submissions);
            await first.StopAsync(default);
            azure.Complete = true;
            await WaitUntil(async () => { await using var db = f.Open(); return await db.ProcessingRuns.AnyAsync(x => x.Status == ProcessingStatus.Completed); });
            Assert.Equal(1, azure.Submissions);
            Assert.True(azure.Polls > 0);
            await using var check = f.Open();
            Assert.Equal(DocumentStatus.ReviewRequired, (await check.Documents.SingleAsync()).Status);
        }
        finally { await first.StopAsync(default); await second.StopAsync(default); await DeleteQueues(transport); }
    }

    [RabbitMqQueueFact]
    public async Task Rolled_back_publication_is_dead_lettered_and_never_reaches_Azure()
    {
        await using var f = await Fixture.CreateAsync();
        var transport = CreateTransport();
        var azure = new FakeAzure();
        using var services = new ServiceCollection().AddSingleton(f.Options)
            .AddSingleton<IQueuedDocumentAnalyzer>(azure).BuildServiceProvider();
        using var worker = new InvoiceQueueWorker(services.GetRequiredService<IServiceScopeFactory>(), transport,
            NullLogger<InvoiceQueueWorker>.Instance);
        await worker.StartAsync(default);
        try
        {
            await using (var db = f.Open())
                await Assert.ThrowsAsync<InvalidOperationException>(() =>
                    new InvoiceQueue(db, new PublishThenFail(transport)).EnqueueAsync(f.DocumentId));
            await using var connection = await transport.ConnectAsync(default);
            await using var channel = await connection.CreateChannelAsync();
            await transport.DeclareAsync(channel, default);
            await WaitUntil(async () => (await channel.QueueDeclarePassiveAsync(transport.DeadLetterQueue)).MessageCount == 1);
            var failed = await channel.BasicGetAsync(transport.DeadLetterQueue, autoAck: true);
            Assert.NotNull(failed);
            Assert.Equal(0, azure.Submissions);
            await using var check = f.Open();
            Assert.Empty(await check.ProcessingRuns.ToListAsync());
            Assert.Equal(DocumentStatus.Uploaded, (await check.Documents.SingleAsync()).Status);
        }
        finally { await worker.StopAsync(default); await DeleteQueues(transport); }
    }

    private sealed class PublishThenFail(RabbitMqTransport transport) : Wida.Bll.Services.Interfaces.IAnalysisJobPublisher
    {
        public async Task PublishAsync(Guid runId, CancellationToken cancellationToken = default)
        {
            await transport.PublishAsync(runId, cancellationToken);
            throw new InvalidOperationException("Simulated admission failure after broker confirmation.");
        }
    }

    private static RabbitMqTransport CreateTransport() => new(new ConfigurationBuilder().AddInMemoryCollection(
        new Dictionary<string, string?>
        {
            ["RabbitMQ:Uri"] = Environment.GetEnvironmentVariable("WIDA_QUEUE_TEST_RABBITMQ"),
            ["RabbitMQ:QueueName"] = "wida.test." + Guid.NewGuid().ToString("N")
        }).Build());

    private static async Task DeleteQueues(RabbitMqTransport transport)
    {
        await using var connection = await transport.ConnectAsync(default);
        await using var channel = await connection.CreateChannelAsync();
        await channel.QueueDeleteAsync(transport.QueueName, ifUnused: false, ifEmpty: false);
        await channel.QueueDeleteAsync(transport.DeadLetterQueue, ifUnused: false, ifEmpty: false);
    }

    private static async Task WaitUntil(Func<Task<bool>> predicate)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        while (!await predicate()) await Task.Delay(100, timeout.Token);
    }

    private sealed class FakeAzure : IQueuedDocumentAnalyzer
    {
        private int submissions;
        private int polls;
        public volatile bool Complete;
        public int Submissions => Volatile.Read(ref submissions);
        public int Polls => Volatile.Read(ref polls);
        private readonly string operation = Guid.NewGuid().ToString();
        public Task<string> SubmitAsync(string path, CancellationToken token)
        { Interlocked.Increment(ref submissions); return Task.FromResult(operation); }
        public Task<DocumentAnalysisResult?> PollAsync(string id, CancellationToken token)
        {
            Assert.Equal(operation, id); Interlocked.Increment(ref polls);
            return Task.FromResult(Complete ? new DocumentAnalysisResult { RawResult = "{}" } : null);
        }
    }

    private sealed class Fixture : IAsyncDisposable
    {
        private readonly string admin;
        private readonly string database = "wida_queue_test_" + Guid.NewGuid().ToString("N");
        public DbContextOptions<WidaDbContext> Options { get; }
        public Guid DocumentId { get; } = Guid.NewGuid();
        private Fixture(string connection)
        {
            admin = connection;
            var settings = new NpgsqlConnectionStringBuilder(connection) { Database = database };
            Options = new DbContextOptionsBuilder<WidaDbContext>().UseNpgsql(settings.ConnectionString).Options;
        }
        public static async Task<Fixture> CreateAsync()
        {
            var f = new Fixture(Environment.GetEnvironmentVariable("WIDA_QUEUE_TEST_POSTGRES")!);
            await using var connection = new NpgsqlConnection(f.admin); await connection.OpenAsync();
            await using var create = new NpgsqlCommand($"CREATE DATABASE {f.database}", connection); await create.ExecuteNonQueryAsync();
            try
            {
                await using var db = f.Open();
                await db.Database.MigrateAsync();
                db.Users.Add(new AppUser { Id = TestCurrentUser.Default.UserId!.Value, GoogleSubject = "queue-integration" });
                db.Documents.Add(new Document { Id = f.DocumentId, PageCount = 1 });
                await db.SaveChangesAsync();
                return f;
            }
            catch { await f.DisposeAsync(); throw; }
        }
        public WidaDbContext Open() => new(Options, TestCurrentUser.Default);
        public async ValueTask DisposeAsync()
        {
            await using var connection = new NpgsqlConnection(admin); await connection.OpenAsync();
            await using var drop = new NpgsqlCommand($"DROP DATABASE IF EXISTS {database} WITH (FORCE)", connection);
            await drop.ExecuteNonQueryAsync();
        }
    }
}
