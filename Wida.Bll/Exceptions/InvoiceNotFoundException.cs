namespace Wida.Bll.Exceptions;

public sealed class InvoiceNotFoundException(Guid id) : Exception($"Invoice '{id}' was not found.");
