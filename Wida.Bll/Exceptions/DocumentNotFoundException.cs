namespace Wida.Bll.Exceptions;

public sealed class DocumentNotFoundException(Guid documentId)
    : Exception($"Document '{documentId}' was not found.");
