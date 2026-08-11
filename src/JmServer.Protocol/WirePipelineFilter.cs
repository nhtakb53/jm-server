using System.Buffers;
using SuperSocket.ProtoBase;

namespace JmServer.Protocol;

public sealed class WirePipelineFilter : FixedHeaderPipelineFilter<WirePacket>
{
    public WirePipelineFilter()
        : base(WirePacketCodec.HeaderLength)
    {
    }

    protected override int GetBodyLengthFromHeader(ref ReadOnlySequence<byte> buffer)
    {
        Span<byte> header = stackalloc byte[WirePacketCodec.HeaderLength];
        buffer.Slice(0, WirePacketCodec.HeaderLength).CopyTo(header);
        return WirePacketCodec.ReadBodyLength(header);
    }

    protected override WirePacket DecodePackage(ref ReadOnlySequence<byte> buffer)
    {
        return WirePacketCodec.Decode(buffer.ToArray());
    }
}
