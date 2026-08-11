using System.Text;

namespace JmServer.Domain;

public sealed class CharacterVaultService
{
    public static CharacterCreationPolicy CreationPolicy { get; } = new(
        MaxCharactersPerAccount: 8,
        MinimumNameLength: 2,
        MaximumNameLength: 15,
        Classes:
        [
            new(VaultCharacterClass.Amazon, "아마존", false),
            new(VaultCharacterClass.Sorceress, "원소술사", false),
            new(VaultCharacterClass.Necromancer, "강령술사", false),
            new(VaultCharacterClass.Paladin, "성기사", false),
            new(VaultCharacterClass.Barbarian, "야만용사", false),
            new(VaultCharacterClass.Druid, "드루이드", false),
            new(VaultCharacterClass.Assassin, "암살자", false),
            new(VaultCharacterClass.Warlock, "워락", true)
        ],
        Presets:
        [
            new(
                CharacterCreationPreset.PvpReady,
                "99레벨 완성 캐릭터",
                "모든 퀘스트·웨이포인트·헬 난이도 개방 · 영구 퀘스트 보상 획득",
                Level: 99,
                CompletesQuests: true,
                UnlocksAllWaypoints: true,
                UnspentStatPoints: 505,
                UnspentSkillPoints: 110)
        ]);

    private readonly ICharacterStore _store;
    private readonly ICharacterSaveFactory _saveFactory;
    private readonly ICharacterSaveEditor _saveEditor;
    private readonly TimeProvider _timeProvider;

    public CharacterVaultService(
        ICharacterStore store,
        ICharacterSaveFactory saveFactory,
        TimeProvider? timeProvider = null)
        : this(store, saveFactory, UnsupportedSaveEditor.Instance, timeProvider)
    {
    }

    public CharacterVaultService(
        ICharacterStore store,
        ICharacterSaveFactory saveFactory,
        ICharacterSaveEditor saveEditor,
        TimeProvider? timeProvider = null)
    {
        _store = store;
        _saveFactory = saveFactory;
        _saveEditor = saveEditor;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public Task<IReadOnlyList<VaultCharacterSummary>> ListAsync(
        Guid accountId,
        CancellationToken cancellationToken) =>
        _store.ListAsync(accountId, _timeProvider.GetUtcNow(), cancellationToken);

    public async Task<VaultCharacterSummary> CreateAsync(
        Guid accountId,
        Guid deviceId,
        string name,
        VaultCharacterClass characterClass,
        CharacterCreationPreset preset,
        CancellationToken cancellationToken)
    {
        var normalizedName = ValidateAndNormalizeName(name);
        var classDefinition = CreationPolicy.Classes.SingleOrDefault(
                                  item => item.CharacterClass == characterClass)
                              ?? throw new VaultException(
                                  VaultError.CharacterCreationInvalid,
                                  "지원하지 않는 캐릭터 직업입니다.");
        _ = classDefinition;
        if (!Enum.IsDefined(preset))
        {
            throw new VaultException(
                VaultError.CharacterCreationInvalid,
                "지원하지 않는 캐릭터 생성 프리셋입니다.");
        }

        // FreshStart/PvpReady 값을 쓰는 이전 클라이언트도 서버의 단일 생성 정책으로 정규화한다.
        var presetDefinition = CreationPolicy.Presets.Single();
        var now = _timeProvider.GetUtcNow();
        var saveData = _saveFactory.Create(
            new CharacterSaveRequest(
                normalizedName,
                characterClass,
                presetDefinition,
                now));
        if (saveData.Length is 0 or > ProfileBundleCodec.MaxFileLength)
        {
            throw new VaultException(
                VaultError.CharacterCreationInvalid,
                "생성된 캐릭터 세이브의 크기가 올바르지 않습니다.");
        }

        var initialSharedStash = _saveFactory.CreateInitialSharedStash();
        if (initialSharedStash is not null &&
            (!ProfileSavePolicy.IsSharedStash(initialSharedStash.RelativePath) ||
             initialSharedStash.Data.Length is 0 or > ProfileBundleCodec.MaxFileLength))
        {
            throw new VaultException(
                VaultError.CharacterCreationInvalid,
                "The initial D2R shared stash is invalid.");
        }

        return await _store.CreateAsync(
            accountId,
            deviceId,
            new NewVaultCharacter(
                normalizedName,
                characterClass,
                presetDefinition.Preset,
                presetDefinition.Level,
                saveData,
                initialSharedStash),
            CreationPolicy.MaxCharactersPerAccount,
            now,
            cancellationToken);
    }

    public Task<IReadOnlyList<DeletedVaultCharacterSummary>> ListDeletedAsync(
        Guid accountId,
        CancellationToken cancellationToken) =>
        _store.ListDeletedAsync(accountId, cancellationToken);

    public async Task<CharacterManagementSnapshot> GetManagementAsync(
        Guid accountId,
        Guid characterId,
        CancellationToken cancellationToken)
    {
        var character = await _store.GetAsync(
            accountId,
            characterId,
            _timeProvider.GetUtcNow(),
            cancellationToken);
        return new CharacterManagementSnapshot(
            ToSummary(character),
            _saveEditor.ReadPrimaryStats(character.SaveData));
    }

    public async Task<VaultCharacterSummary> RenameAsync(
        Guid accountId,
        Guid deviceId,
        Guid characterId,
        string newName,
        CancellationToken cancellationToken)
    {
        var normalizedName = ValidateAndNormalizeName(newName);
        var now = _timeProvider.GetUtcNow();
        var character = await _store.GetAsync(accountId, characterId, now, cancellationToken);
        if (string.Equals(character.Name, normalizedName, StringComparison.Ordinal))
        {
            return ToSummary(character);
        }

        var renamedSave = _saveEditor.Rename(character.SaveData, normalizedName);
        return await _store.RenameAsync(
            accountId,
            deviceId,
            characterId,
            character.VaultRevision,
            normalizedName,
            renamedSave,
            now,
            cancellationToken);
    }

    public async Task<CharacterManagementSnapshot> ResetStatsAsync(
        Guid accountId,
        Guid deviceId,
        Guid characterId,
        CancellationToken cancellationToken)
    {
        var now = _timeProvider.GetUtcNow();
        var character = await _store.GetAsync(accountId, characterId, now, cancellationToken);
        var resetSave = _saveEditor.ResetPrimaryStats(
            character.SaveData,
            character.CharacterClass);
        var stats = _saveEditor.ReadPrimaryStats(resetSave);
        var updated = await _store.ResetStatsAsync(
            accountId,
            deviceId,
            characterId,
            character.VaultRevision,
            stats,
            resetSave,
            now,
            cancellationToken);
        return new CharacterManagementSnapshot(updated, stats);
    }

    public async Task DeleteAsync(
        Guid accountId,
        Guid deviceId,
        Guid characterId,
        CancellationToken cancellationToken)
    {
        var now = _timeProvider.GetUtcNow();
        var character = await _store.GetAsync(accountId, characterId, now, cancellationToken);
        await _store.DeleteAsync(
            accountId,
            deviceId,
            characterId,
            character.VaultRevision,
            now,
            cancellationToken);
    }

    public Task<VaultCharacterSummary> RestoreAsync(
        Guid accountId,
        Guid deviceId,
        Guid characterId,
        CancellationToken cancellationToken) =>
        _store.RestoreAsync(
            accountId,
            deviceId,
            characterId,
            CreationPolicy.MaxCharactersPerAccount,
            _timeProvider.GetUtcNow(),
            cancellationToken);

    public Task PurgeDeletedAsync(
        Guid accountId,
        Guid deviceId,
        Guid characterId,
        CancellationToken cancellationToken) =>
        _store.PurgeDeletedAsync(
            accountId,
            deviceId,
            characterId,
            _timeProvider.GetUtcNow(),
            cancellationToken);

    private static VaultCharacterSummary ToSummary(VaultCharacterData character) =>
        new(
            character.CharacterId,
            character.Name,
            character.CharacterClass.ToString(),
            character.VaultRevision,
            character.IsLeased,
            character.LeaseExpiresAt);

    private static string ValidateAndNormalizeName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw InvalidName();
        }

        var normalized = name.Trim().Normalize(NormalizationForm.FormC);
        var runes = normalized.EnumerateRunes().ToArray();
        if (runes.Length < CreationPolicy.MinimumNameLength ||
            runes.Length > CreationPolicy.MaximumNameLength ||
            !Rune.IsLetter(runes[0]))
        {
            throw InvalidName();
        }

        var separatorCount = 0;
        foreach (var rune in runes.Skip(1))
        {
            if (Rune.IsLetterOrDigit(rune))
            {
                continue;
            }

            if (rune.Value is '-' or '_' && ++separatorCount <= 1)
            {
                continue;
            }

            throw InvalidName();
        }

        return normalized;
    }

    private static VaultException InvalidName() => new(
        VaultError.CharacterNameInvalid,
        $"캐릭터 이름은 글자로 시작하는 {CreationPolicy.MinimumNameLength}~{CreationPolicy.MaximumNameLength}자의 글자·숫자 조합이어야 하며 '-' 또는 '_'는 하나만 사용할 수 있습니다.");

    private sealed class UnsupportedSaveEditor : ICharacterSaveEditor
    {
        public static UnsupportedSaveEditor Instance { get; } = new();

        public CharacterPrimaryStats ReadPrimaryStats(ReadOnlyMemory<byte> saveData) =>
            throw new NotSupportedException("Character save management is not configured.");

        public byte[] Rename(ReadOnlyMemory<byte> saveData, string newName) =>
            throw new NotSupportedException("Character save management is not configured.");

        public byte[] ResetPrimaryStats(
            ReadOnlyMemory<byte> saveData,
            VaultCharacterClass characterClass) =>
            throw new NotSupportedException("Character save management is not configured.");
    }
}
