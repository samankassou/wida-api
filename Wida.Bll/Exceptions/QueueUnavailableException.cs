namespace Wida.Bll.Exceptions;

public sealed class QueueUnavailableException(Exception inner)
    : Exception("The analysis queue is temporarily unavailable. Your document is saved; please try again later.", inner);
