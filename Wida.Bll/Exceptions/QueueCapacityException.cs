namespace Wida.Bll.Exceptions;

public sealed class QueueCapacityException(string message) : Exception(message);
