namespace JmServer.Protocol;

public sealed class WireProtocolException : Exception
{
    public WireProtocolException(string message)
        : base(message)
    {
    }
}
