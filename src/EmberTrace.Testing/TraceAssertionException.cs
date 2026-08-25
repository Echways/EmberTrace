namespace EmberTrace.Testing;

public sealed class TraceAssertionException : Exception
{
    public TraceAssertionException(string message) : base(message)
    {
    }

    public TraceAssertionException(string message, Exception innerException) : base(message, innerException)
    {
    }
}
