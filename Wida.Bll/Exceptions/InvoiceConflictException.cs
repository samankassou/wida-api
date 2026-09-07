namespace Wida.Bll.Exceptions;

public sealed class InvoiceConflictException(string message) : Exception(message);
