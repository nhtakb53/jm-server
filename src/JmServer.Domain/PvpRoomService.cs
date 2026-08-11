using System.Net;
using System.Net.Sockets;

namespace JmServer.Domain;

public sealed class PvpRoomService(
    ICharacterStore characterStore,
    TimeProvider? timeProvider = null)
{
    public const int GamePort = 15571;
    public static readonly TimeSpan RoomLeaseDuration = TimeSpan.FromMinutes(3);

    private readonly object _gate = new();
    private readonly Dictionary<Guid, MutableRoom> _rooms = [];
    private readonly TimeProvider _timeProvider = timeProvider ?? TimeProvider.System;

    public IReadOnlyList<PvpRoomSnapshot> List()
    {
        lock (_gate)
        {
            RemoveExpired(_timeProvider.GetUtcNow());
            return _rooms.Values
                .OrderBy(room => room.CreatedAt)
                .Select(ToSnapshot)
                .ToArray();
        }
    }

    public async Task<PvpRoomSnapshot> CreateAsync(
        DeviceIdentity identity,
        Guid characterId,
        string hostAddress,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(identity);
        var normalizedAddress = NormalizeHostAddress(hostAddress);
        var now = _timeProvider.GetUtcNow();
        var participant = await ResolveParticipantAsync(
            identity,
            characterId,
            now,
            cancellationToken);

        lock (_gate)
        {
            RemoveExpired(now);
            EnsureNotParticipating(identity.AccountId);

            var roomId = Guid.NewGuid();
            var room = new MutableRoom(
                roomId,
                CreateRoomCode(roomId),
                participant,
                normalizedAddress,
                now,
                now.Add(RoomLeaseDuration));
            _rooms.Add(roomId, room);
            return ToSnapshot(room);
        }
    }

    public async Task<PvpRoomSnapshot> JoinAsync(
        DeviceIdentity identity,
        Guid roomId,
        Guid characterId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(identity);
        var now = _timeProvider.GetUtcNow();
        var participant = await ResolveParticipantAsync(
            identity,
            characterId,
            now,
            cancellationToken);

        lock (_gate)
        {
            RemoveExpired(now);
            if (!_rooms.TryGetValue(roomId, out var room))
            {
                throw new VaultException(
                    VaultError.PvpRoomNotFound,
                    "PK 방을 찾을 수 없거나 방이 만료됐습니다.");
            }

            if (room.Host.AccountId == identity.AccountId)
            {
                throw new VaultException(
                    VaultError.PvpRoomConflict,
                    "자신이 만든 PK 방에는 참가할 수 없습니다.");
            }

            if (room.Guest?.AccountId == identity.AccountId &&
                room.Guest.DeviceId == identity.DeviceId)
            {
                room.ExpiresAt = now.Add(RoomLeaseDuration);
                return ToSnapshot(room);
            }

            EnsureNotParticipating(identity.AccountId);
            if (room.Guest is not null)
            {
                throw new VaultException(
                    VaultError.PvpRoomFull,
                    "이미 두 명이 참가한 PK 방입니다.");
            }

            room.Guest = participant;
            room.ExpiresAt = now.Add(RoomLeaseDuration);
            return ToSnapshot(room);
        }
    }

    public PvpRoomSnapshot Renew(DeviceIdentity identity, Guid roomId)
    {
        ArgumentNullException.ThrowIfNull(identity);
        var now = _timeProvider.GetUtcNow();
        lock (_gate)
        {
            RemoveExpired(now);
            if (!_rooms.TryGetValue(roomId, out var room) || !IsParticipant(room, identity))
            {
                throw new VaultException(
                    VaultError.PvpRoomNotFound,
                    "갱신할 PK 방을 찾을 수 없습니다.");
            }

            room.ExpiresAt = now.Add(RoomLeaseDuration);
            return ToSnapshot(room);
        }
    }

    public void Leave(DeviceIdentity identity, Guid roomId)
    {
        ArgumentNullException.ThrowIfNull(identity);
        lock (_gate)
        {
            RemoveExpired(_timeProvider.GetUtcNow());
            if (!_rooms.TryGetValue(roomId, out var room))
            {
                return;
            }

            if (Matches(room.Host, identity))
            {
                _rooms.Remove(roomId);
                return;
            }

            if (room.Guest is not null && Matches(room.Guest, identity))
            {
                room.Guest = null;
                room.ExpiresAt = _timeProvider.GetUtcNow().Add(RoomLeaseDuration);
                return;
            }

            throw new VaultException(
                VaultError.PvpRoomNotFound,
                "나갈 PK 방을 찾을 수 없습니다.");
        }
    }

    private async Task<PvpRoomParticipant> ResolveParticipantAsync(
        DeviceIdentity identity,
        Guid characterId,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var characters = await characterStore.ListAsync(
            identity.AccountId,
            now,
            cancellationToken);
        var character = characters.SingleOrDefault(item => item.CharacterId == characterId)
                        ?? throw new VaultException(
                            VaultError.CharacterNotFound,
                            "PK에 사용할 캐릭터를 현재 계정에서 찾을 수 없습니다.");
        if (character.IsLeased)
        {
            throw new VaultException(
                VaultError.LeaseConflict,
                "선택한 캐릭터가 이미 사용 중입니다.");
        }

        return new PvpRoomParticipant(
            identity.AccountId,
            identity.DeviceId,
            identity.Username,
            character.CharacterId,
            character.Name);
    }

    private void EnsureNotParticipating(Guid accountId)
    {
        if (_rooms.Values.Any(room =>
                room.Host.AccountId == accountId || room.Guest?.AccountId == accountId))
        {
            throw new VaultException(
                VaultError.PvpRoomConflict,
                "이미 참가 중인 PK 방이 있습니다.");
        }
    }

    private void RemoveExpired(DateTimeOffset now)
    {
        var expiredRoomIds = _rooms
            .Where(pair => pair.Value.ExpiresAt <= now)
            .Select(pair => pair.Key)
            .ToArray();
        foreach (var roomId in expiredRoomIds)
        {
            _rooms.Remove(roomId);
        }
    }

    private static bool IsParticipant(MutableRoom room, DeviceIdentity identity) =>
        Matches(room.Host, identity) ||
        room.Guest is not null && Matches(room.Guest, identity);

    private static bool Matches(PvpRoomParticipant participant, DeviceIdentity identity) =>
        participant.AccountId == identity.AccountId &&
        participant.DeviceId == identity.DeviceId;

    private static string NormalizeHostAddress(string value)
    {
        var normalized = value?.Trim() ?? string.Empty;
        if (!IPAddress.TryParse(normalized, out var address) ||
            address.AddressFamily != AddressFamily.InterNetwork ||
            address.Equals(IPAddress.Any) ||
            address.Equals(IPAddress.Broadcast) ||
            IPAddress.IsLoopback(address) ||
            address.GetAddressBytes()[0] is >= 224)
        {
            throw new VaultException(
                VaultError.PvpEndpointInvalid,
                "친구가 접속할 수 있는 유효한 IPv4 주소를 입력하세요.");
        }

        return address.ToString();
    }

    private static string CreateRoomCode(Guid roomId) =>
        Convert.ToHexString(roomId.ToByteArray().AsSpan(0, 3));

    private static PvpRoomSnapshot ToSnapshot(MutableRoom room) =>
        new(
            room.RoomId,
            room.RoomCode,
            room.Host,
            room.Guest,
            room.HostAddress,
            GamePort,
            room.Guest is null ? PvpRoomState.Waiting : PvpRoomState.Ready,
            room.CreatedAt,
            room.ExpiresAt);

    private sealed class MutableRoom(
        Guid roomId,
        string roomCode,
        PvpRoomParticipant host,
        string hostAddress,
        DateTimeOffset createdAt,
        DateTimeOffset expiresAt)
    {
        public Guid RoomId { get; } = roomId;
        public string RoomCode { get; } = roomCode;
        public PvpRoomParticipant Host { get; } = host;
        public PvpRoomParticipant? Guest { get; set; }
        public string HostAddress { get; } = hostAddress;
        public DateTimeOffset CreatedAt { get; } = createdAt;
        public DateTimeOffset ExpiresAt { get; set; } = expiresAt;
    }
}
