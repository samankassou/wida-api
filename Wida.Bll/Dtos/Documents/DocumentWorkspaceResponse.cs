using Wida.Bll.Dtos.Invoices;
using Wida.Bll.Dtos.Processing;

namespace Wida.Bll.Dtos.Documents;

public record DocumentWorkspaceResponse(
    DocumentResponse Document,
    InvoiceResponse? Invoice,
    ProcessingRunResponse? LatestRun);
