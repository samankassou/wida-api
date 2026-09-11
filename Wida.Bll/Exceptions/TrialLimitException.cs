namespace Wida.Bll.Exceptions;
public sealed class TrialLimitException(string message, int status = 429) : Exception(message)
{
    public int Status { get; } = status;
}
