using System.Buffers.Binary;
using JmServer.Contracts;
using JmServer.Protocol;

namespace JmServer.Protocol.Tests;

public sealed class WirePacketCodecTests
{
    [Fact]
    public void EncodeAndDecode_PreserveHeaderAndJsonPayload()
    {
        var correlationId = Guid.NewGuid();
        var packet = WirePacket.Create(
            MessageType.HandshakeRequest,
            correlationId,
            new HandshakeRequest(ProtocolConstants.CurrentVersion, "test-client"));

        var decoded = WirePacketCodec.Decode(WirePacketCodec.Encode(packet));
        var payload = PacketPayload.DeserializeJson<HandshakeRequest>(decoded.Body.Span);

        Assert.Equal(ProtocolConstants.CurrentVersion, decoded.ProtocolVersion);
        Assert.Equal(MessageType.HandshakeRequest, decoded.MessageType);
        Assert.Equal(correlationId, decoded.CorrelationId);
        Assert.Equal("test-client", payload.ClientVersion);
    }

    [Fact]
    public void BinaryPayload_RoundTripsWithoutBase64Conversion()
    {
        var bundle = Enumerable.Range(0, 1024).Select(value => (byte)(value % 251)).ToArray();
        var metadata = new CheckinProfileRequest(
            Guid.NewGuid(),
            7,
            bundle.Length,
            "ABCDEF");

        var packet = WirePacket.CreateWithBinary(
            MessageType.CheckinProfileRequest,
            Guid.NewGuid(),
            metadata,
            bundle);
        var decodedPacket = WirePacketCodec.Decode(WirePacketCodec.Encode(packet));
        var decodedPayload = PacketPayload.DeserializeWithBinary<CheckinProfileRequest>(
            decodedPacket.Body.Span);

        Assert.Equal(metadata, decodedPayload.Metadata);
        Assert.Equal(bundle, decodedPayload.Binary);
    }

    [Fact]
    public void PvpRoomPayload_RoundTripsEndpointAndParticipationState()
    {
        var room = new PvpRoomInfo(
            Guid.NewGuid(),
            "A1B2C3",
            "ht",
            "악콩이",
            null,
            null,
            "192.168.0.20",
            15571,
            PvpRoomStatus.Waiting,
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow.AddMinutes(3),
            true,
            true,
            false);
        var packet = WirePacket.Create(
            MessageType.CreatePvpRoomResponse,
            Guid.NewGuid(),
            new CreatePvpRoomResponse(room));

        var decoded = WirePacketCodec.Decode(WirePacketCodec.Encode(packet));
        var payload = PacketPayload.DeserializeJson<CreatePvpRoomResponse>(decoded.Body.Span);

        Assert.Equal(room.RoomId, payload.Room.RoomId);
        Assert.Equal("192.168.0.20", payload.Room.HostAddress);
        Assert.Equal(15571, payload.Room.HostPort);
        Assert.True(payload.Room.IsHost);
    }

    [Fact]
    public void CharacterManagementPayload_RoundTripsStats()
    {
        var character = new CharacterSummary(
            Guid.NewGuid(), "악콩이", "Warlock", 12, false, null);
        var stats = new CharacterPrimaryStatsInfo(90, 15, 20, 25, 20, 460);
        var packet = WirePacket.Create(
            MessageType.GetCharacterManagementResponse,
            Guid.NewGuid(),
            new GetCharacterManagementResponse(character, stats));

        var decoded = WirePacketCodec.Decode(WirePacketCodec.Encode(packet));
        var payload = PacketPayload.DeserializeJson<GetCharacterManagementResponse>(
            decoded.Body.Span);

        Assert.Equal(character, payload.Character);
        Assert.Equal(460, payload.Stats.UnspentStatPoints);
        Assert.Equal(90, payload.Stats.Level);
    }

    [Fact]
    public void Decode_RejectsInvalidMagic()
    {
        var frame = WirePacketCodec.Encode(
            WirePacket.Create(
                MessageType.ListCharactersRequest,
                Guid.NewGuid(),
                new ListCharactersRequest()));
        frame[0] = (byte)'X';

        Assert.Throws<WireProtocolException>(() => WirePacketCodec.Decode(frame));
    }

    [Fact]
    public void Decode_RejectsDeclaredLengthThatDoesNotMatchFrame()
    {
        var frame = WirePacketCodec.Encode(
            WirePacket.Create(
                MessageType.ListCharactersRequest,
                Guid.NewGuid(),
                new ListCharactersRequest()));
        BinaryPrimitives.WriteInt32BigEndian(frame.AsSpan(24, 4), frame.Length);

        Assert.Throws<WireProtocolException>(() => WirePacketCodec.Decode(frame));
    }
}
