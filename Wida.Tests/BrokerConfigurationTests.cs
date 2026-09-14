using Microsoft.Extensions.Configuration;
using Wida.Api.Processing;

namespace Wida.Tests;

public sealed class BrokerConfigurationTests
{
    [Theory]
    [InlineData("quorum")]
    [InlineData("classic")]
    public void Shared_broker_mode_preserves_single_consumer_capacity_and_failure_routing(string type)
    {
        var broker = new RabbitMqTransport(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            { ["RabbitMQ:QueueType"] = type }).Build());
        var args = broker.QueueArguments();
        Assert.Equal(type, args["x-queue-type"]);
        Assert.Equal(true, args["x-single-active-consumer"]);
        Assert.Equal("reject-publish", args["x-overflow"]);
        Assert.Equal(broker.DeadLetterQueue, args["x-dead-letter-routing-key"]);
        Assert.Equal(type == "quorum", args.ContainsKey("x-dead-letter-strategy"));
        Assert.Equal(type == "quorum", args.ContainsKey("x-delivery-limit"));
        Assert.Single(broker.QueueArguments(failed: true));
    }
}
