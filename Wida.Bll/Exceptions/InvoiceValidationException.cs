namespace Wida.Bll.Exceptions;

public sealed class InvoiceValidationException(IReadOnlyDictionary<string, string[]> errors)
    : Exception("Some invoice fields need attention.")
{
    public IReadOnlyDictionary<string, string[]> Errors { get; } = errors;
}
