namespace Wida.Bll.Services.Interfaces;

public interface IAnalysisJobPublisher
{
    // Returns only after the broker confirms durable acceptance.
    Task PublishAsync(Guid runId, CancellationToken cancellationToken = default);
}
