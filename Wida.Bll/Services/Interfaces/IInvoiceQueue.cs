using Wida.Bll.Dtos.Processing;

namespace Wida.Bll.Services.Interfaces;

public interface IInvoiceQueue
{
    Task<ProcessingRunResponse> EnqueueAsync(Guid documentId, CancellationToken cancellationToken = default, bool reanalyze = false);
}
