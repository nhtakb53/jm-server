using JmServer.Contracts;
using JmServer.Domain;
using JmServer.Protocol;
using Microsoft.Extensions.Logging;
using SuperSocket.Server.Abstractions.Session;

namespace JmServer.Server;

public sealed class PacketDispatcher(
    IDeviceStore deviceStore,
    CharacterVaultService vault,
    ProfileVaultService profileVault,
    PvpRoomService pvpRooms,
    ILogger<PacketDispatcher> logger)
{
    public async ValueTask DispatchAsync(JmSession session, WirePacket packet)
    {
        try
        {
            if (packet.MessageType != MessageType.HandshakeRequest &&
                packet.ProtocolVersion != ProtocolConstants.CurrentVersion)
            {
                await SendErrorAsync(
                    session,
                    packet.CorrelationId,
                    ErrorCode.UnsupportedProtocol,
                    $"Protocol version {packet.ProtocolVersion} is not supported.");
                return;
            }

            if (packet.MessageType != MessageType.HandshakeRequest && !session.HandshakeComplete)
            {
                await SendErrorAsync(
                    session,
                    packet.CorrelationId,
                    ErrorCode.UnsupportedProtocol,
                    "Handshake is required before other requests.");
                return;
            }

            switch (packet.MessageType)
            {
                case MessageType.HandshakeRequest:
                    await HandleHandshakeAsync(session, packet);
                    break;
                case MessageType.AuthenticateRequest:
                    await HandleAuthenticateAsync(session, packet);
                    break;
                case MessageType.ListCharactersRequest:
                    await HandleListCharactersAsync(session, packet);
                    break;
                case MessageType.GetCharacterCreationPolicyRequest:
                    await HandleCharacterCreationPolicyAsync(session, packet);
                    break;
                case MessageType.CreateCharacterRequest:
                    await HandleCreateCharacterAsync(session, packet);
                    break;
                case MessageType.GetCharacterManagementRequest:
                    await HandleGetCharacterManagementAsync(session, packet);
                    break;
                case MessageType.ListDeletedCharactersRequest:
                    await HandleListDeletedCharactersAsync(session, packet);
                    break;
                case MessageType.RenameCharacterRequest:
                    await HandleRenameCharacterAsync(session, packet);
                    break;
                case MessageType.ResetCharacterStatsRequest:
                    await HandleResetCharacterStatsAsync(session, packet);
                    break;
                case MessageType.DeleteCharacterRequest:
                    await HandleDeleteCharacterAsync(session, packet);
                    break;
                case MessageType.RestoreCharacterRequest:
                    await HandleRestoreCharacterAsync(session, packet);
                    break;
                case MessageType.PurgeDeletedCharacterRequest:
                    await HandlePurgeDeletedCharacterAsync(session, packet);
                    break;
                case MessageType.CheckoutProfileRequest:
                    await HandleProfileCheckoutAsync(session, packet);
                    break;
                case MessageType.RenewProfileLeaseRequest:
                    await HandleProfileRenewLeaseAsync(session, packet);
                    break;
                case MessageType.CheckinProfileRequest:
                    await HandleProfileCheckinAsync(session, packet);
                    break;
                case MessageType.ListPvpRoomsRequest:
                    await HandleListPvpRoomsAsync(session, packet);
                    break;
                case MessageType.CreatePvpRoomRequest:
                    await HandleCreatePvpRoomAsync(session, packet);
                    break;
                case MessageType.JoinPvpRoomRequest:
                    await HandleJoinPvpRoomAsync(session, packet);
                    break;
                case MessageType.RenewPvpRoomRequest:
                    await HandleRenewPvpRoomAsync(session, packet);
                    break;
                case MessageType.LeavePvpRoomRequest:
                    await HandleLeavePvpRoomAsync(session, packet);
                    break;
                default:
                    await SendErrorAsync(
                        session,
                        packet.CorrelationId,
                        ErrorCode.InvalidPacket,
                        $"Message type {(ushort)packet.MessageType} is not a client request.");
                    break;
            }
        }
        catch (AuthenticationRequiredException exception)
        {
            await SendErrorAsync(
                session,
                packet.CorrelationId,
                ErrorCode.AuthenticationRequired,
                exception.Message);
        }
        catch (VaultException exception)
        {
            await SendErrorAsync(
                session,
                packet.CorrelationId,
                MapError(exception.Error),
                exception.Message);
        }
        catch (WireProtocolException exception)
        {
            await SendErrorAsync(
                session,
                packet.CorrelationId,
                ErrorCode.InvalidPacket,
                exception.Message);
        }
        catch (InvalidDataException exception)
        {
            await SendErrorAsync(
                session,
                packet.CorrelationId,
                ErrorCode.InvalidPacket,
                exception.Message);
        }
        catch (Exception exception)
        {
            ServerDiagnostics.WriteException(exception, packet.MessageType, session.SessionID);
            logger.LogError(
                exception,
                "Request {MessageType} failed for session {SessionId}.",
                packet.MessageType,
                session.SessionID);
            await SendErrorAsync(
                session,
                packet.CorrelationId,
                ErrorCode.InternalServerError,
                "The server could not complete the request.",
                retryable: true);
        }
    }

    private static async ValueTask HandleHandshakeAsync(JmSession session, WirePacket packet)
    {
        var request = PacketPayload.DeserializeJson<HandshakeRequest>(packet.Body.Span);
        var accepted = request.ProtocolVersion == ProtocolConstants.CurrentVersion &&
                       packet.ProtocolVersion == ProtocolConstants.CurrentVersion;
        session.HandshakeComplete = accepted;

        await SendAsync(
            session,
            WirePacket.Create(
                MessageType.HandshakeResponse,
                packet.CorrelationId,
                new HandshakeResponse(
                    accepted,
                    ProtocolConstants.CurrentVersion,
                    typeof(PacketDispatcher).Assembly.GetName().Version?.ToString() ?? "0.1.0",
                    accepted ? null : "Client and server protocol versions do not match.")));
    }

    private async ValueTask HandleAuthenticateAsync(JmSession session, WirePacket packet)
    {
        var request = PacketPayload.DeserializeJson<AuthenticateRequest>(packet.Body.Span);
        if (request.DeviceId == Guid.Empty || string.IsNullOrWhiteSpace(request.Token))
        {
            await SendErrorAsync(
                session,
                packet.CorrelationId,
                ErrorCode.AuthenticationFailed,
                "Device id and token are required.");
            return;
        }

        var identity = await deviceStore.AuthenticateAsync(
            request.DeviceId,
            TokenHasher.Hash(request.Token),
            CancellationToken.None);
        if (identity is null)
        {
            await SendErrorAsync(
                session,
                packet.CorrelationId,
                ErrorCode.AuthenticationFailed,
                "Device credentials were not accepted.");
            return;
        }

        session.Identity = identity;
        await SendAsync(
            session,
            WirePacket.Create(
                MessageType.AuthenticateResponse,
                packet.CorrelationId,
                new AuthenticateResponse(identity.AccountId, identity.DeviceId, identity.Username)));
    }

    private async ValueTask HandleListCharactersAsync(JmSession session, WirePacket packet)
    {
        _ = PacketPayload.DeserializeJson<ListCharactersRequest>(packet.Body.Span);
        var identity = RequireIdentity(session);
        var characters = await vault.ListAsync(identity.AccountId, CancellationToken.None);
        var response = characters
            .Select(character => new CharacterSummary(
                character.CharacterId,
                character.Name,
                character.CharacterClass,
                character.Revision,
                character.IsLeased,
                character.LeaseExpiresAt))
            .ToArray();

        await SendAsync(
            session,
            WirePacket.Create(
                MessageType.ListCharactersResponse,
                packet.CorrelationId,
                new ListCharactersResponse(response)));
    }

    private async ValueTask HandleProfileCheckoutAsync(JmSession session, WirePacket packet)
    {
        var request = PacketPayload.DeserializeJson<CheckoutProfileRequest>(packet.Body.Span);
        var identity = RequireIdentity(session);
        var checkout = await profileVault.CheckoutAsync(
            identity.AccountId,
            identity.DeviceId,
            request.SelectedCharacterId,
            CancellationToken.None);
        var metadata = new CheckoutProfileResponse(
            checkout.SelectedCharacterId,
            checkout.LeaseId,
            checkout.Revision,
            checkout.LeaseExpiresAt,
            checkout.BundleData.Length,
            Convert.ToHexString(checkout.Sha256));
        await SendAsync(
            session,
            WirePacket.CreateWithBinary(
                MessageType.CheckoutProfileResponse,
                packet.CorrelationId,
                metadata,
                checkout.BundleData));
    }

    private static async ValueTask HandleCharacterCreationPolicyAsync(
        JmSession session,
        WirePacket packet)
    {
        _ = PacketPayload.DeserializeJson<GetCharacterCreationPolicyRequest>(packet.Body.Span);
        _ = RequireIdentity(session);
        var policy = CharacterVaultService.CreationPolicy;
        await SendAsync(
            session,
            WirePacket.Create(
                MessageType.GetCharacterCreationPolicyResponse,
                packet.CorrelationId,
                new GetCharacterCreationPolicyResponse(
                    policy.MaxCharactersPerAccount,
                    policy.MinimumNameLength,
                    policy.MaximumNameLength,
                    policy.Classes.Select(item => new CharacterClassOption(
                        MapClass(item.CharacterClass),
                        item.DisplayName,
                        item.RequiresWarlockExpansion)).ToArray(),
                    policy.Presets.Select(item => new CharacterPresetOption(
                        MapPreset(item.Preset),
                        item.DisplayName,
                        item.Description,
                        item.Level,
                        item.CompletesQuests,
                        item.UnlocksAllWaypoints,
                        item.UnspentStatPoints,
                        item.UnspentSkillPoints)).ToArray())));
    }

    private async ValueTask HandleCreateCharacterAsync(JmSession session, WirePacket packet)
    {
        var request = PacketPayload.DeserializeJson<CreateCharacterRequest>(packet.Body.Span);
        var identity = RequireIdentity(session);
        var created = await vault.CreateAsync(
            identity.AccountId,
            identity.DeviceId,
            request.Name,
            MapClass(request.CharacterClass),
            MapPreset(request.Preset),
            CancellationToken.None);
        await SendAsync(
            session,
            WirePacket.Create(
                MessageType.CreateCharacterResponse,
                packet.CorrelationId,
                new CreateCharacterResponse(
                    MapCharacter(created))));
    }

    private async ValueTask HandleGetCharacterManagementAsync(
        JmSession session,
        WirePacket packet)
    {
        var request = PacketPayload.DeserializeJson<GetCharacterManagementRequest>(packet.Body.Span);
        var identity = RequireIdentity(session);
        var snapshot = await vault.GetManagementAsync(
            identity.AccountId,
            request.CharacterId,
            CancellationToken.None);
        await SendAsync(
            session,
            WirePacket.Create(
                MessageType.GetCharacterManagementResponse,
                packet.CorrelationId,
                new GetCharacterManagementResponse(
                    MapCharacter(snapshot.Character),
                    MapStats(snapshot.Stats))));
    }

    private async ValueTask HandleListDeletedCharactersAsync(
        JmSession session,
        WirePacket packet)
    {
        _ = PacketPayload.DeserializeJson<ListDeletedCharactersRequest>(packet.Body.Span);
        var identity = RequireIdentity(session);
        var deleted = await vault.ListDeletedAsync(identity.AccountId, CancellationToken.None);
        await SendAsync(
            session,
            WirePacket.Create(
                MessageType.ListDeletedCharactersResponse,
                packet.CorrelationId,
                new ListDeletedCharactersResponse(
                    deleted.Select(character => new DeletedCharacterSummary(
                        character.CharacterId,
                        character.Name,
                        character.CharacterClass,
                        character.DeletedAt)).ToArray())));
    }

    private async ValueTask HandleRenameCharacterAsync(JmSession session, WirePacket packet)
    {
        var request = PacketPayload.DeserializeJson<RenameCharacterRequest>(packet.Body.Span);
        var identity = RequireIdentity(session);
        var character = await vault.RenameAsync(
            identity.AccountId,
            identity.DeviceId,
            request.CharacterId,
            request.NewName,
            CancellationToken.None);
        await SendAsync(
            session,
            WirePacket.Create(
                MessageType.RenameCharacterResponse,
                packet.CorrelationId,
                new RenameCharacterResponse(MapCharacter(character))));
    }

    private async ValueTask HandleResetCharacterStatsAsync(
        JmSession session,
        WirePacket packet)
    {
        var request = PacketPayload.DeserializeJson<ResetCharacterStatsRequest>(packet.Body.Span);
        var identity = RequireIdentity(session);
        var snapshot = await vault.ResetStatsAsync(
            identity.AccountId,
            identity.DeviceId,
            request.CharacterId,
            CancellationToken.None);
        await SendAsync(
            session,
            WirePacket.Create(
                MessageType.ResetCharacterStatsResponse,
                packet.CorrelationId,
                new ResetCharacterStatsResponse(
                    MapCharacter(snapshot.Character),
                    MapStats(snapshot.Stats))));
    }

    private async ValueTask HandleDeleteCharacterAsync(JmSession session, WirePacket packet)
    {
        var request = PacketPayload.DeserializeJson<DeleteCharacterRequest>(packet.Body.Span);
        var identity = RequireIdentity(session);
        await vault.DeleteAsync(
            identity.AccountId,
            identity.DeviceId,
            request.CharacterId,
            CancellationToken.None);
        await SendAsync(
            session,
            WirePacket.Create(
                MessageType.DeleteCharacterResponse,
                packet.CorrelationId,
                new DeleteCharacterResponse(request.CharacterId)));
    }

    private async ValueTask HandleRestoreCharacterAsync(JmSession session, WirePacket packet)
    {
        var request = PacketPayload.DeserializeJson<RestoreCharacterRequest>(packet.Body.Span);
        var identity = RequireIdentity(session);
        var character = await vault.RestoreAsync(
            identity.AccountId,
            identity.DeviceId,
            request.CharacterId,
            CancellationToken.None);
        await SendAsync(
            session,
            WirePacket.Create(
                MessageType.RestoreCharacterResponse,
                packet.CorrelationId,
                new RestoreCharacterResponse(MapCharacter(character))));
    }

    private async ValueTask HandlePurgeDeletedCharacterAsync(
        JmSession session,
        WirePacket packet)
    {
        var request = PacketPayload.DeserializeJson<PurgeDeletedCharacterRequest>(packet.Body.Span);
        var identity = RequireIdentity(session);
        await vault.PurgeDeletedAsync(
            identity.AccountId,
            identity.DeviceId,
            request.CharacterId,
            CancellationToken.None);
        await SendAsync(
            session,
            WirePacket.Create(
                MessageType.PurgeDeletedCharacterResponse,
                packet.CorrelationId,
                new PurgeDeletedCharacterResponse(request.CharacterId)));
    }

    private async ValueTask HandleProfileRenewLeaseAsync(JmSession session, WirePacket packet)
    {
        var request = PacketPayload.DeserializeJson<RenewProfileLeaseRequest>(packet.Body.Span);
        var identity = RequireIdentity(session);
        var expiresAt = await profileVault.RenewLeaseAsync(
            identity.AccountId,
            identity.DeviceId,
            request.LeaseId,
            CancellationToken.None);
        await SendAsync(
            session,
            WirePacket.Create(
                MessageType.RenewProfileLeaseResponse,
                packet.CorrelationId,
                new RenewProfileLeaseResponse(request.LeaseId, expiresAt)));
    }

    private async ValueTask HandleProfileCheckinAsync(JmSession session, WirePacket packet)
    {
        var payload = PacketPayload.DeserializeWithBinary<CheckinProfileRequest>(packet.Body.Span);
        var request = payload.Metadata;
        if (request.BundleLength != payload.Binary.Length)
        {
            throw new WireProtocolException(
                $"Declared profile length {request.BundleLength} does not match {payload.Binary.Length} bytes received.");
        }

        var identity = RequireIdentity(session);
        var result = await profileVault.CheckinAsync(
            identity.AccountId,
            identity.DeviceId,
            request.LeaseId,
            request.ExpectedRevision,
            payload.Binary,
            request.Sha256Hex,
            CancellationToken.None);
        await SendAsync(
            session,
            WirePacket.Create(
                MessageType.CheckinProfileResponse,
                packet.CorrelationId,
                new CheckinProfileResponse(result.Revision, Convert.ToHexString(result.Sha256))));
    }

    private async ValueTask HandleListPvpRoomsAsync(
        JmSession session,
        WirePacket packet)
    {
        _ = PacketPayload.DeserializeJson<ListPvpRoomsRequest>(packet.Body.Span);
        var identity = RequireIdentity(session);
        var rooms = pvpRooms.List().Select(room => MapRoom(room, identity)).ToArray();
        await SendAsync(
            session,
            WirePacket.Create(
                MessageType.ListPvpRoomsResponse,
                packet.CorrelationId,
                new ListPvpRoomsResponse(rooms)));
    }

    private async ValueTask HandleCreatePvpRoomAsync(JmSession session, WirePacket packet)
    {
        var request = PacketPayload.DeserializeJson<CreatePvpRoomRequest>(packet.Body.Span);
        var identity = RequireIdentity(session);
        var room = await pvpRooms.CreateAsync(
            identity,
            request.CharacterId,
            request.HostAddress,
            CancellationToken.None);
        await SendAsync(
            session,
            WirePacket.Create(
                MessageType.CreatePvpRoomResponse,
                packet.CorrelationId,
                new CreatePvpRoomResponse(MapRoom(room, identity))));
    }

    private async ValueTask HandleJoinPvpRoomAsync(JmSession session, WirePacket packet)
    {
        var request = PacketPayload.DeserializeJson<JoinPvpRoomRequest>(packet.Body.Span);
        var identity = RequireIdentity(session);
        var room = await pvpRooms.JoinAsync(
            identity,
            request.RoomId,
            request.CharacterId,
            CancellationToken.None);
        await SendAsync(
            session,
            WirePacket.Create(
                MessageType.JoinPvpRoomResponse,
                packet.CorrelationId,
                new JoinPvpRoomResponse(MapRoom(room, identity))));
    }

    private async ValueTask HandleRenewPvpRoomAsync(
        JmSession session,
        WirePacket packet)
    {
        var request = PacketPayload.DeserializeJson<RenewPvpRoomRequest>(packet.Body.Span);
        var identity = RequireIdentity(session);
        var room = pvpRooms.Renew(identity, request.RoomId);
        await SendAsync(
            session,
            WirePacket.Create(
                MessageType.RenewPvpRoomResponse,
                packet.CorrelationId,
                new RenewPvpRoomResponse(MapRoom(room, identity))));
    }

    private async ValueTask HandleLeavePvpRoomAsync(
        JmSession session,
        WirePacket packet)
    {
        var request = PacketPayload.DeserializeJson<LeavePvpRoomRequest>(packet.Body.Span);
        var identity = RequireIdentity(session);
        pvpRooms.Leave(identity, request.RoomId);
        await SendAsync(
            session,
            WirePacket.Create(
                MessageType.LeavePvpRoomResponse,
                packet.CorrelationId,
                new LeavePvpRoomResponse(request.RoomId)));
    }

    private static DeviceIdentity RequireIdentity(JmSession session) =>
        session.Identity ?? throw new AuthenticationRequiredException();

    private static async ValueTask SendAsync(JmSession session, WirePacket packet)
    {
        await ((IAppSession)session).SendAsync(WirePacketCodec.Encode(packet));
    }

    private static ValueTask SendErrorAsync(
        JmSession session,
        Guid correlationId,
        ErrorCode code,
        string message,
        bool retryable = false) =>
        SendAsync(
            session,
            WirePacket.Create(
                MessageType.ErrorResponse,
                correlationId,
                new ErrorResponse(code, message, retryable)));

    private static ErrorCode MapError(VaultError error) => error switch
    {
        VaultError.CharacterNotFound => ErrorCode.CharacterNotFound,
        VaultError.CharacterNameInvalid => ErrorCode.CharacterNameInvalid,
        VaultError.CharacterAlreadyExists => ErrorCode.CharacterAlreadyExists,
        VaultError.CharacterLimitReached => ErrorCode.CharacterLimitReached,
        VaultError.CharacterCreationInvalid => ErrorCode.CharacterCreationInvalid,
        VaultError.CharacterDeleted => ErrorCode.CharacterDeleted,
        VaultError.CharacterManagementInvalid => ErrorCode.CharacterManagementInvalid,
        VaultError.PvpRoomNotFound => ErrorCode.PvpRoomNotFound,
        VaultError.PvpRoomFull => ErrorCode.PvpRoomFull,
        VaultError.PvpRoomConflict => ErrorCode.PvpRoomConflict,
        VaultError.PvpEndpointInvalid => ErrorCode.PvpEndpointInvalid,
        VaultError.LeaseConflict => ErrorCode.CharacterLeaseConflict,
        VaultError.LeaseInvalid => ErrorCode.CharacterLeaseInvalid,
        VaultError.RevisionConflict => ErrorCode.CharacterRevisionConflict,
        VaultError.SaveTooLarge => ErrorCode.SaveTooLarge,
        VaultError.ChecksumMismatch => ErrorCode.SaveChecksumMismatch,
        _ => ErrorCode.Unknown
    };

    private static CharacterSummary MapCharacter(VaultCharacterSummary character) =>
        new(
            character.CharacterId,
            character.Name,
            character.CharacterClass,
            character.Revision,
            character.IsLeased,
            character.LeaseExpiresAt);

    private static CharacterPrimaryStatsInfo MapStats(CharacterPrimaryStats stats) =>
        new(
            stats.Level,
            stats.Strength,
            stats.Dexterity,
            stats.Vitality,
            stats.Energy,
            stats.UnspentStatPoints);

    private static PvpRoomInfo MapRoom(PvpRoomSnapshot room, DeviceIdentity identity)
    {
        var isHost = room.Host.AccountId == identity.AccountId &&
                     room.Host.DeviceId == identity.DeviceId;
        var isGuest = room.Guest?.AccountId == identity.AccountId &&
                      room.Guest.DeviceId == identity.DeviceId;
        return new PvpRoomInfo(
            room.RoomId,
            room.RoomCode,
            room.Host.Username,
            room.Host.CharacterName,
            room.Guest?.Username,
            room.Guest?.CharacterName,
            room.HostAddress,
            room.HostPort,
            room.State == JmServer.Domain.PvpRoomState.Waiting
                ? PvpRoomStatus.Waiting
                : PvpRoomStatus.Ready,
            room.CreatedAt,
            room.ExpiresAt,
            isHost,
            isHost || isGuest,
            room.Guest is null && room.Host.AccountId != identity.AccountId);
    }

    private static PlayableCharacterClass MapClass(VaultCharacterClass characterClass) =>
        characterClass switch
        {
            VaultCharacterClass.Amazon => PlayableCharacterClass.Amazon,
            VaultCharacterClass.Sorceress => PlayableCharacterClass.Sorceress,
            VaultCharacterClass.Necromancer => PlayableCharacterClass.Necromancer,
            VaultCharacterClass.Paladin => PlayableCharacterClass.Paladin,
            VaultCharacterClass.Barbarian => PlayableCharacterClass.Barbarian,
            VaultCharacterClass.Druid => PlayableCharacterClass.Druid,
            VaultCharacterClass.Assassin => PlayableCharacterClass.Assassin,
            VaultCharacterClass.Warlock => PlayableCharacterClass.Warlock,
            _ => throw new ArgumentOutOfRangeException(nameof(characterClass))
        };

    private static VaultCharacterClass MapClass(PlayableCharacterClass characterClass) =>
        characterClass switch
        {
            PlayableCharacterClass.Amazon => VaultCharacterClass.Amazon,
            PlayableCharacterClass.Sorceress => VaultCharacterClass.Sorceress,
            PlayableCharacterClass.Necromancer => VaultCharacterClass.Necromancer,
            PlayableCharacterClass.Paladin => VaultCharacterClass.Paladin,
            PlayableCharacterClass.Barbarian => VaultCharacterClass.Barbarian,
            PlayableCharacterClass.Druid => VaultCharacterClass.Druid,
            PlayableCharacterClass.Assassin => VaultCharacterClass.Assassin,
            PlayableCharacterClass.Warlock => VaultCharacterClass.Warlock,
            _ => throw new VaultException(
                VaultError.CharacterCreationInvalid,
                "지원하지 않는 캐릭터 직업입니다.")
        };

    private static JmServer.Contracts.CharacterCreationPreset MapPreset(
        JmServer.Domain.CharacterCreationPreset preset) =>
        preset switch
        {
            JmServer.Domain.CharacterCreationPreset.FreshStart =>
                JmServer.Contracts.CharacterCreationPreset.FreshStart,
            JmServer.Domain.CharacterCreationPreset.PvpReady =>
                JmServer.Contracts.CharacterCreationPreset.PvpReady,
            _ => throw new ArgumentOutOfRangeException(nameof(preset))
        };

    private static JmServer.Domain.CharacterCreationPreset MapPreset(
        JmServer.Contracts.CharacterCreationPreset preset) =>
        preset switch
        {
            JmServer.Contracts.CharacterCreationPreset.FreshStart =>
                JmServer.Domain.CharacterCreationPreset.FreshStart,
            JmServer.Contracts.CharacterCreationPreset.PvpReady =>
                JmServer.Domain.CharacterCreationPreset.PvpReady,
            _ => throw new VaultException(
                VaultError.CharacterCreationInvalid,
                "지원하지 않는 캐릭터 생성 프리셋입니다.")
        };

    private sealed class AuthenticationRequiredException : Exception
    {
        public AuthenticationRequiredException()
            : base("Authentication is required.")
        {
        }
    }
}
