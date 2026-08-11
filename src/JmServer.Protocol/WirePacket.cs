using JmServer.Contracts;
using SuperSocket.ProtoBase;

namespace JmServer.Protocol;

public sealed class WirePacket : IKeyedPackageInfo<ushort>
{
    public WirePacket(
        ushort protocolVersion,
        MessageType messageType,
        Guid correlationId,
        ReadOnlyMemory<byte> body)
    {
        ProtocolVersion = protocolVersion;
        MessageType = messageType;
        CorrelationId = correlationId;
        Body = body;
    }

    public ushort ProtocolVersion { get; }

    public MessageType MessageType { get; }

    public Guid CorrelationId { get; }

    public ReadOnlyMemory<byte> Body { get; }

    public ushort Key => (ushort)MessageType;

    public static WirePacket Create<T>(MessageType messageType, Guid correlationId, T payload) =>
        new(
            ProtocolConstants.CurrentVersion,
            messageType,
            correlationId,
            PacketPayload.SerializeJson(payload));

    public static WirePacket CreateWithBinary<T>(
        MessageType messageType,
        Guid correlationId,
        T metadata,
        ReadOnlySpan<byte> binary) =>
        new(
            ProtocolConstants.CurrentVersion,
            messageType,
            correlationId,
            PacketPayload.SerializeWithBinary(metadata, binary));
}
