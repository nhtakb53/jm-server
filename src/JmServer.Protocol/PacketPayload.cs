using System.Buffers.Binary;
using System.Text.Json;
using JmServer.Contracts;

namespace JmServer.Protocol;

public static class PacketPayload
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = false
    };

    public static byte[] SerializeJson<T>(T value) =>
        JsonSerializer.SerializeToUtf8Bytes(value, JsonOptions);

    public static T DeserializeJson<T>(ReadOnlySpan<byte> payload) =>
        JsonSerializer.Deserialize<T>(payload, JsonOptions)
        ?? throw new WireProtocolException($"Payload did not contain a {typeof(T).Name} value.");

    public static byte[] SerializeWithBinary<T>(T metadata, ReadOnlySpan<byte> binary)
    {
        if (binary.Length > ProtocolConstants.MaxBinaryPayloadLength)
        {
            throw new WireProtocolException(
                $"Binary payload is {binary.Length} bytes; maximum is {ProtocolConstants.MaxBinaryPayloadLength}.");
        }

        var json = SerializeJson(metadata);
        if (json.Length > ProtocolConstants.MaxMetadataLength)
        {
            throw new WireProtocolException(
                $"Metadata is {json.Length} bytes; maximum is {ProtocolConstants.MaxMetadataLength}.");
        }

        var payload = GC.AllocateUninitializedArray<byte>(4 + json.Length + binary.Length);
        BinaryPrimitives.WriteInt32BigEndian(payload.AsSpan(0, 4), json.Length);
        json.CopyTo(payload.AsSpan(4));
        binary.CopyTo(payload.AsSpan(4 + json.Length));
        return payload;
    }

    public static BinaryPayload<T> DeserializeWithBinary<T>(ReadOnlySpan<byte> payload)
    {
        if (payload.Length < 4)
        {
            throw new WireProtocolException("Binary payload is missing its metadata length.");
        }

        var metadataLength = BinaryPrimitives.ReadInt32BigEndian(payload[..4]);
        if (metadataLength is < 0 or > ProtocolConstants.MaxMetadataLength ||
            payload.Length < 4 + metadataLength)
        {
            throw new WireProtocolException("Binary payload metadata length is invalid.");
        }

        var metadata = DeserializeJson<T>(payload.Slice(4, metadataLength));
        var binary = payload[(4 + metadataLength)..].ToArray();
        if (binary.Length > ProtocolConstants.MaxBinaryPayloadLength)
        {
            throw new WireProtocolException(
                $"Binary payload is {binary.Length} bytes; maximum is {ProtocolConstants.MaxBinaryPayloadLength}.");
        }

        return new BinaryPayload<T>(metadata, binary);
    }
}

public sealed record BinaryPayload<T>(T Metadata, byte[] Binary);
