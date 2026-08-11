using System.Net;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using JmServer.Contracts;
using JmServer.GameIntegration;
using JmServer.Protocol;
using SuperSocket.Client;

namespace JmServer.Launcher;

public sealed class LauncherConnection : IAsyncDisposable
{
    private readonly IEasyClient<WirePacket> _client;
    private bool _connected;

    public LauncherConnection(bool useTls, string targetHost, string? certificateSha256 = null)
    {
        if (!useTls && !string.IsNullOrWhiteSpace(certificateSha256))
        {
            throw new ArgumentException("A certificate pin requires TLS.", nameof(certificateSha256));
        }

        var client = new EasyClient<WirePacket>(new WirePipelineFilter());
        if (useTls)
        {
            var hasCertificatePin = !string.IsNullOrWhiteSpace(certificateSha256);
            client.Security = new SecurityOptions
            {
                TargetHost = targetHost,
                EnabledSslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13,
                CertificateRevocationCheckMode = hasCertificatePin
                    ? X509RevocationMode.NoCheck
                    : X509RevocationMode.Online,
                RemoteCertificateValidationCallback = !hasCertificatePin
                    ? null
                    : CertificatePinning.CreateSha256Validator(certificateSha256!)
            };
        }

        _client = client.AsClient();
    }

    public async Task ConnectAsync(string host, int port, CancellationToken cancellationToken)
    {
        var connected = await _client.ConnectAsync(new DnsEndPoint(host, port), cancellationToken);
        if (!connected)
        {
            throw new IOException($"Could not connect to {host}:{port}.");
        }

        _connected = true;

        var response = await RequestAsync<HandshakeResponse>(
            WirePacket.Create(
                MessageType.HandshakeRequest,
                Guid.NewGuid(),
                new HandshakeRequest(ProtocolConstants.CurrentVersion, "jeongman-launcher/0.6")),
            MessageType.HandshakeResponse,
            cancellationToken);
        if (!response.Accepted)
        {
            throw new IOException(response.Message ?? "Server rejected the protocol handshake.");
        }
    }

    public Task<AuthenticateResponse> AuthenticateAsync(
        Guid deviceId,
        string token,
        CancellationToken cancellationToken) =>
        RequestAsync<AuthenticateResponse>(
            WirePacket.Create(
                MessageType.AuthenticateRequest,
                Guid.NewGuid(),
                new AuthenticateRequest(deviceId, token)),
            MessageType.AuthenticateResponse,
            cancellationToken);

    public Task<ListCharactersResponse> ListCharactersAsync(CancellationToken cancellationToken) =>
        RequestAsync<ListCharactersResponse>(
            WirePacket.Create(
                MessageType.ListCharactersRequest,
                Guid.NewGuid(),
                new ListCharactersRequest()),
            MessageType.ListCharactersResponse,
            cancellationToken);

    public Task<GetCharacterCreationPolicyResponse> GetCharacterCreationPolicyAsync(
        CancellationToken cancellationToken) =>
        RequestAsync<GetCharacterCreationPolicyResponse>(
            WirePacket.Create(
                MessageType.GetCharacterCreationPolicyRequest,
                Guid.NewGuid(),
                new GetCharacterCreationPolicyRequest()),
            MessageType.GetCharacterCreationPolicyResponse,
            cancellationToken);

    public Task<CreateCharacterResponse> CreateCharacterAsync(
        CreateCharacterRequest request,
        CancellationToken cancellationToken) =>
        RequestAsync<CreateCharacterResponse>(
            WirePacket.Create(
                MessageType.CreateCharacterRequest,
                Guid.NewGuid(),
                request),
            MessageType.CreateCharacterResponse,
            cancellationToken);

    public Task<GetCharacterManagementResponse> GetCharacterManagementAsync(
        Guid characterId,
        CancellationToken cancellationToken) =>
        RequestAsync<GetCharacterManagementResponse>(
            WirePacket.Create(
                MessageType.GetCharacterManagementRequest,
                Guid.NewGuid(),
                new GetCharacterManagementRequest(characterId)),
            MessageType.GetCharacterManagementResponse,
            cancellationToken);

    public Task<ListDeletedCharactersResponse> ListDeletedCharactersAsync(
        CancellationToken cancellationToken) =>
        RequestAsync<ListDeletedCharactersResponse>(
            WirePacket.Create(
                MessageType.ListDeletedCharactersRequest,
                Guid.NewGuid(),
                new ListDeletedCharactersRequest()),
            MessageType.ListDeletedCharactersResponse,
            cancellationToken);

    public Task<RenameCharacterResponse> RenameCharacterAsync(
        Guid characterId,
        string newName,
        CancellationToken cancellationToken) =>
        RequestAsync<RenameCharacterResponse>(
            WirePacket.Create(
                MessageType.RenameCharacterRequest,
                Guid.NewGuid(),
                new RenameCharacterRequest(characterId, newName)),
            MessageType.RenameCharacterResponse,
            cancellationToken);

    public Task<ResetCharacterStatsResponse> ResetCharacterStatsAsync(
        Guid characterId,
        CancellationToken cancellationToken) =>
        RequestAsync<ResetCharacterStatsResponse>(
            WirePacket.Create(
                MessageType.ResetCharacterStatsRequest,
                Guid.NewGuid(),
                new ResetCharacterStatsRequest(characterId)),
            MessageType.ResetCharacterStatsResponse,
            cancellationToken);

    public Task<DeleteCharacterResponse> DeleteCharacterAsync(
        Guid characterId,
        CancellationToken cancellationToken) =>
        RequestAsync<DeleteCharacterResponse>(
            WirePacket.Create(
                MessageType.DeleteCharacterRequest,
                Guid.NewGuid(),
                new DeleteCharacterRequest(characterId)),
            MessageType.DeleteCharacterResponse,
            cancellationToken);

    public Task<RestoreCharacterResponse> RestoreCharacterAsync(
        Guid characterId,
        CancellationToken cancellationToken) =>
        RequestAsync<RestoreCharacterResponse>(
            WirePacket.Create(
                MessageType.RestoreCharacterRequest,
                Guid.NewGuid(),
                new RestoreCharacterRequest(characterId)),
            MessageType.RestoreCharacterResponse,
            cancellationToken);

    public Task<PurgeDeletedCharacterResponse> PurgeDeletedCharacterAsync(
        Guid characterId,
        CancellationToken cancellationToken) =>
        RequestAsync<PurgeDeletedCharacterResponse>(
            WirePacket.Create(
                MessageType.PurgeDeletedCharacterRequest,
                Guid.NewGuid(),
                new PurgeDeletedCharacterRequest(characterId)),
            MessageType.PurgeDeletedCharacterResponse,
            cancellationToken);

    public Task<BinaryPayload<CheckoutProfileResponse>> CheckoutProfileAsync(
        Guid selectedCharacterId,
        CancellationToken cancellationToken) =>
        RequestBinaryAsync<CheckoutProfileResponse>(
            WirePacket.Create(
                MessageType.CheckoutProfileRequest,
                Guid.NewGuid(),
                new CheckoutProfileRequest(selectedCharacterId)),
            MessageType.CheckoutProfileResponse,
            cancellationToken);

    public Task<RenewProfileLeaseResponse> RenewProfileLeaseAsync(
        Guid leaseId,
        CancellationToken cancellationToken) =>
        RequestAsync<RenewProfileLeaseResponse>(
            WirePacket.Create(
                MessageType.RenewProfileLeaseRequest,
                Guid.NewGuid(),
                new RenewProfileLeaseRequest(leaseId)),
            MessageType.RenewProfileLeaseResponse,
            cancellationToken);

    public Task<CheckinProfileResponse> CheckinProfileAsync(
        CheckinProfileRequest metadata,
        ReadOnlySpan<byte> bundleData,
        CancellationToken cancellationToken) =>
        RequestAsync<CheckinProfileResponse>(
            WirePacket.CreateWithBinary(
                MessageType.CheckinProfileRequest,
                Guid.NewGuid(),
                metadata,
                bundleData),
            MessageType.CheckinProfileResponse,
            cancellationToken);

    public Task<ListPvpRoomsResponse> ListPvpRoomsAsync(CancellationToken cancellationToken) =>
        RequestAsync<ListPvpRoomsResponse>(
            WirePacket.Create(
                MessageType.ListPvpRoomsRequest,
                Guid.NewGuid(),
                new ListPvpRoomsRequest()),
            MessageType.ListPvpRoomsResponse,
            cancellationToken);

    public Task<CreatePvpRoomResponse> CreatePvpRoomAsync(
        CreatePvpRoomRequest request,
        CancellationToken cancellationToken) =>
        RequestAsync<CreatePvpRoomResponse>(
            WirePacket.Create(
                MessageType.CreatePvpRoomRequest,
                Guid.NewGuid(),
                request),
            MessageType.CreatePvpRoomResponse,
            cancellationToken);

    public Task<JoinPvpRoomResponse> JoinPvpRoomAsync(
        JoinPvpRoomRequest request,
        CancellationToken cancellationToken) =>
        RequestAsync<JoinPvpRoomResponse>(
            WirePacket.Create(
                MessageType.JoinPvpRoomRequest,
                Guid.NewGuid(),
                request),
            MessageType.JoinPvpRoomResponse,
            cancellationToken);

    public Task<RenewPvpRoomResponse> RenewPvpRoomAsync(
        Guid roomId,
        CancellationToken cancellationToken) =>
        RequestAsync<RenewPvpRoomResponse>(
            WirePacket.Create(
                MessageType.RenewPvpRoomRequest,
                Guid.NewGuid(),
                new RenewPvpRoomRequest(roomId)),
            MessageType.RenewPvpRoomResponse,
            cancellationToken);

    public Task<LeavePvpRoomResponse> LeavePvpRoomAsync(
        Guid roomId,
        CancellationToken cancellationToken) =>
        RequestAsync<LeavePvpRoomResponse>(
            WirePacket.Create(
                MessageType.LeavePvpRoomRequest,
                Guid.NewGuid(),
                new LeavePvpRoomRequest(roomId)),
            MessageType.LeavePvpRoomResponse,
            cancellationToken);

    public async ValueTask DisposeAsync()
    {
        if (_connected)
        {
            await _client.CloseAsync();
            _connected = false;
        }
    }

    private async Task<T> RequestAsync<T>(
        WirePacket request,
        MessageType expectedResponse,
        CancellationToken cancellationToken)
    {
        var packet = await SendAndReceiveAsync(request, expectedResponse, cancellationToken);
        return PacketPayload.DeserializeJson<T>(packet.Body.Span);
    }

    private async Task<BinaryPayload<T>> RequestBinaryAsync<T>(
        WirePacket request,
        MessageType expectedResponse,
        CancellationToken cancellationToken)
    {
        var packet = await SendAndReceiveAsync(request, expectedResponse, cancellationToken);
        return PacketPayload.DeserializeWithBinary<T>(packet.Body.Span);
    }

    private async Task<WirePacket> SendAndReceiveAsync(
        WirePacket request,
        MessageType expectedResponse,
        CancellationToken cancellationToken)
    {
        await _client.SendAsync(WirePacketCodec.Encode(request));
        var response = await _client.ReceiveAsync().AsTask().WaitAsync(cancellationToken);
        if (response is null)
        {
            throw new EndOfStreamException("Server closed the connection before responding.");
        }

        if (response.ProtocolVersion != ProtocolConstants.CurrentVersion)
        {
            throw new WireProtocolException(
                $"Server returned unsupported protocol version {response.ProtocolVersion}.");
        }

        if (response.CorrelationId != request.CorrelationId)
        {
            throw new WireProtocolException("Response correlation id does not match the request.");
        }

        if (response.MessageType == MessageType.ErrorResponse)
        {
            var error = PacketPayload.DeserializeJson<ErrorResponse>(response.Body.Span);
            throw new RemoteServerException(error);
        }

        if (response.MessageType != expectedResponse)
        {
            throw new WireProtocolException(
                $"Expected {expectedResponse}, but server returned {response.MessageType}.");
        }

        return response;
    }
}

public sealed class RemoteServerException(ErrorResponse error)
    : Exception($"{error.Code}: {error.Message}")
{
    public ErrorResponse Error { get; } = error;
}
