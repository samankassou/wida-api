using Wida.Dal.Enums;

namespace Wida.Bll.Dtos.Documents;

public record DocumentResponse(
    Guid Id,
    string OriginalFileName,
    string ContentType,
    DocumentType DocumentType,
    DocumentStatus Status,
    DateTime UploadedAt,
    int PageCount = 0
);