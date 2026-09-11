using System.Text;
using RabbitMQ.Client;
using Wida.Bll.Exceptions;
using Wida.Bll.Services.Interfaces;

namespace Wida.Api.Processing;

public sealed class RabbitMqTransport(IConfiguration configuration) : IAnalysisJobPublisher
{
    public string QueueName => configuration["RabbitMQ:QueueName"] ?? "wida.invoice-analysis.v1";
    public string DeadLetterQueue => QueueName + ".failed";

    public async Task<IConnection> ConnectAsync(CancellationToken token)
    {
        var uri = configuration["RabbitMQ:Uri"];
        if (string.IsNullOrWhiteSpace(uri))
            throw new InvalidOperationException("Configure RabbitMQ:Uri for analysis messaging.");
        var factory = new ConnectionFactory
        {
            Uri = new Uri(uri), AutomaticRecoveryEnabled = false,
            RequestedHeartbeat = TimeSpan.FromSeconds(10),
            RequestedConnectionTimeout = TimeSpan.FromSeconds(10),
            ConsumerDispatchConcurrency = 1
        };
        // Recovery is explicit: a lost consumer connection cancels processing before reconnect.
        return await factory.CreateConnectionAsync(token);
    }

    public async Task DeclareAsync(IChannel channel, CancellationToken token)
    {
        await channel.QueueDeclareAsync(DeadLetterQueue, durable: true, exclusive: false, autoDelete: false,
            arguments: new Dictionary<string, object?> { ["x-queue-type"] = "quorum" }, cancellationToken: token);
        await channel.QueueDeclareAsync(QueueName, durable: true, exclusive: false, autoDelete: false,
            arguments: new Dictionary<string, object?>
            {
                ["x-queue-type"] = "quorum",
                ["x-single-active-consumer"] = true,
                ["x-dead-letter-exchange"] = "",
                ["x-dead-letter-routing-key"] = DeadLetterQueue,
                ["x-dead-letter-strategy"] = "at-least-once",
                ["x-overflow"] = "reject-publish",
                ["x-max-length"] = 1000,
                // Database outages must not silently exhaust Rabbit's default delivery limit.
                // Azure failures have a separate bounded retry policy and end in the DLQ.
                ["x-delivery-limit"] = -1,
                ["x-consumer-timeout"] = 86400000
            }, cancellationToken: token);
    }

    public async Task PublishAsync(Guid runId, CancellationToken cancellationToken = default)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(15));
        try
        {
            await using var connection = await ConnectAsync(timeout.Token);
            await using var channel = await connection.CreateChannelAsync(
                new CreateChannelOptions(publisherConfirmationsEnabled: true, publisherConfirmationTrackingEnabled: true), timeout.Token);
            await DeclareAsync(channel, timeout.Token);
            var properties = new BasicProperties
            {
                Persistent = true, ContentType = "text/plain", Type = "wida.invoice-analysis.v1",
                MessageId = runId.ToString()
            };
            await channel.BasicPublishAsync("", QueueName, mandatory: true, basicProperties: properties,
                body: Encoding.UTF8.GetBytes(runId.ToString()), cancellationToken: timeout.Token);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception ex) { throw new QueueUnavailableException(ex); }
    }
}
