using System.Text;
using Microsoft.EntityFrameworkCore;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using Wida.Bll.Services.Implementations;
using Wida.Dal.Enums;
using Wida.Dal.Persistence;
using Wida.Dal.Services.Interfaces;

namespace Wida.Api.Processing;

public sealed class InvoiceQueueWorker(IServiceScopeFactory scopes, RabbitMqTransport transport,
    ILogger<InvoiceQueueWorker> logger) : BackgroundService
{
    private sealed record QueueUser(Guid? UserId) : ICurrentUser;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try { await ConsumeAsync(stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception ex) { logger.LogError(ex, "RabbitMQ analysis consumer interrupted; retrying in five seconds."); }
            try { await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
        }
    }

    private async Task ConsumeAsync(CancellationToken stoppingToken)
    {
        using var session = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
        await using var connection = await transport.ConnectAsync(stoppingToken);
        await using var channel = await connection.CreateChannelAsync(cancellationToken: stoppingToken);
        connection.ConnectionShutdownAsync += (_, _) => { session.Cancel(); return Task.CompletedTask; };
        channel.ChannelShutdownAsync += (_, _) => { session.Cancel(); return Task.CompletedTask; };
        await transport.DeclareAsync(channel, session.Token);
        await channel.BasicQosAsync(0, 1, global: false, cancellationToken: session.Token);
        var consumer = new AsyncEventingBasicConsumer(channel);
        consumer.UnregisteredAsync += (_, _) => { session.Cancel(); return Task.CompletedTask; };
        consumer.ShutdownAsync += (_, _) => { session.Cancel(); return Task.CompletedTask; };
        consumer.ReceivedAsync += async (_, delivery) =>
        {
            try
            {
                // Copy/parse the delivery body before returning from this handler.
                if (delivery.Body.Length > 64 || !Guid.TryParse(Encoding.UTF8.GetString(delivery.Body.Span), out var runId))
                {
                    await channel.BasicRejectAsync(delivery.DeliveryTag, requeue: false, session.Token);
                    return;
                }
                var completed = await ProcessAsync(runId, session.Token);
                if (completed) await channel.BasicAckAsync(delivery.DeliveryTag, multiple: false, session.Token);
                else await channel.BasicRejectAsync(delivery.DeliveryTag, requeue: false, session.Token);
            }
            catch (OperationCanceledException) when (session.IsCancellationRequested) { }
            catch (Exception ex)
            {
                // Do not ack database/network failures. Closing the channel requeues the message.
                logger.LogError(ex, "Analysis delivery interrupted; it will be redelivered.");
                session.Cancel();
            }
        };
        await channel.BasicConsumeAsync(transport.QueueName, autoAck: false, consumer, session.Token);
        logger.LogInformation("RabbitMQ analysis consumer registered (single active consumer, prefetch 1).");
        try { await Task.Delay(Timeout.Infinite, session.Token); }
        catch (OperationCanceledException) when (session.IsCancellationRequested) { }
        // Await channel disposal/consumer dispatch completion before establishing another session.
    }

    private async Task<bool> ProcessAsync(Guid runId, CancellationToken token)
    {
        using var scope = scopes.CreateScope();
        var options = scope.ServiceProvider.GetRequiredService<DbContextOptions<WidaDbContext>>();
        Guid? owner;
        await using (var lookup = new WidaDbContext(options, new QueueUser(null)))
        {
            // Admission publishes before commit. This short barrier waits for its commit/rollback;
            // it is not a work queue or a worker election lock, and no job list is scanned.
            await using var transaction = await lookup.Database.BeginTransactionAsync(token);
            if (lookup.Database.IsNpgsql())
                await lookup.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(73190421)", token);
            owner = await lookup.ProcessingRuns.IgnoreQueryFilters().Where(x => x.Id == runId && x.IsBackgroundJob)
                .Select(x => (Guid?)x.Document.OwnerUserId).SingleOrDefaultAsync(token);
            await transaction.CommitAsync(token);
        }
        if (owner is null) return false; // Invalid ID or publication whose admission rolled back.
        while (true)
        {
            // Includes a delay after takeover/redelivery so both POST and GET obey the F0 cadence.
            await Task.Delay(TimeSpan.FromSeconds(2), token);
            await using var owned = new WidaDbContext(options, new QueueUser(owner));
            var run = await owned.ProcessingRuns.AsNoTracking().SingleOrDefaultAsync(x => x.Id == runId, token);
            if (run is null) return false;
            if (run.Status == ProcessingStatus.Completed) return true;
            if (run.Status == ProcessingStatus.Failed) return false;
            if (run.NextAttemptAt > DateTime.UtcNow) continue;
            var analyzer = scope.ServiceProvider.GetRequiredService<IQueuedDocumentAnalyzer>();
            await new InvoiceQueueExecutor(owned, analyzer).StepAsync(runId, token);
        }
    }
}
