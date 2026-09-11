using Wida.Bll.Services.Interfaces;

namespace Wida.Tests;

internal sealed class RecordingJobPublisher : IAnalysisJobPublisher
{
    public List<Guid> Published { get; } = [];
    public Task PublishAsync(Guid runId, CancellationToken cancellationToken = default)
    { Published.Add(runId); return Task.CompletedTask; }
}
