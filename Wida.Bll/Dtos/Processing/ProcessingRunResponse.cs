using Wida.Dal.Enums;

namespace Wida.Bll.Dtos.Processing;

public record ProcessingRunResponse(
    Guid Id,
    Guid DocumentId,
    ProcessingStatus Status,
    string Processor,
    string? ProcessorVersion,
    DateTime StartedAt,
    DateTime? CompletedAt,
    string? ErrorCode,
    string? ErrorMessage
);