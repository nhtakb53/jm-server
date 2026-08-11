using JmServer.Domain;

namespace JmServer.Domain.Tests;

public sealed class CharacterVaultServiceTests
{
    [Fact]
    public async Task List_UsesConfiguredClock()
    {
        var now = new DateTimeOffset(2026, 8, 8, 12, 0, 0, TimeSpan.Zero);
        var store = new RecordingCharacterStore();
        var service = new CharacterVaultService(
            store,
            new StubSaveFactory(),
            new FixedTimeProvider(now));

        await service.ListAsync(Guid.NewGuid(), CancellationToken.None);

        Assert.Equal(now, store.ListNow);
    }

    [Fact]
    public async Task Create_UsesServerPolicyAndNormalizedName()
    {
        var now = new DateTimeOffset(2026, 8, 8, 12, 0, 0, TimeSpan.Zero);
        var store = new RecordingCharacterStore();
        var factory = new StubSaveFactory();
        var service = new CharacterVaultService(
            store,
            factory,
            new FixedTimeProvider(now));

        var result = await service.CreateAsync(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "  악콩이2  ",
            VaultCharacterClass.Warlock,
            CharacterCreationPreset.PvpReady,
            CancellationToken.None);

        Assert.Equal("악콩이2", result.Name);
        Assert.Equal("악콩이2", factory.Request?.Name);
        Assert.Equal((byte)99, factory.Request?.Preset.Level);
        Assert.Equal(505, factory.Request?.Preset.UnspentStatPoints);
        Assert.Equal(110, factory.Request?.Preset.UnspentSkillPoints);
        Assert.Equal(CharacterCreationPreset.PvpReady, store.CreatedCharacter?.Preset);
        Assert.Equal(CharacterVaultService.CreationPolicy.MaxCharactersPerAccount, store.MaximumCharacters);
        Assert.Equal(now, store.CreateNow);
    }

    [Fact]
    public void CreationPolicy_ExposesOneCompletedLevel99Preset()
    {
        var preset = Assert.Single(CharacterVaultService.CreationPolicy.Presets);
        Assert.Equal(99, preset.Level);
        Assert.Equal(505, preset.UnspentStatPoints);
        Assert.Equal(110, preset.UnspentSkillPoints);
        Assert.True(preset.CompletesQuests);
        Assert.True(preset.UnlocksAllWaypoints);
    }

    [Theory]
    [InlineData(CharacterCreationPreset.FreshStart)]
    [InlineData(CharacterCreationPreset.PvpReady)]
    public async Task Create_NormalizesLegacyPresetRequestsToLevel99(
        CharacterCreationPreset requestedPreset)
    {
        var store = new RecordingCharacterStore();
        var factory = new StubSaveFactory();
        var service = new CharacterVaultService(store, factory);

        await service.CreateAsync(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "LegacyHero",
            VaultCharacterClass.Amazon,
            requestedPreset,
            CancellationToken.None);

        Assert.Equal((byte)99, factory.Request?.Preset.Level);
        Assert.Equal(CharacterCreationPreset.PvpReady, store.CreatedCharacter?.Preset);
    }

    [Theory]
    [InlineData("")]
    [InlineData("1악콩")]
    [InlineData("악!콩")]
    [InlineData("악-콩_이")]
    public async Task Create_RejectsNamesOutsidePolicy(string name)
    {
        var service = new CharacterVaultService(
            new RecordingCharacterStore(),
            new StubSaveFactory());

        var exception = await Assert.ThrowsAsync<VaultException>(() => service.CreateAsync(
            Guid.NewGuid(),
            Guid.NewGuid(),
            name,
            VaultCharacterClass.Amazon,
            CharacterCreationPreset.FreshStart,
            CancellationToken.None));

        Assert.Equal(VaultError.CharacterNameInvalid, exception.Error);
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class RecordingCharacterStore : ICharacterStore
    {
        public DateTimeOffset? ListNow { get; private set; }
        public DateTimeOffset? CreateNow { get; private set; }
        public int? MaximumCharacters { get; private set; }
        public NewVaultCharacter? CreatedCharacter { get; private set; }

        public Task<IReadOnlyList<VaultCharacterSummary>> ListAsync(
            Guid accountId,
            DateTimeOffset now,
            CancellationToken cancellationToken)
        {
            ListNow = now;
            return Task.FromResult<IReadOnlyList<VaultCharacterSummary>>([]);
        }

        public Task<VaultCharacterSummary> CreateAsync(
            Guid accountId,
            Guid deviceId,
            NewVaultCharacter character,
            int maximumCharacters,
            DateTimeOffset now,
            CancellationToken cancellationToken)
        {
            CreateNow = now;
            MaximumCharacters = maximumCharacters;
            CreatedCharacter = character;
            return Task.FromResult(new VaultCharacterSummary(
                Guid.NewGuid(),
                character.Name,
                character.CharacterClass.ToString(),
                1,
                false,
                null));
        }

        public Task<VaultCharacterData> GetAsync(
            Guid accountId,
            Guid characterId,
            DateTimeOffset now,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<IReadOnlyList<DeletedVaultCharacterSummary>> ListDeletedAsync(
            Guid accountId,
            CancellationToken cancellationToken) => throw new NotSupportedException();

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

    private sealed class StubSaveFactory : ICharacterSaveFactory
    {
        public CharacterSaveRequest? Request { get; private set; }

        public byte[] Create(CharacterSaveRequest request)
        {
            Request = request;
            return [1, 2, 3];
        }
    }
}
