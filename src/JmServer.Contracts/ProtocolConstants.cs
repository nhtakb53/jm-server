namespace JmServer.Contracts;

public static class ProtocolConstants
{
    public const ushort CurrentVersion = 6;
    public const int MaxPacketBodyLength = 9 * 1024 * 1024;
    public const int MaxMetadataLength = 64 * 1024;
    public const int MaxBinaryPayloadLength = 8 * 1024 * 1024;
}
