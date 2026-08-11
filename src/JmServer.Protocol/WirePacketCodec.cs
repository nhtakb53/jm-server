using System.Buffers.Binary;
using JmServer.Contracts;

namespace JmServer.Protocol;

public static class WirePacketCodec
{
    private static ReadOnlySpan<byte> Magic => "JMS1"u8;

    public const int HeaderLength = 28;

    public static byte[] Encode(WirePacket packet)
    {
        if (packet.Body.Length > ProtocolConstants.MaxPacketBodyLength)
        {
            throw new WireProtocolException(
                $"Packet body is {packet.Body.Length} bytes; maximum is {ProtocolConstants.MaxPacketBodyLength}.");
        }

        var output = GC.AllocateUninitializedArray<byte>(HeaderLength + packet.Body.Length);
        Magic.CopyTo(output);
        BinaryPrimitives.WriteUInt16BigEndian(output.AsSpan(4, 2), packet.ProtocolVersion);
        BinaryPrimitives.WriteUInt16BigEndian(output.AsSpan(6, 2), (ushort)packet.MessageType);
        packet.CorrelationId.TryWriteBytes(output.AsSpan(8, 16));
        BinaryPrimitives.WriteInt32BigEndian(output.AsSpan(24, 4), packet.Body.Length);
        packet.Body.Span.CopyTo(output.AsSpan(HeaderLength));
        return output;
    }

    public static WirePacket Decode(ReadOnlySpan<byte> frame)
    {
        if (frame.Length < HeaderLength)
        {
            throw new WireProtocolException("Packet is shorter than the fixed header.");
        }

        if (!frame[..4].SequenceEqual(Magic))
        {
            throw new WireProtocolException("Packet magic is invalid.");
        }

        var bodyLength = BinaryPrimitives.ReadInt32BigEndian(frame.Slice(24, 4));
        if (bodyLength is < 0 or > ProtocolConstants.MaxPacketBodyLength)
        {
            throw new WireProtocolException($"Packet body length {bodyLength} is invalid.");
        }

        if (frame.Length != HeaderLength + bodyLength)
        {
            throw new WireProtocolException(
                $"Packet length mismatch. Declared {bodyLength}; received {frame.Length - HeaderLength}.");
        }

        var protocolVersion = BinaryPrimitives.ReadUInt16BigEndian(frame.Slice(4, 2));
        var messageType = (MessageType)BinaryPrimitives.ReadUInt16BigEndian(frame.Slice(6, 2));
        var correlationId = new Guid(frame.Slice(8, 16));
        var body = frame[HeaderLength..].ToArray();

        return new WirePacket(protocolVersion, messageType, correlationId, body);
    }

    public static int ReadBodyLength(ReadOnlySpan<byte> header)
    {
        if (header.Length < HeaderLength || !header[..4].SequenceEqual(Magic))
        {
            throw new WireProtocolException("Packet header is invalid.");
        }

        var bodyLength = BinaryPrimitives.ReadInt32BigEndian(header.Slice(24, 4));
        if (bodyLength is < 0 or > ProtocolConstants.MaxPacketBodyLength)
        {
            throw new WireProtocolException($"Packet body length {bodyLength} is invalid.");
        }

        return bodyLength;
    }
}
