using JmServer.Domain;

namespace JmServer.Domain.Tests;

public sealed class PvpRoomServiceTests
{
    private static readonly DateTimeOffset Start =
        new(2026, 8, 8, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Create_AddsWaitingRoomWithFixedGamePort()
    {
        var fixture = new Fixture();

        var room = await fixture.Service.CreateAsync(
            fixture.Host,
            fixture.HostCharacterId,
            "192.168.50.10",
            CancellationToken.None);

        Assert.Equal(PvpRoomService.GamePort, room.HostPort);
        Assert.Equal(PvpRoomState.Waiting, room.State);
        Assert.Equal("192.168.50.10", room.HostAddress);
        Assert.Equal(fixture.Host.Username, room.Host.Username);
        Assert.Single(fixture.Service.List());
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-an-address")]
    [InlineData("127.0.0.1")]
    [InlineData("0.0.0.0")]
    [InlineData("224.0.0.1")]
    public async Task Create_RejectsUnreachableEndpoint(string address)
    {
        var fixture = new Fixture();

        var exception = await Assert.ThrowsAsync<VaultException>(() =>
            fixture.Service.CreateAsync(
                fixture.Host,
                fixture.HostCharacterId,
                address,
                CancellationToken.None));

        Assert.Equal(VaultError.PvpEndpointInvalid, exception.Error);
    }

    [Fact]
    public async Task Join_ReservesSecondSeatAndMarksRoomReady()
    {
        var fixture = new Fixture();
        var created = await fixture.CreateRoomAsync();

        var joined = await fixture.Service.JoinAsync(
            fixture.Guest,
            created.RoomId,
            fixture.GuestCharacterId,
            CancellationToken.None);

        Assert.Equal(PvpRoomState.Ready, joined.State);
        Assert.Equal(fixture.Guest.Username, joined.Guest?.Username);
        Assert.Equal("GuestCharacter", joined.Guest?.CharacterName);
    }

    [Fact]
    public async Task Join_RejectsThirdPlayerWhenRoomIsFull()
    {
        var fixture = new Fixture();
        var created = await fixture.CreateRoomAsync();
        _ = await fixture.Service.JoinAsync(
            fixture.Guest,
            created.RoomId,
            fixture.GuestCharacterId,
            CancellationToken.None);

        var exception = await Assert.ThrowsAsync<VaultException>(() =>
            fixture.Service.JoinAsync(
                fixture.Third,
                created.RoomId,
                fixture.ThirdCharacterId,
                CancellationToken.None));

        Assert.Equal(VaultError.PvpRoomFull, exception.Error);
    }

    [Fact]
    public async Task Leave_GuestOpensSeatAndHostClosesRoom()
    {
        var fixture = new Fixture();
        var created = await fixture.CreateRoomAsync();
        _ = await fixture.Service.JoinAsync(
            fixture.Guest,
            created.RoomId,
            fixture.GuestCharacterId,
            CancellationToken.None);

        fixture.Service.Leave(fixture.Guest, created.RoomId);
        Assert.Equal(PvpRoomState.Waiting, Assert.Single(fixture.Service.List()).State);

        fixture.Service.Leave(fixture.Host, created.RoomId);
        Assert.Empty(fixture.Service.List());
    }

    [Fact]
    public async Task Renew_ExtendsLeaseAndExpiredRoomIsRemoved()
    {
        var fixture = new Fixture();
        var created = await fixture.CreateRoomAsync();
        fixture.Clock.Advance(PvpRoomService.RoomLeaseDuration - TimeSpan.FromSeconds(1));

        var renewed = fixture.Service.Renew(fixture.Host, created.RoomId);
        Assert.Equal(
            fixture.Clock.GetUtcNow().Add(PvpRoomService.RoomLeaseDuration),
            renewed.ExpiresAt);

        fixture.Clock.Advance(PvpRoomService.RoomLeaseDuration + TimeSpan.FromSeconds(1));
        Assert.Empty(fixture.Service.List());
    }

    private sealed class Fixture
    {
        private readonly FakeCharacterStore _store = new();

        public Fixture()
        {
            Host = CreateIdentity("host");
            Guest = CreateIdentity("guest");
            Third = CreateIdentity("third");
            HostCharacterId = _store.Add(Host.AccountId, "HostCharacter");
            GuestCharacterId = _store.Add(Guest.AccountId, "GuestCharacter");
            ThirdCharacterId = _store.Add(Third.AccountId, "ThirdCharacter");
            Clock = new MutableTimeProvider(Start);
            Service = new PvpRoomService(_store, Clock);
        }

        public DeviceIdentity Host { get; }
        public DeviceIdentity Guest { get; }
        public DeviceIdentity Third { get; }
        public Guid HostCharacterId { get; }
        public Guid GuestCharacterId { get; }
        public Guid ThirdCharacterId { get; }
        public MutableTimeProvider Clock { get; }
        public PvpRoomService Service { get; }

        public Task<PvpRoomSnapshot> CreateRoomAsync() =>
            Service.CreateAsync(
                Host,
                HostCharacterId,
                "10.10.10.1",
                CancellationToken.None);

        private static DeviceIdentity CreateIdentity(string username) =>
            new(Guid.NewGuid(), Guid.NewGuid(), username);
    }

    private sealed class MutableTimeProvider(DateTimeOffset now) : TimeProvider
    {
        private DateTimeOffset _now = now;

        public override DateTimeOffset GetUtcNow() => _now;

        public void Advance(TimeSpan duration) => _now = _now.Add(duration);
    }

    private sealed class FakeCharacterStore : ICharacterStore
    {
        private readonly Dictionary<Guid, List<VaultCharacterSummary>> _characters = [];

        public Guid Add(Guid accountId, string name)
        {
            var character = new VaultCharacterSummary(
                Guid.NewGuid(),
                name,
                VaultCharacterClass.Amazon.ToString(),
                1,
                false,
                null);
            if (!_characters.TryGetValue(accountId, out var list))
            {
                list = [];
                _characters.Add(accountId, list);
            }

            list.Add(character);
            return character.CharacterId;
        }

        public Task<IReadOnlyList<VaultCharacterSummary>> ListAsync(
            Guid accountId,
            DateTimeOffset now,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<VaultCharacterSummary>>(
                _characters.TryGetValue(accountId, out var characters) ? characters : []);

        public Task<VaultCharacterSummary> CreateAsync(
            Guid accountId,
            Guid deviceId,
            NewVaultCharacter character,
            int maximumCharacters,
            DateTimeOffset now,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<VaultCharacterData> GetAsync(
            Guid accountId, Guid characterId, DateTimeOffset now,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<IReadOnlyList<DeletedVaultCharacterSummary>> ListDeletedAsync(
            Guid accountId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<VaultCharacterSummary> RenameAsync(
            Guid accountId, Guid deviceId, Guid characterId, long expectedVaultRevision,
            string newName, ReadOnlyMemory<byte> saveData, DateTimeOffset now,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<VaultCharacterSummary> ResetStatsAsync(
            Guid accountId, Guid deviceId, Guid characterId, long expectedVaultRevision,
            CharacterPrimaryStats stats, ReadOnlyMemory<byte> saveData, DateTimeOffset now,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task DeleteAsync(
            Guid accountId, Guid deviceId, Guid characterId, long expectedVaultRevision,
            DateTimeOffset now, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<VaultCharacterSummary> RestoreAsync(
            Guid accountId, Guid deviceId, Guid characterId, int maximumCharacters,
            DateTimeOffset now, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task PurgeDeletedAsync(
            Guid accountId, Guid deviceId, Guid characterId, DateTimeOffset now,
            CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
