using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace JmServer.GameIntegration;

public static class InGameSupplyModBuilder
{
    public const string SourceRevision = "cb27a8f574b873807e15cf9613d04d655cabdb60";
    public const string UiSourceRevision = "4474b1eaa69d99b29ae492ded56ab7ad12ea4ae8";
    public const int GambleUniqueWeight = 1_000;
    public const int GambleSetWeight = 1_000;
    public const int GambleRareWeight = 80_000;
    public const int GambleMagicWeight = 18_000;
    public const int GambleCost = 1;
    public const int PreferredRollPercentile = 75;
    public const int PreferredAffixRollPercentile = 90;
    public const int MaximumAffixWeightMultiplier = 15;
    public const int PreferredCharmAffixWeightMultiplier = 8;
    public const int ExpectedVanillaCraftRecipeCount = 36;
    public const int PackageSchemaVersion = 4;
    public const int VendorPriceReductionPercent = 99;
    public const int TomeStackSize = 100;
    public const int KeyStackSize = 50;
    public const int ProjectileStackSize = 500;
    public const int FullAutomapPresetCount = 1_092;
    public const string SelectorVendor = "Akara";
    internal const string SelectorAssetRoot = "jm_selectors/";
    internal const string UniqueSwordSelectorAsset = SelectorAssetRoot + "unique_sword";
    // D2R's shipped tables stay below 30,000. Established total-conversion mods use the
    // 50,000 range for custom strings; values in the 60,000 range are not resolved by the
    // current 3.2 client and fall back to an unrelated base-item string.
    public const int FirstCustomStringId = 50_000;
    private const int MaximumCatalogLevel = 85;

    private static readonly IReadOnlyDictionary<string, string> CategorySelectorAssets =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["unique-sword"] = "unique_sword",
            ["unique-axe"] = "unique_axe",
            ["unique-blunt"] = "unique_blunt",
            ["unique-bow"] = "unique_bow",
            ["unique-pole"] = "unique_pole",
            ["unique-dagger"] = "unique_dagger",
            ["unique-caster"] = "unique_caster",
            ["unique-class-weapon"] = "unique_class_weapon",
            ["unique-body"] = "unique_body",
            ["unique-helm"] = "unique_helm",
            ["unique-shield"] = "unique_shield",
            ["unique-gloves"] = "unique_gloves",
            ["unique-boots"] = "unique_boots",
            ["unique-belts"] = "unique_belts",
            ["unique-class-armor"] = "unique_class_armor",
            ["unique-rings"] = "unique_rings",
            ["unique-amulets"] = "unique_amulets",
            ["unique-charms"] = "unique_charms",
            ["unique-other"] = "unique_other",
            ["set-weapons"] = "set_weapons",
            ["set-body"] = "set_body",
            ["set-helm"] = "set_helm",
            ["set-shield"] = "set_shield",
            ["set-gloves"] = "set_gloves",
            ["set-boots"] = "set_boots",
            ["set-belts"] = "set_belts",
            ["set-class-armor"] = "set_class_armor",
            ["set-jewelry"] = "set_jewelry",
            ["set-other"] = "set_other",
            ["base-sword"] = "base_sword",
            ["base-axe"] = "base_axe",
            ["base-blunt"] = "base_blunt",
            ["base-bow"] = "base_bow",
            ["base-pole"] = "base_pole",
            ["base-dagger"] = "base_dagger",
            ["base-caster"] = "base_caster",
            ["base-class-weapon"] = "base_class_weapon",
            ["base-body"] = "base_body",
            ["base-helm"] = "base_helm",
            ["base-shield"] = "base_shield",
            ["base-gloves"] = "base_gloves",
            ["base-boots"] = "base_boots",
            ["base-belts"] = "base_belts",
            ["base-class-armor"] = "base_class_armor",
            ["materials-runes"] = "materials_runes",
            ["materials-gems"] = "materials_gems",
            ["materials-charms"] = "materials_charms",
            ["skill-charms-amazon"] = "skill_charms_amazon",
            ["skill-charms-sorceress"] = "skill_charms_sorceress",
            ["skill-charms-necromancer"] = "skill_charms_necromancer",
            ["skill-charms-paladin"] = "skill_charms_paladin",
            ["skill-charms-barbarian"] = "skill_charms_barbarian",
            ["skill-charms-druid"] = "skill_charms_druid",
            ["skill-charms-assassin"] = "skill_charms_assassin",
            ["skill-charms-warlock"] = "skill_charms_warlock",
            ["popular-small-charms"] = "popular_small_charms",
            ["popular-large-charms"] = "popular_large_charms"
        };

    private static readonly IReadOnlyDictionary<BaseCreationMode, string> BaseModeSelectorAssets =
        new Dictionary<BaseCreationMode, string>
        {
            [BaseCreationMode.Normal] = "base_mode_normal",
            [BaseCreationMode.Superior] = "base_mode_superior",
            [BaseCreationMode.Magic] = "base_mode_magic",
            [BaseCreationMode.Ethereal] = "base_mode_ethereal",
            [BaseCreationMode.SuperiorEthereal] = "base_mode_superior_ethereal"
        };

    private static readonly IReadOnlyDictionary<string, string> CharmBonusSelectorAssets =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["random"] = "charm_bonus_random",
            ["vitality"] = "charm_bonus_vitality",
            ["fhr"] = "charm_bonus_fhr",
            ["movement"] = "charm_bonus_movement",
            ["strength"] = "charm_bonus_strength",
            ["dexterity"] = "charm_bonus_dexterity",
            ["magic-find"] = "charm_bonus_magic_find"
        };

    private static readonly string[] StaticHdSelectorAssets = CreateStaticHdSelectorAssets();

    private static readonly HashSet<string> OriginalSingleCarryUniqueCharms =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "Gheed's Fortune",
            "Annihilus",
            "Hellfire Torch"
        };

    private static readonly HashSet<string> NonGambleItemTypes =
        new(["scha", "mcha", "lcha"], StringComparer.OrdinalIgnoreCase);

    private static readonly BaseCreationModeDefinition[] BaseCreationModes =
    [
        new(BaseCreationMode.Normal, "Normal base workstone", "일반 베이스 작업석", "nor", false),
        new(BaseCreationMode.Superior, "Superior base workstone", "고급 베이스 작업석", "hiq", false),
        new(BaseCreationMode.Magic, "Magic craft-base workstone", "마법 크래프트 베이스 작업석", "mag", false),
        new(BaseCreationMode.Ethereal, "Ethereal base workstone", "에테리얼 베이스 작업석", "nor,eth", true),
        new(BaseCreationMode.SuperiorEthereal, "Superior ethereal workstone", "고급 에테리얼 작업석", "hiq,eth", true)
    ];

    private static readonly string[] ItemNameLocales =
    [
        "enUS", "zhTW", "deDE", "esES", "frFR", "itIT", "koKR",
        "plPL", "esMX", "jaJP", "ptBR", "ruRU", "zhCN"
    ];

    // Rune names and the four normal-grade gems are resolved from other shipped
    // localization tables, so they are absent from the pinned item-names.json input.
    private static readonly IReadOnlyDictionary<string, string> KoreanItemNameOverrides =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["r01"] = "엘 룬",
            ["r02"] = "엘드 룬",
            ["r03"] = "티르 룬",
            ["r04"] = "네프 룬",
            ["r05"] = "에드 룬",
            ["r06"] = "아이드 룬",
            ["r07"] = "탈 룬",
            ["r08"] = "랄 룬",
            ["r09"] = "오르트 룬",
            ["r10"] = "주울 룬",
            ["r11"] = "앰 룬",
            ["r12"] = "솔 룬",
            ["r13"] = "샤엘 룬",
            ["r14"] = "돌 룬",
            ["r15"] = "헬 룬",
            ["r16"] = "이오 룬",
            ["r17"] = "룸 룬",
            ["r18"] = "코 룬",
            ["r19"] = "팔 룬",
            ["r20"] = "렘 룬",
            ["r21"] = "풀 룬",
            ["r22"] = "우움 룬",
            ["r23"] = "말 룬",
            ["r24"] = "이스트 룬",
            ["r25"] = "굴 룬",
            ["r26"] = "벡스 룬",
            ["r27"] = "오움 룬",
            ["r28"] = "로 룬",
            ["r29"] = "수르 룬",
            ["r30"] = "베르 룬",
            ["r31"] = "자 룬",
            ["r32"] = "참 룬",
            ["r33"] = "조드 룬",
            ["gsb"] = "사파이어",
            ["gsg"] = "에메랄드",
            ["gsr"] = "루비",
            ["gsw"] = "다이아몬드"
        };

    private static readonly string[] WeaponTypes =
    [
        "swor", "axe", "taxe", "club", "mace", "hamm", "scep", "bow", "xbow",
        "pole", "spea", "jave", "tpot", "knif", "tkni", "staf", "wand", "orb",
        "h2h", "h2h2", "abow", "aspe", "ajav"
    ];

    private static readonly IReadOnlyDictionary<string, string> ExpectedSourceHashes =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["armor.txt"] = "734872B6DDE85073C6EBAF4199EC4BD2B30EFCBF9F0E60E381AF2E22662A1B4F",
            ["weapons.txt"] = "B0890901F87FD42FCB474CE835B6E1A9FE1E66DBAB4E7C2F236A3D9C3224F923",
            ["misc.txt"] = "4B7259D1DB5FA0A9DA1D9530A019066CD40806EDE91A4C0F4C69F81AEBFFDB7F",
            ["gamble.txt"] = "FE26418CBFD3B774197D55D39525F6DDDB5723203BE9C38AEB0B552A09781635",
            ["difficultylevels.txt"] = "8DC24E9F15C236F79DBA3B039899155CF8E512BA387D97473583882358340C3E",
            ["uniqueitems.txt"] = "1EB8DAFF5DE5E35704EB28FEBC84FB18EC0724D8E66F880E723399C751365F81",
            ["setitems.txt"] = "846F6328656D72A83716571E433A9030D8BA4A298DC81691C4CAAD0BEC7DFD5E",
            ["cubemain.txt"] = "C2AF6EF0C6EBF9EF5923CCF94CBD0CD1449AA46BDF469C07C8F170FC10E19785",
            ["properties.txt"] = "382528362751C68646E98D4A296C357276AB9A1EE0B7053C29E5DF6B6252A1EF",
            ["magicprefix.txt"] = "3CF2B5F730D5B93388146ACDB6E2A7CFFB0AA0BF23E2C95B2D80D00841720381",
            ["magicsuffix.txt"] = "A697A051D988C78CBBBC8FE30D565CF473989817CE4D8A2BB08A321E2BC15C43",
            ["automagic.txt"] = "AA5B30AA2BF51460955A5FB3AD5E9017607EAC8E8CCE40F9C7B08AEB9FFCB7DF",
            ["qualityitems.txt"] = "2AADD8C89316F3C58DD958839776AB01C3937A5E2E5D03188FC26B27309F4444",
            ["runes.txt"] = "767EDF4FB0F8391A61ED936374618699FE46057765F2D18DF985750AF67D13EB",
            ["lvlprest.txt"] = "C0F715A9368F9F7CCFC3C52F3C1FBD782914945877FA1CD3A711E13C13320037",
            ["items.json"] = "53A3A708AE83073F810CA64B1091FE5B77ABC686548377E34700B63D48885AD2",
            ["item-names.json"] = "E170DBEF74DD3608B19532C4397253B65C094599F756B9B1684F377DE1D1C4C2",
            ["ui.json"] = "B3FA36EE17F57A3A78F96C698EAA26D921EB2CAE4C2633DB38F14155F4D5FAA5"
        };

    // System.Text.Json-normalized copies of the same pinned D2R JSON records.
    // This lets the embedded package be used to reproduce the source JSON after removing
    // appended JM selector records, while retaining exact hash validation.
    private static readonly IReadOnlyDictionary<string, string> NormalizedSourceHashes =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["items.json"] = "442495131AA54E0B48BB3D07C58E3EAA4E60FC711EE8BBC0D8918E5A463DC872",
            ["item-names.json"] = "56F9D6D9C8F1352DA4551D7A2292BDEDA7EA951111141EC5C9CBC1405EEB6305"
        };

    private static readonly IReadOnlyDictionary<string, string> GeneratedSourceHashes =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["items.json"] = "AC97458AA515AC8C07CEF0E01AECB9AFD0CB9F4F7FDC6062A82C929DCA31599B",
            ["item-names.json"] = "3AB46E9366AED462BCC3AC1F8D395E9BDE38DF9B07AD1426AD8C4611B57E580F",
            ["ui.json"] = "1F34E1CA4C30829F7B021382B4F200488289BC374C79EDCC104CDFE19D8D197D"
        };

    // These property functions use min/max as a beneficial numeric range or as
    // minimum/maximum damage endpoints. Parameter/identity-bearing functions such
    // as procs, charged skills and random-class skills are deliberately excluded.
    private static readonly HashSet<int> PreferredNumericPropertyFunctions =
        [1, 3, 5, 6, 7, 8, 9, 10, 13, 14, 15, 17, 18, 21, 22, 24];

    private static readonly HashSet<string> NonPowerRollPropertyCodes =
        new(StringComparer.OrdinalIgnoreCase)
        {
            // Structural selectors: min/max are ids rather than a power roll.
            "randclassskill",
            "skill-rand",
            // Cosmetic or explicitly unused properties in the pinned 3.2 table.
            "bloody",
            "color",
            "time",
            "herb",
            "throw",
            "state",
            "att-mon%",
            "dmg-mon%",
            "dmg-elem-min",
            "dmg-elem-max",
            "res-all-max",
            // These are real numeric stats, but a larger minimum is not a better roll.
            // Requirement reduction is better toward the negative end, while raising
            // the required character level is always a penalty.
            "ease",
            "levelreq"
        };

    private static readonly SupplyCategory[] Categories =
    [
        new("unique-sword", "유니크 검", SelectorVendor, SupplyQuality.Unique, ["swor"]),
        new("unique-axe", "유니크 도끼", SelectorVendor, SupplyQuality.Unique, ["axe", "taxe"]),
        new("unique-blunt", "유니크 둔기·홀", SelectorVendor, SupplyQuality.Unique, ["club", "mace", "hamm", "scep"]),
        new("unique-bow", "유니크 활·석궁", SelectorVendor, SupplyQuality.Unique, ["bow", "xbow"]),
        new("unique-pole", "유니크 창·투창", SelectorVendor, SupplyQuality.Unique, ["pole", "spea", "jave", "tpot"]),
        new("unique-dagger", "유니크 단검·투척", SelectorVendor, SupplyQuality.Unique, ["knif", "tkni"]),
        new("unique-caster", "유니크 지팡이·완드·오브", SelectorVendor, SupplyQuality.Unique, ["staf", "wand", "orb"]),
        new("unique-class-weapon", "유니크 직업 전용 무기", SelectorVendor, SupplyQuality.Unique, ["h2h", "h2h2", "abow", "aspe", "ajav"]),
        new("unique-body", "유니크 갑옷", SelectorVendor, SupplyQuality.Unique, ["tors"]),
        new("unique-helm", "유니크 투구·서클릿", SelectorVendor, SupplyQuality.Unique, ["helm", "circ"]),
        new("unique-shield", "유니크 방패", SelectorVendor, SupplyQuality.Unique, ["shie"]),
        new("unique-gloves", "유니크 장갑", SelectorVendor, SupplyQuality.Unique, ["glov"]),
        new("unique-boots", "유니크 신발", SelectorVendor, SupplyQuality.Unique, ["boot"]),
        new("unique-belts", "유니크 벨트", SelectorVendor, SupplyQuality.Unique, ["belt"]),
        new("unique-class-armor", "유니크 직업 전용 방어구", SelectorVendor, SupplyQuality.Unique, ["phlm", "pelt", "head", "ashd", "grim"]),
        new("unique-rings", "유니크 반지", SelectorVendor, SupplyQuality.Unique, ["ring"]),
        new("unique-amulets", "유니크 목걸이", SelectorVendor, SupplyQuality.Unique, ["amul"]),
        new("unique-charms", "유니크 부적·보석", SelectorVendor, SupplyQuality.Unique, ["scha", "mcha", "lcha", "jewl", "cjwl"]),
        new("unique-other", "기타 유니크", SelectorVendor, SupplyQuality.Unique, []),
        new("set-weapons", "세트 무기", SelectorVendor, SupplyQuality.Set, WeaponTypes),
        new("set-body", "세트 갑옷", SelectorVendor, SupplyQuality.Set, ["tors"]),
        new("set-helm", "세트 투구·서클릿", SelectorVendor, SupplyQuality.Set, ["helm", "circ"]),
        new("set-shield", "세트 방패", SelectorVendor, SupplyQuality.Set, ["shie"]),
        new("set-gloves", "세트 장갑", SelectorVendor, SupplyQuality.Set, ["glov"]),
        new("set-boots", "세트 신발", SelectorVendor, SupplyQuality.Set, ["boot"]),
        new("set-belts", "세트 벨트", SelectorVendor, SupplyQuality.Set, ["belt"]),
        new("set-class-armor", "세트 직업 전용 방어구", SelectorVendor, SupplyQuality.Set, ["phlm", "pelt", "head", "ashd", "grim"]),
        new("set-jewelry", "세트 반지·목걸이", SelectorVendor, SupplyQuality.Set, ["ring", "amul"]),
        new("set-other", "기타 세트", SelectorVendor, SupplyQuality.Set, [])
    ];

    public static async Task<SupplyBuildResult> BuildAsync(
        string sourceDirectory,
        string outputDirectory,
        CancellationToken cancellationToken = default)
    {
        sourceDirectory = Path.GetFullPath(sourceDirectory);
        outputDirectory = Path.GetFullPath(outputDirectory);
        ValidateSourceFiles(sourceDirectory);

        var armor = D2TsvTable.Load(Path.Combine(sourceDirectory, "armor.txt"));
        var weapons = D2TsvTable.Load(Path.Combine(sourceDirectory, "weapons.txt"));
        var misc = D2TsvTable.Load(Path.Combine(sourceDirectory, "misc.txt"));
        var gamble = D2TsvTable.Load(Path.Combine(sourceDirectory, "gamble.txt"));
        var difficulties = D2TsvTable.Load(Path.Combine(sourceDirectory, "difficultylevels.txt"));
        var uniqueItems = D2TsvTable.Load(Path.Combine(sourceDirectory, "uniqueitems.txt"));
        var setItems = D2TsvTable.Load(Path.Combine(sourceDirectory, "setitems.txt"));
        var cubeMain = D2TsvTable.Load(Path.Combine(sourceDirectory, "cubemain.txt"));
        var properties = D2TsvTable.Load(Path.Combine(sourceDirectory, "properties.txt"));
        var magicPrefixes = D2TsvTable.Load(Path.Combine(sourceDirectory, "magicprefix.txt"));
        var magicSuffixes = D2TsvTable.Load(Path.Combine(sourceDirectory, "magicsuffix.txt"));
        var autoMagic = D2TsvTable.Load(Path.Combine(sourceDirectory, "automagic.txt"));
        var qualityItems = D2TsvTable.Load(Path.Combine(sourceDirectory, "qualityitems.txt"));
        var runewords = D2TsvTable.Load(Path.Combine(sourceDirectory, "runes.txt"));
        var levelPresets = D2TsvTable.Load(Path.Combine(sourceDirectory, "lvlprest.txt"));
        var hdItems = JsonNode.Parse(
                await File.ReadAllTextAsync(
                    Path.Combine(sourceDirectory, "items.json"),
                    cancellationToken))?.AsArray()
            ?? throw new InvalidDataException("The pinned D2R HD item mapping is invalid.");
        var itemNameStrings = JsonNode.Parse(
                await File.ReadAllTextAsync(
                    Path.Combine(sourceDirectory, "item-names.json"),
                    cancellationToken))?.AsArray()
            ?? throw new InvalidDataException("The pinned D2R item-name table is invalid.");
        RemovePreviouslyGeneratedJsonRecords(hdItems, itemNameStrings);
        if (itemNameStrings.Any(node =>
                node?["id"]?.GetValue<int>() >= FirstCustomStringId))
        {
            throw new InvalidDataException(
                $"The pinned D2R item-name table already uses reserved string IDs at or above " +
                $"{FirstCustomStringId}.");
        }
        var nextItemNameStringId = FirstCustomStringId;
        DisambiguateRainbowFacetVariants(
            uniqueItems,
            itemNameStrings,
            ref nextItemNameStringId);
        var itemNameLocalizationIndex = CreateItemNameLocalizationIndex(itemNameStrings);
        var hdItemAssets = CreateHdItemAssetIndex(hdItems);
        var uiStrings = JsonNode.Parse(
                await File.ReadAllTextAsync(
                    Path.Combine(sourceDirectory, "ui.json"),
                    cancellationToken))?.AsArray()
            ?? throw new InvalidDataException("The pinned D2R UI string table is invalid.");
        var hostLecture = uiStrings.SingleOrDefault(node =>
            string.Equals(
                node?["Key"]?.GetValue<string>(),
                "strCreateServerLecture",
                StringComparison.Ordinal))
            ?? throw new InvalidDataException(
                "The pinned D2R UI string table has no TCP/IP host description.");
        hostLecture["koKR"] =
            "정만서버 런처에서 호스트를 시작한 뒤 '게임 호스트'를 누르세요. " +
            "친구는 런처가 안내한 주소로 참가합니다.";
        var joinLecture = uiStrings.SingleOrDefault(node =>
            string.Equals(
                node?["Key"]?.GetValue<string>(),
                "strJoinServerLecture",
                StringComparison.Ordinal))
            ?? throw new InvalidDataException(
                "The pinned D2R UI string table has no TCP/IP join description.");
        joinLecture["koKR"] =
            "정만서버 런처에서 방 참가를 시작한 뒤 '게임 참가'를 누르세요. " +
            "주소에는 런처가 안내한 127.0.0.1을 입력합니다.";

        var baseItems = CreateBaseItemIndex(armor, weapons, misc);
        var charmSelectorPlan = CharmSelectorPlanner.Create(magicPrefixes, magicSuffixes);
        var reservedSelectorCodes = baseItems.Keys
            .Concat(hdItemAssets.Keys)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var uniqueTargets = CreateUniqueTargets(uniqueItems, baseItems);
        var setTargets = CreateSetTargets(setItems, baseItems);
        var targets = uniqueTargets.Concat(setTargets).ToArray();
        if (targets.Any(target => target.Category is null))
        {
            throw new InvalidDataException("At least one catalog target was not assigned to a category.");
        }

        ApplyCatalogAvailability(uniqueItems, setItems, uniqueTargets, setTargets);
        var propertyFunctions = CreatePropertyFunctionIndex(properties);
        ApplyPreferredVariableRolls(uniqueItems, uniqueTargets, 12, propertyFunctions);
        ApplyPreferredVariableRolls(setItems, setTargets, 9, propertyFunctions);
        ApplyPreferredAffixWeights(magicPrefixes);
        ApplyPreferredAffixWeights(magicSuffixes);
        ApplyPreferredAffixWeights(autoMagic);
        ApplyPreferredCharmAffixWeights(magicSuffixes, charmSelectorPlan.PreferredSuffixIds);
        ApplyPreferredAffixVariableRolls(magicPrefixes, propertyFunctions);
        ApplyPreferredAffixVariableRolls(magicSuffixes, propertyFunctions);
        ApplyPreferredAffixVariableRolls(autoMagic, propertyFunctions);
        ApplyPreferredSuperiorRolls(qualityItems);
        ApplyPreferredRunewordVariableRolls(runewords, propertyFunctions);
        ApplyPreferredCubeVariableRolls(cubeMain, propertyFunctions);
        ApplyPreferredBaseDefenseRolls(armor);
        ApplyFullAutomapReveal(levelPresets);
        ApplyVendorPriceReduction(uniqueItems);
        ApplyGamblingTables(armor, weapons, misc, gamble, difficulties, targets);
        ApplyConvenienceItemData(armor, weapons, misc);
        var selectorCount = AddSelectorsAndRecipes(
            misc,
            cubeMain,
            targets,
            reservedSelectorCodes,
            hdItems,
            itemNameStrings,
            itemNameLocalizationIndex,
            ref nextItemNameStringId);
        var baseModeTokens = AddBaseCreationModeTokens(
            misc,
            reservedSelectorCodes,
            hdItems,
            itemNameStrings,
            ref nextItemNameStringId);
        var charmBonusTokens = AddCharmBonusTokens(
            misc,
            charmSelectorPlan.BonusModes,
            reservedSelectorCodes,
            hdItems,
            itemNameStrings,
            ref nextItemNameStringId);
        var craftTokenCode = AddCraftToken(
            misc,
            reservedSelectorCodes,
            hdItems,
            itemNameStrings,
            ref nextItemNameStringId);
        var convenienceCategories = CreateConvenienceCategories(baseItems, misc);
        var convenienceSelectorCount = AddConvenienceSelectorsAndRecipes(
            misc,
            cubeMain,
            convenienceCategories,
            reservedSelectorCodes,
            hdItems,
            itemNameStrings,
            itemNameLocalizationIndex,
            baseModeTokens,
            ref nextItemNameStringId);
        var baseSelectorCount = convenienceCategories
            .Where(category => category.Kind == ConvenienceKind.Base)
            .Sum(category => category.Targets.Count);
        var materialSelectorCount = convenienceSelectorCount - baseSelectorCount;
        var charmSelectorCount = AddCharmSelectorsAndRecipes(
            misc,
            cubeMain,
            charmSelectorPlan,
            charmBonusTokens,
            reservedSelectorCodes,
            hdItems,
            itemNameStrings,
            ref nextItemNameStringId);
        var socketTokenCodes = AddSocketTokens(
            misc,
            reservedSelectorCodes,
            hdItems,
            itemNameStrings,
            ref nextItemNameStringId);
        var workbenchRecipeCount = AddWorkbenchRecipes(cubeMain, socketTokenCodes);
        var quickCraftRecipeCount = AddQuickCraftRecipes(cubeMain, craftTokenCode);
        var customItemCount = CountCustomCompactItems(misc);
        var controlTokenCount = baseModeTokens.Count + charmBonusTokens.Count +
                                socketTokenCodes.Count + 1;

        var excelDirectory = Path.Combine(outputDirectory, "data", "global", "excel");
        Directory.CreateDirectory(excelDirectory);
        var tables = new Dictionary<string, D2TsvTable>(StringComparer.Ordinal)
        {
            ["armor.txt"] = armor,
            ["weapons.txt"] = weapons,
            ["misc.txt"] = misc,
            ["gamble.txt"] = gamble,
            ["difficultylevels.txt"] = difficulties,
            ["uniqueitems.txt"] = uniqueItems,
            ["setitems.txt"] = setItems,
            ["cubemain.txt"] = cubeMain,
            ["magicprefix.txt"] = magicPrefixes,
            ["magicsuffix.txt"] = magicSuffixes,
            ["automagic.txt"] = autoMagic,
            ["qualityitems.txt"] = qualityItems,
            ["runes.txt"] = runewords,
            ["lvlprest.txt"] = levelPresets
        };
        foreach (var table in tables)
        {
            await table.Value.SaveAsync(
                Path.Combine(excelDirectory, table.Key),
                cancellationToken);
        }
        await WriteJsonAsync(
            Path.Combine(outputDirectory, "data", "hd", "items", "items.json"),
            hdItems,
            cancellationToken);
        await WriteJsonAsync(
            Path.Combine(outputDirectory, "data", "local", "lng", "strings", "item-names.json"),
            itemNameStrings,
            cancellationToken);
        await WriteJsonAsync(
            Path.Combine(outputDirectory, "data", "local", "lng", "strings", "ui.json"),
            uiStrings,
            cancellationToken);
        var catalog = new SupplyCatalog(
            PackageSchemaVersion,
            D2RClientLayout.SupportedGameVersion,
            SourceRevision,
            uniqueTargets.Count,
            setTargets.Count,
            selectorCount,
            baseSelectorCount,
            materialSelectorCount,
            charmSelectorCount,
            controlTokenCount,
            customItemCount,
            workbenchRecipeCount,
            quickCraftRecipeCount,
            GambleUniqueWeight,
            GambleSetWeight,
            GambleRareWeight,
            Categories
                .Select(category => new SupplyCatalogCategory(
                    category.Key,
                    category.DisplayName,
                    category.Vendor,
                    targets.Count(target => ReferenceEquals(target.Category, category)),
                    OrderTargetsForCategory(
                            targets.Where(target => ReferenceEquals(target.Category, category)),
                            category)
                        .FirstOrDefault()?.Name))
                .Where(category => category.ItemCount != 0)
                .Concat(convenienceCategories.Select(category => new SupplyCatalogCategory(
                    category.Key,
                    category.DisplayName,
                    category.Vendor,
                    category.Targets.Count,
                    category.Targets.FirstOrDefault()?.Name)))
                .Concat(charmSelectorPlan.Categories.Select(category => new SupplyCatalogCategory(
                    category.Key,
                    category.KoreanName,
                    SelectorVendor,
                    category.Targets.Count,
                    category.Targets.FirstOrDefault()?.KoreanName)))
                .ToArray());
        var catalogPath = Path.Combine(outputDirectory, "jm-supply-catalog.json");
        await WriteJsonAsync(catalogPath, catalog, cancellationToken);
        await WriteLootFilterAsync(
            Path.Combine(outputDirectory, "jm-loot-filter.json"),
            cancellationToken);
        await CopyStaticHdSelectorAssetsAsync(outputDirectory, cancellationToken);

        var outputFiles = Directory.EnumerateFiles(outputDirectory, "*", SearchOption.AllDirectories)
            .Where(path => !path.EndsWith("package-manifest.json", StringComparison.OrdinalIgnoreCase))
            .Order(StringComparer.Ordinal)
            .ToArray();
        var packageFiles = outputFiles
            .Select(path => new SupplyPackageFile(
                Path.GetRelativePath(outputDirectory, path).Replace('\\', '/'),
                Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)))))
            .ToArray();
        var manifest = new SupplyPackageManifest(
            PackageSchemaVersion,
            D2RClientLayout.SupportedGameVersion,
            SourceRevision,
            uniqueTargets.Count,
            setTargets.Count,
            selectorCount,
            baseSelectorCount,
            materialSelectorCount,
            charmSelectorCount,
            controlTokenCount,
            customItemCount,
            workbenchRecipeCount,
            quickCraftRecipeCount,
            packageFiles);
        await WriteJsonAsync(
            Path.Combine(outputDirectory, "package-manifest.json"),
            manifest,
            cancellationToken);

        return new SupplyBuildResult(
            uniqueTargets.Count,
            setTargets.Count,
            selectorCount,
            baseSelectorCount,
            materialSelectorCount,
            charmSelectorCount,
            controlTokenCount,
            customItemCount,
            workbenchRecipeCount,
            quickCraftRecipeCount,
            packageFiles.Length,
            outputDirectory);
    }

    private static void ValidateSourceFiles(string sourceDirectory)
    {
        foreach (var expected in ExpectedSourceHashes)
        {
            var path = Path.Combine(sourceDirectory, expected.Key);
            if (!File.Exists(path))
            {
                throw new FileNotFoundException($"Pinned D2R source table '{expected.Key}' is missing.", path);
            }

            var actual = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
            var matchesNormalized = NormalizedSourceHashes.TryGetValue(
                                        expected.Key,
                                        out var normalizedHash) &&
                                    string.Equals(
                                        actual,
                                        normalizedHash,
                                        StringComparison.OrdinalIgnoreCase);
            var matchesGenerated = GeneratedSourceHashes.TryGetValue(
                                       expected.Key,
                                       out var generatedHash) &&
                                   string.Equals(
                                       actual,
                                       generatedHash,
                                       StringComparison.OrdinalIgnoreCase);
            if (!string.Equals(actual, expected.Value, StringComparison.OrdinalIgnoreCase) &&
                !matchesNormalized &&
                !matchesGenerated)
            {
                throw new InvalidDataException(
                    $"D2R source table '{expected.Key}' does not match pinned revision {SourceRevision}.");
            }
        }
    }

    private static void RemovePreviouslyGeneratedJsonRecords(
        JsonArray hdItems,
        JsonArray itemNameStrings)
    {
        var generatedSelectorCodes = itemNameStrings
            .Where(node => node?["id"]?.GetValue<int>() >= FirstCustomStringId)
            .Select(node => node?["Key"]?.GetValue<string>())
            .Where(key => key is not null &&
                          key.StartsWith("jm_selector_", StringComparison.Ordinal))
            .Select(key => key!["jm_selector_".Length..])
            .Where(code => code.Length == 3)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var firstSelectorMapping = -1;
        for (var index = 0; index < hdItems.Count; index++)
        {
            if (hdItems[index] is JsonObject mapping &&
                mapping.Any(entry => generatedSelectorCodes.Contains(entry.Key)))
            {
                firstSelectorMapping = index;
                break;
            }
        }

        if (firstSelectorMapping >= 0)
        {
            while (hdItems.Count > firstSelectorMapping)
            {
                hdItems.RemoveAt(hdItems.Count - 1);
            }
        }

        for (var index = itemNameStrings.Count - 1; index >= 0; index--)
        {
            if (itemNameStrings[index]?["id"]?.GetValue<int>() >= FirstCustomStringId)
            {
                itemNameStrings.RemoveAt(index);
            }
        }
    }

    private static Dictionary<string, BaseItem> CreateBaseItemIndex(
        D2TsvTable armor,
        D2TsvTable weapons,
        D2TsvTable misc)
    {
        var result = new Dictionary<string, BaseItem>(StringComparer.OrdinalIgnoreCase);
        AddBaseItems(armor, BaseItemKind.Armor, result);
        AddBaseItems(weapons, BaseItemKind.Weapon, result);
        AddBaseItems(misc, BaseItemKind.Misc, result);
        return result;
    }

    private static void AddBaseItems(
        D2TsvTable table,
        BaseItemKind kind,
        IDictionary<string, BaseItem> result)
    {
        foreach (var row in table.Rows)
        {
            var code = table.Get(row, "code").Trim();
            if (code.Length == 0)
            {
                continue;
            }

            var itemType = table.Get(row, "type");
            var supportsEthereal = kind is BaseItemKind.Armor or BaseItemKind.Weapon &&
                                    itemType is not "bow" and not "xbow" and not "abow" &&
                                    int.TryParse(table.Get(row, "durability"), out var durability) &&
                                    durability > 0;
            result[code] = new BaseItem(
                code,
                table.Get(row, "name"),
                table.Get(row, "namestr"),
                itemType,
                kind,
                !string.IsNullOrWhiteSpace(table.Get(row, "quest")),
                table.Get(row, "spawnable") == "1",
                supportsEthereal,
                table.Get(row, "invwidth"),
                table.Get(row, "invheight"),
                table.Get(row, "invfile"),
                table.Get(row, "flippyfile"));
        }
    }

    private static List<SupplyTarget> CreateUniqueTargets(
        D2TsvTable table,
        IReadOnlyDictionary<string, BaseItem> baseItems)
    {
        var targets = new List<SupplyTarget>();
        var seenNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var row in table.Rows)
        {
            var name = table.Get(row, "index").Trim();
            var code = table.Get(row, "code").Trim();
            if (name.Length == 0 || code.Length == 0 ||
                table.Get(row, "disabled") == "1" ||
                table.Get(row, "spawnable") == "0" ||
                name.StartsWith("Crafted ", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (!seenNames.Add(name))
            {
                throw new InvalidDataException(
                    $"Catalog unique name '{name}' is duplicated and has no variant identity.");
            }

            if (!baseItems.TryGetValue(code, out var baseItem))
            {
                throw new InvalidDataException($"Catalog item '{name}' refers to unknown base code '{code}'.");
            }

            if (!baseItem.IsQuestItem)
            {
                targets.Add(CreateTarget(name, code, SupplyQuality.Unique, baseItems));
            }
        }

        return targets;
    }

    private static void DisambiguateRainbowFacetVariants(
        D2TsvTable uniqueItems,
        JsonArray itemNameStrings,
        ref int nextItemNameStringId)
    {
        const string originalName = "Rainbow Facet";
        var sourceLocalization = itemNameStrings
            .OfType<JsonObject>()
            .Single(entry => string.Equals(
                entry["Key"]?.GetValue<string>(),
                originalName,
                StringComparison.Ordinal));
        var variants = uniqueItems.Rows
            .Where(row => uniqueItems.Get(row, "index") == originalName &&
                          uniqueItems.Get(row, "spawnable") != "0" &&
                          uniqueItems.Get(row, "disabled") != "1")
            .ToArray();
        if (variants.Length != 8)
        {
            throw new InvalidDataException(
                $"Expected 8 {originalName} variants, found {variants.Length}.");
        }

        var identities = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var row in variants)
        {
            var element = uniqueItems.Get(row, "prop1") switch
            {
                "dmg-ltng" => (English: "Lightning", Korean: "번개"),
                "dmg-cold" => (English: "Cold", Korean: "냉기"),
                "dmg-fire" => (English: "Fire", Korean: "화염"),
                "dmg-pois" => (English: "Poison", Korean: "독"),
                var property => throw new InvalidDataException(
                    $"Unknown {originalName} element property '{property}'.")
            };
            var trigger = uniqueItems.Get(row, "prop4") switch
            {
                "death-skill" => (Key: "Death", English: "Death", Korean: "사망 시"),
                "levelup-skill" => (Key: "LevelUp", English: "Level Up", Korean: "레벨 상승 시"),
                var property => throw new InvalidDataException(
                    $"Unknown {originalName} trigger property '{property}'.")
            };
            var identity = $"{element.English}-{trigger.Key}";
            if (!identities.Add(identity))
            {
                throw new InvalidDataException(
                    $"Duplicate {originalName} variant identity '{identity}'.");
            }

            var variantKey = $"JM Rainbow Facet {element.English} {trigger.Key}";
            uniqueItems.Set(row, "index", variantKey);
            var localization = new JsonObject
            {
                ["id"] = nextItemNameStringId++,
                ["Key"] = variantKey
            };
            foreach (var locale in ItemNameLocales)
            {
                var localizedBaseName = sourceLocalization[locale]?.GetValue<string>()
                                        ?? sourceLocalization["enUS"]!.GetValue<string>();
                localization[locale] = locale == "koKR"
                    ? $"{localizedBaseName} ({element.Korean}·{trigger.Korean})"
                    : $"{localizedBaseName} ({element.English} / {trigger.English})";
            }

            itemNameStrings.Add(localization);
        }
    }

    private static List<SupplyTarget> CreateSetTargets(
        D2TsvTable table,
        IReadOnlyDictionary<string, BaseItem> baseItems)
    {
        var targets = new List<SupplyTarget>();
        foreach (var row in table.Rows)
        {
            var name = table.Get(row, "index").Trim();
            var code = table.Get(row, "item").Trim();
            if (name.Length == 0 || code.Length == 0 ||
                table.Get(row, "disabled") == "1" ||
                table.Get(row, "spawnable") == "0")
            {
                continue;
            }

            if (!baseItems.TryGetValue(code, out var baseItem))
            {
                throw new InvalidDataException($"Catalog item '{name}' refers to unknown base code '{code}'.");
            }

            if (!baseItem.IsQuestItem)
            {
                targets.Add(CreateTarget(name, code, SupplyQuality.Set, baseItems));
            }
        }

        return targets;
    }

    private static SupplyTarget CreateTarget(
        string name,
        string code,
        SupplyQuality quality,
        IReadOnlyDictionary<string, BaseItem> baseItems)
    {
        if (!baseItems.TryGetValue(code, out var baseItem))
        {
            throw new InvalidDataException($"Catalog item '{name}' refers to unknown base code '{code}'.");
        }

        var category = FindCategory(quality, baseItem);
        return new SupplyTarget(name, code, baseItem, quality, category);
    }

    private static SupplyCategory FindCategory(SupplyQuality quality, BaseItem item)
    {
        var candidates = Categories.Where(category => category.Quality == quality).ToArray();
        var match = candidates.FirstOrDefault(category => category.ItemTypes.Contains(
            item.Type,
            StringComparer.OrdinalIgnoreCase));
        if (match is not null)
        {
            return match;
        }

        if (item.Kind == BaseItemKind.Weapon)
        {
            return candidates.Single(category => category.Key.EndsWith("other", StringComparison.Ordinal));
        }

        return candidates.Single(category => category.Key.EndsWith("other", StringComparison.Ordinal));
    }

    private static void ApplyCatalogAvailability(
        D2TsvTable uniqueItems,
        D2TsvTable setItems,
        IReadOnlyCollection<SupplyTarget> uniqueTargets,
        IReadOnlyCollection<SupplyTarget> setTargets)
    {
        var uniqueNames = uniqueTargets.Select(target => target.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var row in uniqueItems.Rows.Where(row =>
                     uniqueNames.Contains(uniqueItems.Get(row, "index"))))
        {
            var uniqueName = uniqueItems.Get(row, "index");
            if (OriginalSingleCarryUniqueCharms.Contains(uniqueName))
            {
                uniqueItems.Set(row, "nolimit", string.Empty);
                // D2R 3.2 uses a distinct carry-group id for each original single-carry
                // charm (Gheed=1001, Annihilus=1002, Torch=1003). Collapsing these to the
                // old boolean value 1 incorrectly makes the three different charms
                // mutually exclusive.
                if (!int.TryParse(uniqueItems.Get(row, "carry1"), out var carryGroup) ||
                    carryGroup <= 0)
                {
                    throw new InvalidDataException(
                        $"Single-carry unique '{uniqueName}' has no valid carry-group id.");
                }
            }
            else
            {
                uniqueItems.Set(row, "nolimit", "1");
                uniqueItems.Set(row, "carry1", "0");
            }

            EnableEveryGameMode(uniqueItems, row);
            ClampLevel(uniqueItems, row);
        }

        var setNames = setTargets.Select(target => target.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var row in setItems.Rows.Where(row =>
                     setNames.Contains(setItems.Get(row, "index"))))
        {
            EnableEveryGameMode(setItems, row);
            ClampLevel(setItems, row);
        }
    }

    private static void ApplyVendorPriceReduction(D2TsvTable uniqueItems)
    {
        var gheedsFortune = uniqueItems.Rows.Single(row =>
            uniqueItems.Get(row, "index").Equals("Gheed's Fortune", StringComparison.Ordinal));
        for (var propertyIndex = 1; propertyIndex <= 12; propertyIndex++)
        {
            if (!uniqueItems.Get(gheedsFortune, $"prop{propertyIndex}")
                    .Equals("cheap", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            uniqueItems.Set(
                gheedsFortune,
                $"min{propertyIndex}",
                VendorPriceReductionPercent.ToString());
            uniqueItems.Set(
                gheedsFortune,
                $"max{propertyIndex}",
                VendorPriceReductionPercent.ToString());
            return;
        }

        throw new InvalidDataException("Gheed's Fortune has no vendor-price property to adjust.");
    }

    private static IReadOnlyDictionary<string, int> CreatePropertyFunctionIndex(
        D2TsvTable properties)
    {
        var result = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var row in properties.Rows)
        {
            var code = properties.Get(row, "code").Trim();
            if (code.Length == 0)
            {
                continue;
            }

            if (!int.TryParse(properties.Get(row, "func1"), out var function))
            {
                continue;
            }

            if (!result.TryAdd(code, function))
            {
                throw new InvalidDataException($"D2 property '{code}' is duplicated.");
            }
        }

        return result;
    }

    private static void ApplyPreferredVariableRolls(
        D2TsvTable table,
        IReadOnlyCollection<SupplyTarget> targets,
        int propertySlotCount,
        IReadOnlyDictionary<string, int> propertyFunctions)
    {
        var targetNames = targets.Select(target => target.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var row in table.Rows.Where(row =>
                     targetNames.Contains(table.Get(row, "index"))))
        {
            for (var propertyIndex = 1; propertyIndex <= propertySlotCount; propertyIndex++)
            {
                var propertyCode = table.Get(row, $"prop{propertyIndex}").Trim();
                if (propertyCode.Length == 0 ||
                    !propertyFunctions.TryGetValue(propertyCode, out var function) ||
                    !ShouldApplyPreferredRoll(propertyCode, function) ||
                    !int.TryParse(table.Get(row, $"min{propertyIndex}"), out var minimum) ||
                    !int.TryParse(table.Get(row, $"max{propertyIndex}"), out var maximum) ||
                    minimum >= maximum)
                {
                    continue;
                }

                table.Set(
                    row,
                    $"min{propertyIndex}",
                    CalculatePreferredMinimum(minimum, maximum).ToString());
            }
        }
    }

    internal static int CalculatePreferredMinimum(int minimum, int maximum)
        => CalculatePreferredMinimum(minimum, maximum, PreferredRollPercentile);

    internal static int CalculatePreferredAffixMinimum(int minimum, int maximum)
        => CalculatePreferredMinimum(minimum, maximum, PreferredAffixRollPercentile);

    internal static bool ShouldApplyPreferredRoll(string propertyCode, int function) =>
        PreferredNumericPropertyFunctions.Contains(function) &&
        !NonPowerRollPropertyCodes.Contains(propertyCode);

    private static int CalculatePreferredMinimum(int minimum, int maximum, int percentile)
    {
        if (minimum >= maximum)
        {
            return minimum;
        }

        var spread = (long)maximum - minimum;
        var preferredOffset =
            (spread * percentile + 99) / 100;
        return checked(minimum + (int)preferredOffset);
    }

    private static void ApplyPreferredAffixWeights(D2TsvTable table)
    {
        foreach (var row in table.Rows)
        {
            if (table.Get(row, "spawnable") != "1" ||
                !int.TryParse(table.Get(row, "frequency"), out var frequency) ||
                frequency <= 0 ||
                !int.TryParse(table.Get(row, "level"), out var level))
            {
                continue;
            }

            table.Set(
                row,
                "frequency",
                checked(frequency * CalculateAffixWeightMultiplier(level)).ToString());
        }
    }

    private static void ApplyPreferredCharmAffixWeights(
        D2TsvTable suffixes,
        IReadOnlySet<int> preferredSuffixIds)
    {
        foreach (var suffixId in preferredSuffixIds)
        {
            if (suffixId < 0 || suffixId >= suffixes.Rows.Count)
            {
                throw new InvalidDataException($"Preferred charm suffix id {suffixId} is out of range.");
            }

            var row = suffixes.Rows[suffixId];
            if (suffixes.Get(row, "spawnable") != "1" ||
                !int.TryParse(suffixes.Get(row, "frequency"), out var frequency) ||
                frequency <= 0)
            {
                throw new InvalidDataException(
                    $"Preferred charm suffix id {suffixId} cannot be weighted.");
            }

            suffixes.Set(
                row,
                "frequency",
                checked(frequency * PreferredCharmAffixWeightMultiplier).ToString());
        }
    }

    private static void ApplyPreferredAffixVariableRolls(
        D2TsvTable table,
        IReadOnlyDictionary<string, int> propertyFunctions)
    {
        foreach (var row in table.Rows.Where(row => table.Get(row, "spawnable") == "1"))
        {
            for (var propertyIndex = 1; propertyIndex <= 3; propertyIndex++)
            {
                RaisePreferredPropertyMinimum(
                    table,
                    row,
                    $"mod{propertyIndex}code",
                    $"mod{propertyIndex}min",
                    $"mod{propertyIndex}max",
                    propertyFunctions,
                    PreferredAffixRollPercentile);
            }
        }
    }

    private static void ApplyPreferredSuperiorRolls(D2TsvTable qualityItems)
    {
        var supportedProperties = new HashSet<string>(StringComparer.Ordinal)
        {
            "att", "dmg%", "ac%", "dur%"
        };
        foreach (var row in qualityItems.Rows)
        {
            for (var propertyIndex = 1; propertyIndex <= 2; propertyIndex++)
            {
                var property = qualityItems.Get(row, $"mod{propertyIndex}code");
                if (property.Length == 0)
                {
                    continue;
                }

                if (!supportedProperties.Contains(property) ||
                    !int.TryParse(qualityItems.Get(row, $"mod{propertyIndex}min"), out var minimum) ||
                    !int.TryParse(qualityItems.Get(row, $"mod{propertyIndex}max"), out var maximum))
                {
                    throw new InvalidDataException(
                        $"Unsupported superior property range '{property}'.");
                }

                qualityItems.Set(
                    row,
                    $"mod{propertyIndex}min",
                    CalculatePreferredMinimum(
                        minimum,
                        maximum,
                        PreferredAffixRollPercentile).ToString());
            }
        }

        // QualityItems has no frequency column. Duplicate only existing legal rows to
        // weight useful two-property outcomes without inventing a modifier combination
        // or exceeding the vanilla maximum of two superior properties.
        DuplicateSuperiorCombination(qualityItems, "ac%", "dur%", targetWeight: 8);
        DuplicateSuperiorCombination(qualityItems, "att", "dmg%", targetWeight: 5);
        DuplicateSuperiorCombination(qualityItems, "att", "dur%", targetWeight: 2);
        DuplicateSuperiorCombination(qualityItems, "dmg%", "dur%", targetWeight: 10);
    }

    private static void DuplicateSuperiorCombination(
        D2TsvTable qualityItems,
        string firstProperty,
        string secondProperty,
        int targetWeight)
    {
        var source = qualityItems.Rows.Single(row =>
            qualityItems.Get(row, "mod1code") == firstProperty &&
            qualityItems.Get(row, "mod2code") == secondProperty);
        for (var weight = 1; weight < targetWeight; weight++)
        {
            qualityItems.Rows.Add(qualityItems.CloneRow(source));
        }
    }

    private static void ApplyPreferredRunewordVariableRolls(
        D2TsvTable runewords,
        IReadOnlyDictionary<string, int> propertyFunctions)
    {
        foreach (var row in runewords.Rows.Where(row => runewords.Get(row, "complete") == "1"))
        {
            for (var propertyIndex = 1; propertyIndex <= 7; propertyIndex++)
            {
                RaisePreferredPropertyMinimum(
                    runewords,
                    row,
                    $"T1Code{propertyIndex}",
                    $"T1Min{propertyIndex}",
                    $"T1Max{propertyIndex}",
                    propertyFunctions);
            }
        }
    }

    private static void ApplyPreferredCubeVariableRolls(
        D2TsvTable cubeMain,
        IReadOnlyDictionary<string, int> propertyFunctions)
    {
        foreach (var row in cubeMain.Rows.Where(row => cubeMain.Get(row, "enabled") == "1"))
        {
            for (var propertyIndex = 1; propertyIndex <= 5; propertyIndex++)
            {
                RaisePreferredPropertyMinimum(
                    cubeMain,
                    row,
                    $"mod {propertyIndex}",
                    $"mod {propertyIndex} min",
                    $"mod {propertyIndex} max",
                    propertyFunctions);
            }
        }
    }

    private static void RaisePreferredPropertyMinimum(
        D2TsvTable table,
        string[] row,
        string propertyColumn,
        string minimumColumn,
        string maximumColumn,
        IReadOnlyDictionary<string, int> propertyFunctions,
        int percentile = PreferredRollPercentile)
    {
        var propertyCode = table.Get(row, propertyColumn).Trim();
        if (propertyCode.Length == 0 ||
            !propertyFunctions.TryGetValue(propertyCode, out var function) ||
            !ShouldApplyPreferredRoll(propertyCode, function) ||
            !int.TryParse(table.Get(row, minimumColumn), out var minimum) ||
            !int.TryParse(table.Get(row, maximumColumn), out var maximum) ||
            minimum >= maximum)
        {
            return;
        }

        table.Set(
            row,
            minimumColumn,
            CalculatePreferredMinimum(minimum, maximum, percentile).ToString());
    }

    private static void ApplyPreferredBaseDefenseRolls(D2TsvTable armor)
    {
        foreach (var row in armor.Rows.Where(row => armor.Get(row, "spawnable") == "1"))
        {
            if (!int.TryParse(armor.Get(row, "minac"), out var minimum) ||
                !int.TryParse(armor.Get(row, "maxac"), out var maximum) ||
                minimum >= maximum)
            {
                continue;
            }

            armor.Set(row, "minac", CalculatePreferredMinimum(minimum, maximum).ToString());
        }
    }

    private static void ApplyFullAutomapReveal(D2TsvTable levelPresets)
    {
        var presetCount = 0;
        foreach (var row in levelPresets.Rows)
        {
            if (!int.TryParse(levelPresets.Get(row, "Def"), out _))
            {
                continue;
            }

            levelPresets.Set(row, "AutoMap", "1");
            presetCount++;
        }

        if (presetCount != FullAutomapPresetCount)
        {
            throw new InvalidDataException(
                $"Expected {FullAutomapPresetCount} level presets, found {presetCount}.");
        }
    }

    internal static int CalculateAffixWeightMultiplier(int affixLevel) => affixLevel switch
    {
        < 20 => 1,
        < 40 => 3,
        < 60 => 6,
        < 80 => 10,
        _ => MaximumAffixWeightMultiplier
    };

    private static void EnableEveryGameMode(D2TsvTable table, string[] row)
    {
        table.Set(row, "disableChronicle", string.Empty);
        table.Set(row, "DropConditionCalc", string.Empty);
        table.Set(row, "firstLadderSeason", string.Empty);
        table.Set(row, "lastLadderSeason", string.Empty);
    }

    private static void ClampLevel(D2TsvTable table, string[] row)
    {
        if (int.TryParse(table.Get(row, "lvl"), out var level) && level > MaximumCatalogLevel)
        {
            table.Set(row, "lvl", MaximumCatalogLevel.ToString());
        }
    }

    private static void ApplyGamblingTables(
        D2TsvTable armor,
        D2TsvTable weapons,
        D2TsvTable misc,
        D2TsvTable gamble,
        D2TsvTable difficulties,
        IReadOnlyCollection<SupplyTarget> targets)
    {
        foreach (var row in difficulties.Rows)
        {
            difficulties.Set(row, "GambleUnique", GambleUniqueWeight.ToString());
            difficulties.Set(row, "GambleSet", GambleSetWeight.ToString());
            difficulties.Set(row, "GambleRare", GambleRareWeight.ToString());
            difficulties.Set(row, "GambleUber", "10000");
            difficulties.Set(row, "GambleUltra", "10000");
        }

        var gambleTargets = targets
            .Where(target => !NonGambleItemTypes.Contains(target.BaseItem.Type))
            .ToArray();
        var targetCodes = gambleTargets.Select(target => target.Code)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        ExpandGambleItemFamilies(armor, targetCodes);
        ExpandGambleItemFamilies(weapons, targetCodes);
        foreach (var table in new[] { armor, weapons, misc })
        {
            foreach (var row in table.Rows)
            {
                var code = table.Get(row, "code").Trim();
                if (!targetCodes.Contains(code))
                {
                    continue;
                }

                table.Set(row, "gamble cost", GambleCost.ToString());
                table.Set(row, "cost", GambleCost.ToString());
                if (int.TryParse(table.Get(row, "level"), out var level) && level > MaximumCatalogLevel)
                {
                    table.Set(row, "level", MaximumCatalogLevel.ToString());
                }
            }
        }

        var existingCodes = gamble.Rows
            .Select(row => gamble.Get(row, "code").Trim())
            .Where(code => code.Length != 0)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var target in gambleTargets
                     .GroupBy(target => target.Code, StringComparer.OrdinalIgnoreCase)
                     .Select(group => group.First())
                     .OrderBy(target => target.BaseItem.LevelSortKey)
                     .ThenBy(target => target.BaseItem.Name, StringComparer.OrdinalIgnoreCase))
        {
            if (!existingCodes.Add(target.Code))
            {
                continue;
            }

            var row = gamble.NewRow();
            gamble.Set(row, "name", target.BaseItem.Name);
            gamble.Set(row, "code", target.Code);
            gamble.Rows.Add(row);
        }
    }

    private static void ExpandGambleItemFamilies(
        D2TsvTable table,
        ISet<string> targetCodes)
    {
        var addedCode = true;
        while (addedCode)
        {
            addedCode = false;
            foreach (var row in table.Rows)
            {
                var familyCodes = new[]
                    {
                        table.Get(row, "code"),
                        table.Get(row, "normcode"),
                        table.Get(row, "ubercode"),
                        table.Get(row, "ultracode")
                    }
                    .Select(code => code.Trim())
                    .Where(IsRealItemCode)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray();
                if (!familyCodes.Any(targetCodes.Contains))
                {
                    continue;
                }

                foreach (var familyCode in familyCodes)
                {
                    addedCode |= targetCodes.Add(familyCode);
                }
            }
        }
    }

    private static bool IsRealItemCode(string code) =>
        code.Length is 3 or 4 &&
        !string.Equals(code, "xxx", StringComparison.OrdinalIgnoreCase);

    private static void ApplyConvenienceItemData(
        D2TsvTable armor,
        D2TsvTable weapons,
        D2TsvTable misc)
    {
        foreach (var row in armor.Rows)
        {
            armor.Set(row, "ShowLevel", "1");
        }

        foreach (var row in weapons.Rows.Where(row =>
                     !string.Equals(weapons.Get(row, "type"), "tpot", StringComparison.OrdinalIgnoreCase)))
        {
            weapons.Set(row, "ShowLevel", "1");
        }

        var informationCodes = new HashSet<string>(
            ["amu", "rin", "cm1", "cm2", "cm3", "jew"],
            StringComparer.OrdinalIgnoreCase);
        foreach (var row in misc.Rows.Where(row => informationCodes.Contains(misc.Get(row, "code"))))
        {
            misc.Set(row, "ShowLevel", "1");
        }

        SetStackSize(misc, "tbk", TomeStackSize, 20);
        SetStackSize(misc, "ibk", TomeStackSize, 20);
        SetStackSize(misc, "key", KeyStackSize, 20);
        SetStackSize(misc, "aqv", ProjectileStackSize, ProjectileStackSize);
        SetStackSize(misc, "cqv", ProjectileStackSize, ProjectileStackSize);
    }

    private static void SetStackSize(
        D2TsvTable misc,
        string code,
        int maximum,
        int spawnQuantity)
    {
        var row = misc.Rows.Single(item => misc.Get(item, "code") == code);
        misc.Set(row, "stackable", "1");
        misc.Set(row, "minstack", "1");
        misc.Set(row, "maxstack", maximum.ToString());
        misc.Set(row, "spawnstack", Math.Min(maximum, spawnQuantity).ToString());
    }

    private static IReadOnlyList<ConvenienceCategory> CreateConvenienceCategories(
        IReadOnlyDictionary<string, BaseItem> baseItems,
        D2TsvTable misc)
    {
        var baseWeapons = baseItems.Values
            .Where(item => item.Kind == BaseItemKind.Weapon && item.IsSpawnable &&
                           !item.IsQuestItem &&
                           !string.Equals(item.Type, "tpot", StringComparison.OrdinalIgnoreCase))
            .OrderBy(item => item.LevelSortKey)
            .ThenBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
            .Select((item, index) => CreateBaseConvenienceTarget(item, index))
            .ToArray();
        var baseArmor = baseItems.Values
            .Where(item => item.Kind == BaseItemKind.Armor && item.IsSpawnable && !item.IsQuestItem)
            .OrderBy(item => item.LevelSortKey)
            .ThenBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
            .Select((item, index) =>
                CreateBaseConvenienceTarget(item, baseWeapons.Length + index))
            .ToArray();

        var materialRows = misc.Rows
            .Where(row => !misc.Get(row, "name").StartsWith("JM ", StringComparison.Ordinal))
            .ToArray();
        var runes = materialRows
            .Where(row => IsRuneCode(misc.Get(row, "code")))
            .OrderBy(row => misc.Get(row, "code"), StringComparer.OrdinalIgnoreCase)
            .Select((row, index) => CreateConvenienceTarget(misc, row, index))
            .ToArray();
        var gems = materialRows
            .Where(row => misc.Get(row, "type").StartsWith("gem", StringComparison.OrdinalIgnoreCase))
            .OrderBy(row => misc.Get(row, "code"), StringComparer.OrdinalIgnoreCase)
            .Select((row, index) => CreateConvenienceTarget(misc, row, runes.Length + index))
            .ToArray();
        var charmAndJewelCodes = new HashSet<string>(
            ["cm1", "cm2", "jew"],
            StringComparer.OrdinalIgnoreCase);
        var charmsAndJewels = materialRows
            .Where(row => charmAndJewelCodes.Contains(misc.Get(row, "code")))
            .OrderBy(row => misc.Get(row, "name"), StringComparer.OrdinalIgnoreCase)
            .Select((row, index) =>
                CreateConvenienceTarget(misc, row, runes.Length + gems.Length + index))
            .ToArray();

        ConvenienceCategory[] categories =
        [
            CreateBaseCategory("base-sword", "베이스 검", SelectorVendor, baseWeapons, ["swor"]),
            CreateBaseCategory("base-axe", "베이스 도끼", SelectorVendor, baseWeapons, ["axe", "taxe"]),
            CreateBaseCategory(
                "base-blunt",
                "베이스 둔기·홀",
                SelectorVendor,
                baseWeapons,
                ["club", "mace", "hamm", "scep"]),
            CreateBaseCategory(
                "base-bow",
                "베이스 활·석궁",
                SelectorVendor,
                baseWeapons,
                ["bow", "xbow"]),
            CreateBaseCategory(
                "base-pole",
                "베이스 창·투창",
                SelectorVendor,
                baseWeapons,
                ["pole", "spea", "jave"]),
            CreateBaseCategory(
                "base-dagger",
                "베이스 단검·투척",
                SelectorVendor,
                baseWeapons,
                ["knif", "tkni"]),
            CreateBaseCategory(
                "base-caster",
                "베이스 지팡이·완드·오브",
                SelectorVendor,
                baseWeapons,
                ["staf", "wand", "orb"]),
            CreateBaseCategory(
                "base-class-weapon",
                "베이스 직업 전용 무기",
                SelectorVendor,
                baseWeapons,
                ["h2h", "h2h2", "abow", "aspe", "ajav"]),
            CreateBaseCategory("base-body", "베이스 갑옷", SelectorVendor, baseArmor, ["tors"]),
            CreateBaseCategory(
                "base-helm",
                "베이스 투구·서클릿",
                SelectorVendor,
                baseArmor,
                ["helm", "circ"]),
            CreateBaseCategory("base-shield", "베이스 방패", SelectorVendor, baseArmor, ["shie"]),
            CreateBaseCategory("base-gloves", "베이스 장갑", SelectorVendor, baseArmor, ["glov"]),
            CreateBaseCategory("base-boots", "베이스 신발", SelectorVendor, baseArmor, ["boot"]),
            CreateBaseCategory("base-belts", "베이스 벨트", SelectorVendor, baseArmor, ["belt"]),
            CreateBaseCategory(
                "base-class-armor",
                "베이스 직업 전용 방어구",
                SelectorVendor,
                baseArmor,
                ["phlm", "pelt", "head", "ashd", "grim"]),
            new("materials-runes", "룬 재료", SelectorVendor, ConvenienceKind.Material, runes),
            new("materials-gems", "보석 재료", SelectorVendor, ConvenienceKind.Material, gems),
            new("materials-charms", "참과 주얼", SelectorVendor, ConvenienceKind.Material, charmsAndJewels)
        ];

        var assignedBaseTargets = categories
            .Where(category => category.Kind == ConvenienceKind.Base)
            .SelectMany(category => category.Targets)
            .ToArray();
        var expectedBaseTargetCount = baseWeapons.Length + baseArmor.Length;
        if (assignedBaseTargets.Length != expectedBaseTargetCount ||
            assignedBaseTargets.Select(target => target.Code)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Count() != expectedBaseTargetCount)
        {
            throw new InvalidDataException(
                "The base selector categories do not form an exact item partition.");
        }

        return categories;
    }

    private static ConvenienceTarget CreateBaseConvenienceTarget(
        BaseItem item,
        int selectorOrder) =>
        new(
            item.Name,
            item.NameStringKey.Length == 0 ? item.Code : item.NameStringKey,
            item.Code,
            item.Type,
            selectorOrder,
            item.SupportsEthereal,
            item.InventoryWidth,
            item.InventoryHeight,
            item.InventoryFile,
            item.FlippyFile);

    private static ConvenienceCategory CreateBaseCategory(
        string key,
        string displayName,
        string vendor,
        IReadOnlyList<ConvenienceTarget> source,
        IReadOnlyCollection<string> itemTypes) =>
        new(
            key,
            displayName,
            vendor,
            ConvenienceKind.Base,
            source.Where(target => itemTypes.Contains(
                    target.ItemType,
                    StringComparer.OrdinalIgnoreCase))
                .ToArray());

    private static ConvenienceTarget CreateConvenienceTarget(
        D2TsvTable misc,
        string[] row,
        int selectorOrder)
    {
        var code = misc.Get(row, "code");
        var nameStringKey = misc.Get(row, "namestr");
        return new ConvenienceTarget(
            misc.Get(row, "name"),
            nameStringKey.Length == 0 ? code : nameStringKey,
            code,
            misc.Get(row, "type"),
            selectorOrder,
            false,
            misc.Get(row, "invwidth"),
            misc.Get(row, "invheight"),
            misc.Get(row, "invfile"),
            misc.Get(row, "flippyfile"));
    }

    private static bool IsRuneCode(string code) =>
        code.Length == 3 && code[0] == 'r' &&
        char.IsAsciiDigit(code[1]) && char.IsAsciiDigit(code[2]);

    private static int AddSelectorsAndRecipes(
        D2TsvTable misc,
        D2TsvTable cubeMain,
        IReadOnlyCollection<SupplyTarget> targets,
        ISet<string> reservedSelectorCodes,
        JsonArray hdItems,
        JsonArray itemNameStrings,
        IReadOnlyDictionary<string, JsonObject> itemNameLocalizationIndex,
        ref int nextItemNameStringId)
    {
        var template = misc.Rows.Single(row => misc.Get(row, "code") == "isc");
        var selectorIndex = 0;

        foreach (var category in Categories)
        {
            var categoryTargets = OrderTargetsForCategory(
                    targets.Where(target => ReferenceEquals(target.Category, category)),
                    category)
                .ToArray();
            if (categoryTargets.Length == 0)
            {
                continue;
            }

            var selectors = categoryTargets
                .Select(target => new Selector(
                    target,
                    AllocateSelectorCode(
                        target.Quality == SupplyQuality.Unique ? '5' : '1',
                        ref selectorIndex,
                        reservedSelectorCodes)))
                .ToArray();
            foreach (var selector in selectors)
            {
                var row = misc.CloneRow(template);
                ClearVendorColumns(misc, row);
                misc.Set(row, "name", $"JM supply selector {selector.Code}");
                misc.Set(row, "version", "100");
                misc.Set(row, "level", "1");
                misc.Set(row, "ShowLevel", "0");
                misc.Set(row, "levelreq", "1");
                misc.Set(row, "rarity", "1");
                misc.Set(row, "cost", "1");
                misc.Set(row, "gamble cost", "0");
                misc.Set(row, "code", selector.Code);
                var nameStringKey = CreateSelectorNameStringKey(selector.Code);
                misc.Set(row, "namestr", nameStringKey);
                misc.Set(row, "type", "torc");
                misc.Set(row, "type2", string.Empty);
                misc.Set(row, "flippyfile", selector.Target.Quality == SupplyQuality.Unique ? "flpgsy" : "flpgsg");
                misc.Set(row, "invfile", selector.Target.Quality == SupplyQuality.Unique ? "invgsye" : "invgsge");
                misc.Set(row, "useable", "0");
                misc.Set(row, "pSpell", "-1");
                misc.Set(row, "PermStoreItem", "1");
                misc.Set(row, "multibuy", "0");
                if (ReferenceEquals(selector, selectors[0]))
                {
                    misc.Set(row, category.Vendor + "Min", "1");
                    misc.Set(row, category.Vendor + "Max", "1");
                }

                misc.Rows.Add(row);
                AddHdItemMapping(
                    hdItems,
                    selector.Code,
                    ResolveCategorySelectorAsset(category.Key));
                AddItemNameString(
                    itemNameStrings,
                    itemNameLocalizationIndex,
                    nameStringKey,
                    selector.Target.Quality == SupplyQuality.Unique ? "Unique selector" : "Set selector",
                    selector.Target.Quality == SupplyQuality.Unique ? "유니크 선택" : "세트 선택",
                    selector.Target.Name,
                    selector.Target.Name,
                    nextItemNameStringId++);
            }

            for (var index = 0; index < selectors.Length; index++)
            {
                var current = selectors[index];
                var next = selectors[(index + 1) % selectors.Length];
                var previous = selectors[(index + selectors.Length - 1) % selectors.Length];
                AddCycleRecipe(cubeMain, category.Key, current.Code, "tsc", next.Code, "next");
                AddCycleRecipe(cubeMain, category.Key, current.Code, "isc", previous.Code, "previous");
                AddCreateRecipe(cubeMain, category, current);
            }
        }

        return targets.Count;
    }

    private static IOrderedEnumerable<SupplyTarget> OrderTargetsForCategory(
        IEnumerable<SupplyTarget> targets,
        SupplyCategory category) =>
        targets
            .OrderBy(target =>
                category.Key == "unique-charms" && target.Name == "Gheed's Fortune" ? 0 : 1)
            .ThenBy(target => target.Name, StringComparer.OrdinalIgnoreCase);

    private static int AddConvenienceSelectorsAndRecipes(
        D2TsvTable misc,
        D2TsvTable cubeMain,
        IReadOnlyList<ConvenienceCategory> categories,
        ISet<string> reservedSelectorCodes,
        JsonArray hdItems,
        JsonArray itemNameStrings,
        IReadOnlyDictionary<string, JsonObject> itemNameLocalizationIndex,
        IReadOnlyDictionary<BaseCreationMode, string> baseModeTokens,
        ref int nextItemNameStringId)
    {
        var template = misc.Rows.Single(row => misc.Get(row, "code") == "isc");
        var selectorCount = 0;
        var selectorCodes = new Dictionary<ConvenienceTarget, string>();
        AllocateConvenienceSelectorCodes(
            categories,
            ConvenienceKind.Base,
            '3',
            reservedSelectorCodes,
            selectorCodes);
        AllocateConvenienceSelectorCodes(
            categories,
            ConvenienceKind.Material,
            '4',
            reservedSelectorCodes,
            selectorCodes);

        foreach (var category in categories)
        {
            var selectors = category.Targets
                .Select(target => new ConvenienceSelector(
                    target,
                    selectorCodes[target]))
                .ToArray();
            if (selectors.Length == 0)
            {
                continue;
            }

            foreach (var selector in selectors)
            {
                var isBaseSelector = category.Kind == ConvenienceKind.Base;
                var row = misc.CloneRow(template);
                ClearVendorColumns(misc, row);
                misc.Set(row, "name", $"JM convenience selector {selector.Code}");
                misc.Set(row, "version", "100");
                misc.Set(row, "level", "1");
                misc.Set(row, "ShowLevel", "0");
                misc.Set(row, "levelreq", "1");
                misc.Set(row, "rarity", "1");
                misc.Set(row, "cost", "1");
                misc.Set(row, "gamble cost", "0");
                misc.Set(row, "code", selector.Code);
                var nameStringKey = CreateSelectorNameStringKey(selector.Code);
                misc.Set(row, "namestr", nameStringKey);
                misc.Set(row, "type", "torc");
                misc.Set(row, "type2", string.Empty);
                misc.Set(row, "invwidth", isBaseSelector ? "1" : selector.Target.InventoryWidth);
                misc.Set(row, "invheight", isBaseSelector ? "1" : selector.Target.InventoryHeight);
                misc.Set(row, "invfile", isBaseSelector ? "invgswe" : selector.Target.InventoryFile);
                misc.Set(row, "flippyfile", isBaseSelector ? "flpgsw" : selector.Target.FlippyFile);
                misc.Set(row, "uniqueinvfile", string.Empty);
                misc.Set(row, "useable", "0");
                misc.Set(row, "pSpell", "-1");
                misc.Set(row, "PermStoreItem", "1");
                misc.Set(row, "multibuy", "0");
                if (ReferenceEquals(selector, selectors[0]))
                {
                    misc.Set(row, category.Vendor + "Min", "1");
                    misc.Set(row, category.Vendor + "Max", "1");
                }

                misc.Rows.Add(row);
                AddHdItemMapping(
                    hdItems,
                    selector.Code,
                    ResolveCategorySelectorAsset(category.Key));
                AddItemNameString(
                    itemNameStrings,
                    itemNameLocalizationIndex,
                    nameStringKey,
                    category.Kind == ConvenienceKind.Base ? "Base selector" : "Material selector",
                    category.Kind == ConvenienceKind.Base
                        ? $"{category.DisplayName} 선택"
                        : "재료 선택",
                    selector.Target.NameStringKey,
                    selector.Target.Name,
                    nextItemNameStringId++);
            }

            for (var index = 0; index < selectors.Length; index++)
            {
                var current = selectors[index];
                var next = selectors[(index + 1) % selectors.Length];
                var previous = selectors[(index + selectors.Length - 1) % selectors.Length];
                AddCycleRecipe(cubeMain, category.Key, current.Code, "tsc", next.Code, "next");
                AddCycleRecipe(cubeMain, category.Key, current.Code, "isc", previous.Code, "previous");
                if (category.Kind == ConvenienceKind.Base)
                {
                    foreach (var mode in BaseCreationModes)
                    {
                        if (!mode.IsEthereal || current.Target.SupportsEthereal)
                        {
                            AddBaseCreateRecipe(
                                cubeMain,
                                category,
                                current,
                                mode,
                                baseModeTokens[mode.Mode]);
                        }
                    }
                }
                else
                {
                    AddMaterialCreateRecipe(cubeMain, category, current);
                }
            }

            selectorCount += selectors.Length;
        }

        return selectorCount;
    }

    private static void AllocateConvenienceSelectorCodes(
        IReadOnlyList<ConvenienceCategory> categories,
        ConvenienceKind kind,
        char prefix,
        ISet<string> reservedSelectorCodes,
        IDictionary<ConvenienceTarget, string> selectorCodes)
    {
        var targets = categories
            .Where(category => category.Kind == kind)
            .SelectMany(category => category.Targets)
            .OrderBy(target => target.SelectorOrder)
            .ToArray();
        if (!targets.Select(target => target.SelectorOrder)
                .SequenceEqual(Enumerable.Range(0, targets.Length)))
        {
            throw new InvalidDataException(
                $"The {kind} selector order is duplicated or has gaps.");
        }

        var nextIndex = 0;
        foreach (var target in targets)
        {
            selectorCodes.Add(
                target,
                AllocateSelectorCode(prefix, ref nextIndex, reservedSelectorCodes));
        }
    }

    private static int AddCharmSelectorsAndRecipes(
        D2TsvTable misc,
        D2TsvTable cubeMain,
        CharmSelectorPlan plan,
        IReadOnlyDictionary<string, string> bonusTokenCodes,
        ISet<string> reservedSelectorCodes,
        JsonArray hdItems,
        JsonArray itemNameStrings,
        ref int nextItemNameStringId)
    {
        var template = misc.Rows.Single(row => misc.Get(row, "code") == "isc");
        var nextSelectorIndex = 0;
        var count = 0;
        foreach (var category in plan.Categories)
        {
            var selectors = category.Targets
                .OrderBy(target => target.SelectorOrder)
                .Select(target => new CharmSelector(
                    target,
                    AllocateSelectorCode('7', ref nextSelectorIndex, reservedSelectorCodes)))
                .ToArray();
            if (selectors.Length == 0)
            {
                throw new InvalidDataException($"Charm selector category '{category.Key}' is empty.");
            }

            foreach (var selector in selectors)
            {
                var row = misc.CloneRow(template);
                ClearVendorColumns(misc, row);
                misc.Set(row, "name", $"JM charm selector {selector.Code}");
                misc.Set(row, "version", "100");
                misc.Set(row, "level", "1");
                misc.Set(row, "ShowLevel", "0");
                misc.Set(row, "levelreq", "1");
                misc.Set(row, "rarity", "1");
                misc.Set(row, "cost", "1");
                misc.Set(row, "gamble cost", "0");
                misc.Set(row, "code", selector.Code);
                var nameStringKey = CreateSelectorNameStringKey(selector.Code);
                misc.Set(row, "namestr", nameStringKey);
                misc.Set(row, "type", "torc");
                misc.Set(row, "type2", string.Empty);
                misc.Set(row, "invwidth", "1");
                misc.Set(row, "invheight", "1");
                misc.Set(row, "invfile", "invgsve");
                misc.Set(row, "flippyfile", "flpgsv");
                misc.Set(row, "uniqueinvfile", string.Empty);
                misc.Set(row, "useable", "0");
                misc.Set(row, "pSpell", "-1");
                misc.Set(row, "PermStoreItem", "1");
                misc.Set(row, "multibuy", "0");
                if (ReferenceEquals(selector, selectors[0]))
                {
                    misc.Set(row, SelectorVendor + "Min", "1");
                    misc.Set(row, SelectorVendor + "Max", "1");
                }

                misc.Rows.Add(row);
                AddHdItemMapping(
                    hdItems,
                    selector.Code,
                    ResolveCategorySelectorAsset(category.Key));
                AddLiteralItemNameString(
                    itemNameStrings,
                    nameStringKey,
                    $"{category.EnglishName}: {selector.Target.EnglishName}",
                    $"{category.KoreanName}: {selector.Target.KoreanName}",
                    nextItemNameStringId++);
            }

            for (var index = 0; index < selectors.Length; index++)
            {
                var current = selectors[index];
                var next = selectors[(index + 1) % selectors.Length];
                var previous = selectors[(index + selectors.Length - 1) % selectors.Length];
                AddCycleRecipe(cubeMain, category.Key, current.Code, "tsc", next.Code, "next");
                AddCycleRecipe(cubeMain, category.Key, current.Code, "isc", previous.Code, "previous");
                foreach (var bonusMode in plan.BonusModes)
                {
                    int? suffixId = null;
                    if (bonusMode.SuffixIds is not null)
                    {
                        if (!bonusMode.SuffixIds.TryGetValue(current.Target.Kind, out var fixedSuffixId))
                        {
                            continue;
                        }

                        suffixId = fixedSuffixId;
                    }

                    AddCharmCreateRecipe(
                        cubeMain,
                        category,
                        current,
                        bonusMode,
                        bonusTokenCodes[bonusMode.Key],
                        suffixId);
                }
            }

            count += selectors.Length;
        }

        if (count != CharmSelectorPlanner.SkillTabCount +
                     plan.Categories
                         .Where(category => !category.Key.StartsWith("skill-charms-", StringComparison.Ordinal))
                         .Sum(category => category.Targets.Count))
        {
            throw new InvalidDataException("The charm selector count is inconsistent.");
        }

        return count;
    }

    private static void AddCharmCreateRecipe(
        D2TsvTable cubeMain,
        CharmSelectorCategory category,
        CharmSelector selector,
        CharmBonusMode bonusMode,
        string controlTokenCode,
        int? suffixId)
    {
        var row = CreateRecipe(
            cubeMain,
            $"JM {category.Key} {bonusMode.Key} {selector.Code}");
        cubeMain.Set(row, "input 1", selector.Code);
        cubeMain.Set(row, "input 2", controlTokenCode);
        var suffix = suffixId is null ? string.Empty : $",suf={suffixId.Value}";
        cubeMain.Set(
            row,
            "output",
            $"\"{selector.Target.ItemCode},mag,pre={selector.Target.PrefixId}{suffix}\"");
        cubeMain.Set(row, "lvl", "99");
        cubeMain.Set(row, "output b", selector.Code);
        cubeMain.Set(row, "b lvl", "1");
        cubeMain.Set(row, "output c", controlTokenCode);
        cubeMain.Set(row, "c lvl", "1");
        cubeMain.Rows.Add(row);
    }

    private static IReadOnlyDictionary<string, JsonObject> CreateItemNameLocalizationIndex(
        JsonArray strings)
    {
        var result = new Dictionary<string, JsonObject>(StringComparer.OrdinalIgnoreCase);
        foreach (var node in strings)
        {
            if (node is not JsonObject entry ||
                entry["Key"]?.GetValue<string>() is not { Length: > 0 } key)
            {
                continue;
            }

            result.TryAdd(key, entry);
        }

        return result;
    }

    private static void AddItemNameString(
        JsonArray strings,
        IReadOnlyDictionary<string, JsonObject> localizationIndex,
        string key,
        string englishPrefix,
        string koreanPrefix,
        string itemNameStringKey,
        string fallbackItemName,
        int id)
    {
        localizationIndex.TryGetValue(itemNameStringKey, out var localizedSource);
        var entry = new JsonObject
        {
            ["id"] = id,
            ["Key"] = key
        };
        foreach (var locale in ItemNameLocales)
        {
            var prefix = locale == "koKR" ? koreanPrefix : englishPrefix;
            var localizedItemName = ResolveLocalizedItemName(
                localizedSource,
                itemNameStringKey,
                locale,
                fallbackItemName);
            entry[locale] = $"{prefix}: {localizedItemName}";
        }

        strings.Add(entry);
    }

    private static IReadOnlyDictionary<int, string> AddSocketTokens(
        D2TsvTable misc,
        ISet<string> reservedCodes,
        JsonArray hdItems,
        JsonArray itemNameStrings,
        ref int nextItemNameStringId)
    {
        var template = misc.Rows.Single(row => misc.Get(row, "code") == "isc");
        var nextCodeIndex = 0;
        var result = new Dictionary<int, string>();
        var visuals = new[]
        {
            (InventoryFile: "invgsve", FlippyFile: "flpgsv", Asset: "gem/perfect_amethyst"),
            (InventoryFile: "invgsye", FlippyFile: "flpgsy", Asset: "gem/perfect_topaz"),
            (InventoryFile: "invgsbe", FlippyFile: "flpgsb", Asset: "gem/perfect_saphire"),
            (InventoryFile: "invgsge", FlippyFile: "flpgsg", Asset: "gem/perfect_emerald"),
            (InventoryFile: "invgsre", FlippyFile: "flpgsr", Asset: "gem/perfect_ruby"),
            (InventoryFile: "invgswe", FlippyFile: "flpgsw", Asset: "gem/perfect_diamond")
        };

        for (var sockets = 1; sockets <= visuals.Length; sockets++)
        {
            var visual = visuals[sockets - 1];
            var code = AllocateSelectorCode('6', ref nextCodeIndex, reservedCodes);
            var nameStringKey = $"jm_socket_token_{sockets}";
            var row = misc.CloneRow(template);
            ClearVendorColumns(misc, row);
            misc.Set(row, "name", $"JM socket token {sockets}");
            misc.Set(row, "version", "100");
            misc.Set(row, "level", "1");
            misc.Set(row, "ShowLevel", "0");
            misc.Set(row, "levelreq", "1");
            misc.Set(row, "rarity", "1");
            misc.Set(row, "cost", "1");
            misc.Set(row, "gamble cost", "0");
            misc.Set(row, "code", code);
            misc.Set(row, "namestr", nameStringKey);
            misc.Set(row, "type", "torc");
            misc.Set(row, "type2", string.Empty);
            misc.Set(row, "invwidth", "1");
            misc.Set(row, "invheight", "1");
            misc.Set(row, "invfile", visual.InventoryFile);
            misc.Set(row, "flippyfile", visual.FlippyFile);
            misc.Set(row, "uniqueinvfile", string.Empty);
            misc.Set(row, "useable", "0");
            misc.Set(row, "pSpell", "-1");
            misc.Set(row, "stackable", "0");
            misc.Set(row, "minstack", "0");
            misc.Set(row, "maxstack", "0");
            misc.Set(row, "spawnstack", "0");
            misc.Set(row, "PermStoreItem", "1");
            misc.Set(row, "multibuy", "0");
            misc.Set(row, SelectorVendor + "Min", "1");
            misc.Set(row, SelectorVendor + "Max", "1");
            misc.Rows.Add(row);

            AddHdItemMapping(hdItems, code, ResolveSelectorAsset($"socket_{sockets}"));
            AddLiteralItemNameString(
                itemNameStrings,
                nameStringKey,
                $"{sockets}-Socket Workstone",
                $"{sockets}소켓 작업석",
                nextItemNameStringId++);
            result.Add(sockets, code);
        }

        return result;
    }

    private static void AddLiteralItemNameString(
        JsonArray strings,
        string key,
        string englishName,
        string koreanName,
        int id)
    {
        var entry = new JsonObject
        {
            ["id"] = id,
            ["Key"] = key
        };
        foreach (var locale in ItemNameLocales)
        {
            entry[locale] = locale == "koKR" ? koreanName : englishName;
        }

        strings.Add(entry);
    }

    private static string ResolveLocalizedItemName(
        JsonObject? localizedSource,
        string itemNameStringKey,
        string locale,
        string fallbackItemName)
    {
        if (localizedSource?[locale]?.GetValue<string>() is { Length: > 0 } localizedName)
        {
            return localizedName;
        }

        if (locale == "koKR" &&
            KoreanItemNameOverrides.TryGetValue(itemNameStringKey, out var koreanOverride))
        {
            return koreanOverride;
        }

        if (localizedSource?["enUS"]?.GetValue<string>() is { Length: > 0 } englishName)
        {
            return englishName;
        }

        return fallbackItemName;
    }

    private static string CreateSelectorNameStringKey(string selectorCode) =>
        $"jm_selector_{selectorCode}";

    private static IReadOnlyDictionary<BaseCreationMode, string> AddBaseCreationModeTokens(
        D2TsvTable misc,
        ISet<string> reservedCodes,
        JsonArray hdItems,
        JsonArray itemNameStrings,
        ref int nextItemNameStringId)
    {
        var visuals = new[]
        {
            TokenVisual.PerfectDiamond,
            TokenVisual.PerfectTopaz,
            TokenVisual.PerfectAmethyst,
            TokenVisual.PerfectSapphire,
            TokenVisual.PerfectEmerald
        };
        var result = new Dictionary<BaseCreationMode, string>();
        var nextCodeIndex = 0;
        for (var index = 0; index < BaseCreationModes.Length; index++)
        {
            var mode = BaseCreationModes[index];
            result.Add(
                mode.Mode,
                AddControlToken(
                    misc,
                    reservedCodes,
                    hdItems,
                    itemNameStrings,
                    '8',
                    ref nextCodeIndex,
                    $"base-mode-{mode.Mode}",
                    mode.EnglishName,
                    mode.KoreanName,
                    visuals[index],
                    ResolveSelectorAsset(BaseModeSelectorAssets[mode.Mode]),
                    ref nextItemNameStringId));
        }

        return result;
    }

    private static IReadOnlyDictionary<string, string> AddCharmBonusTokens(
        D2TsvTable misc,
        IReadOnlyList<CharmBonusMode> bonusModes,
        ISet<string> reservedCodes,
        JsonArray hdItems,
        JsonArray itemNameStrings,
        ref int nextItemNameStringId)
    {
        var visuals = new[]
        {
            TokenVisual.PerfectDiamond,
            TokenVisual.PerfectRuby,
            TokenVisual.PerfectSapphire,
            TokenVisual.PerfectEmerald,
            TokenVisual.PerfectTopaz,
            TokenVisual.PerfectAmethyst,
            TokenVisual.PerfectSkull
        };
        if (bonusModes.Count != visuals.Length)
        {
            throw new InvalidDataException("The charm bonus token visual set is incomplete.");
        }

        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        var nextCodeIndex = 0;
        for (var index = 0; index < bonusModes.Count; index++)
        {
            var mode = bonusModes[index];
            result.Add(
                mode.Key,
                AddControlToken(
                    misc,
                    reservedCodes,
                    hdItems,
                    itemNameStrings,
                    '9',
                    ref nextCodeIndex,
                    $"charm-bonus-{mode.Key}",
                    $"Charm option: {mode.EnglishName}",
                    $"참 옵션: {mode.KoreanName}",
                    visuals[index],
                    ResolveSelectorAsset(CharmBonusSelectorAssets[mode.Key]),
                    ref nextItemNameStringId));
        }

        return result;
    }

    private static string AddCraftToken(
        D2TsvTable misc,
        ISet<string> reservedCodes,
        JsonArray hdItems,
        JsonArray itemNameStrings,
        ref int nextItemNameStringId)
    {
        var nextCodeIndex = 0;
        return AddControlToken(
            misc,
            reservedCodes,
            hdItems,
            itemNameStrings,
            'a',
            ref nextCodeIndex,
            "quick-craft",
            "Quick craft workstone",
            "빠른 크래프트 작업석",
            TokenVisual.PerfectRuby,
            ResolveSelectorAsset("quick_craft"),
            ref nextItemNameStringId);
    }

    private static string AddControlToken(
        D2TsvTable misc,
        ISet<string> reservedCodes,
        JsonArray hdItems,
        JsonArray itemNameStrings,
        char codePrefix,
        ref int nextCodeIndex,
        string identity,
        string englishName,
        string koreanName,
        TokenVisual visual,
        string selectorAsset,
        ref int nextItemNameStringId)
    {
        var template = misc.Rows.Single(row => misc.Get(row, "code") == "isc");
        var code = AllocateSelectorCode(codePrefix, ref nextCodeIndex, reservedCodes);
        var nameStringKey = $"jm_token_{identity.Replace('-', '_')}";
        var row = misc.CloneRow(template);
        ClearVendorColumns(misc, row);
        misc.Set(row, "name", $"JM control token {identity}");
        misc.Set(row, "version", "100");
        misc.Set(row, "level", "1");
        misc.Set(row, "ShowLevel", "0");
        misc.Set(row, "levelreq", "1");
        misc.Set(row, "rarity", "1");
        misc.Set(row, "cost", "1");
        misc.Set(row, "gamble cost", "0");
        misc.Set(row, "code", code);
        misc.Set(row, "namestr", nameStringKey);
        misc.Set(row, "type", "torc");
        misc.Set(row, "type2", string.Empty);
        misc.Set(row, "invwidth", "1");
        misc.Set(row, "invheight", "1");
        misc.Set(row, "invfile", visual.InventoryFile);
        misc.Set(row, "flippyfile", visual.FlippyFile);
        misc.Set(row, "uniqueinvfile", string.Empty);
        misc.Set(row, "useable", "0");
        misc.Set(row, "pSpell", "-1");
        misc.Set(row, "stackable", "0");
        misc.Set(row, "minstack", "0");
        misc.Set(row, "maxstack", "0");
        misc.Set(row, "spawnstack", "0");
        misc.Set(row, "PermStoreItem", "1");
        misc.Set(row, "multibuy", "0");
        misc.Set(row, SelectorVendor + "Min", "1");
        misc.Set(row, SelectorVendor + "Max", "1");
        misc.Rows.Add(row);

        AddHdItemMapping(hdItems, code, selectorAsset);
        AddLiteralItemNameString(
            itemNameStrings,
            nameStringKey,
            englishName,
            koreanName,
            nextItemNameStringId++);
        return code;
    }

    private static void AddBaseCreateRecipe(
        D2TsvTable cubeMain,
        ConvenienceCategory category,
        ConvenienceSelector selector,
        BaseCreationModeDefinition mode,
        string controlTokenCode)
    {
        var row = CreateRecipe(cubeMain, $"JM {category.Key} {mode.Mode} {selector.Code}");
        cubeMain.Set(row, "input 1", selector.Code);
        cubeMain.Set(row, "input 2", controlTokenCode);
        cubeMain.Set(row, "output", $"\"{selector.Target.Code},{mode.OutputQuality}\"");
        cubeMain.Set(row, "lvl", "99");
        cubeMain.Set(row, "output b", selector.Code);
        cubeMain.Set(row, "b lvl", "1");
        cubeMain.Set(row, "output c", controlTokenCode);
        cubeMain.Set(row, "c lvl", "1");
        cubeMain.Rows.Add(row);
    }

    private static void AddMaterialCreateRecipe(
        D2TsvTable cubeMain,
        ConvenienceCategory category,
        ConvenienceSelector selector)
    {
        var row = CreateRecipe(cubeMain, $"JM {category.Key} create {selector.Code}");
        cubeMain.Set(row, "input 1", selector.Code);
        cubeMain.Set(row, "input 2", "key");
        cubeMain.Set(row, "output", selector.Target.Code);
        cubeMain.Set(row, "lvl", "99");
        cubeMain.Set(row, "output b", selector.Code);
        cubeMain.Set(row, "b lvl", "1");
        cubeMain.Set(row, "output c", "key");
        cubeMain.Set(row, "c lvl", "1");
        cubeMain.Rows.Add(row);
    }

    private static int AddWorkbenchRecipes(
        D2TsvTable cubeMain,
        IReadOnlyDictionary<int, string> socketTokenCodes)
    {
        var count = 0;

        var unsocket = CreateRecipe(cubeMain, "JM workbench preserve socket contents");
        cubeMain.Set(unsocket, "input 1", "\"any,sock\"");
        cubeMain.Set(unsocket, "input 2", "isc");
        cubeMain.Set(unsocket, "output", "\"useitem,rem\"");
        cubeMain.Set(unsocket, "output b", "isc");
        cubeMain.Rows.Add(unsocket);
        count++;

        foreach (var (itemType, maximumSockets) in new[]
                 {
                     (ItemType: "weap", MaximumSockets: 6),
                     (ItemType: "armo", MaximumSockets: 4)
                 })
        {
            AddSocketRecipe(
                cubeMain,
                itemType,
                quality: null,
                sockets: 1,
                socketTokenCodes[1]);
            count++;

            for (var sockets = 2; sockets <= maximumSockets; sockets++)
            {
                foreach (var quality in new[] { "low", "nor", "hiq" })
                {
                    AddSocketRecipe(
                        cubeMain,
                        itemType,
                        quality,
                        sockets,
                        socketTokenCodes[sockets]);
                    count++;
                }

                if (sockets == 2)
                {
                    AddSocketRecipe(
                        cubeMain,
                        itemType,
                        "mag",
                        sockets,
                        socketTokenCodes[sockets]);
                    count++;
                }
            }

            var repair = CreateRecipe(cubeMain, $"JM workbench repair recharge {itemType}");
            cubeMain.Set(repair, "input 1", $"\"{itemType},noe\"");
            cubeMain.Set(repair, "input 2", "key");
            cubeMain.Set(repair, "output", "\"useitem,rep,rch,qty=255\"");
            cubeMain.Set(repair, "output b", "key");
            cubeMain.Rows.Add(repair);
            count++;
        }

        for (var rune = 2; rune <= 33; rune++)
        {
            var ratio = rune <= 21 ? 3 : 2;
            AddDowngradeRecipe(
                cubeMain,
                $"JM workbench downgrade rune {rune:00}",
                $"r{rune:00}",
                $"r{rune - 1:00}",
                ratio);
            count++;
        }

        var gemFamilies = new[]
        {
            new[] { "gcv", "gfv", "gsv", "gzv", "gpv" },
            new[] { "gcy", "gfy", "gsy", "gly", "gpy" },
            new[] { "gcb", "gfb", "gsb", "glb", "gpb" },
            new[] { "gcg", "gfg", "gsg", "glg", "gpg" },
            new[] { "gcr", "gfr", "gsr", "glr", "gpr" },
            new[] { "gcw", "gfw", "gsw", "glw", "gpw" },
            new[] { "skc", "skf", "sku", "skl", "skz" }
        };
        foreach (var family in gemFamilies)
        {
            for (var level = 1; level < family.Length; level++)
            {
                AddDowngradeRecipe(
                    cubeMain,
                    $"JM workbench downgrade gem {family[level]}",
                    family[level],
                    family[level - 1],
                    3);
                count++;
            }
        }

        return count;
    }

    private static int AddQuickCraftRecipes(D2TsvTable cubeMain, string craftTokenCode)
    {
        var vanillaCraftRows = cubeMain.Rows
            .Where(row =>
                cubeMain.Get(row, "enabled") == "1" &&
                !cubeMain.Get(row, "description").StartsWith("JM ", StringComparison.Ordinal) &&
                cubeMain.Get(row, "output").Contains("crf", StringComparison.Ordinal))
            .ToArray();
        if (vanillaCraftRows.Length != ExpectedVanillaCraftRecipeCount)
        {
            throw new InvalidDataException(
                $"Expected {ExpectedVanillaCraftRecipeCount} vanilla craft recipes, " +
                $"found {vanillaCraftRows.Length}.");
        }

        foreach (var source in vanillaCraftRows)
        {
            var row = cubeMain.CloneRow(source);
            cubeMain.Set(row, "description", $"JM quick craft: {cubeMain.Get(source, "description")}");
            cubeMain.Set(row, "numinputs", "2");
            for (var input = 2; input <= 7; input++)
            {
                cubeMain.Set(row, $"input {input}", string.Empty);
            }

            cubeMain.Set(row, "input 2", craftTokenCode);
            cubeMain.Set(row, "output b", craftTokenCode);
            cubeMain.Set(row, "b lvl", "1");
            cubeMain.Set(row, "b plvl", string.Empty);
            cubeMain.Set(row, "b ilvl", string.Empty);
            cubeMain.Set(row, "output c", string.Empty);
            cubeMain.Set(row, "c lvl", string.Empty);
            cubeMain.Set(row, "c plvl", string.Empty);
            cubeMain.Set(row, "c ilvl", string.Empty);
            cubeMain.Rows.Add(row);
        }

        return vanillaCraftRows.Length;
    }

    private static int CountCustomCompactItems(D2TsvTable misc) =>
        misc.Rows.Count(row =>
            misc.Get(row, "name").StartsWith("JM ", StringComparison.Ordinal) &&
            misc.Get(row, "type") == "torc" &&
            misc.Get(row, "compactsave") == "1");

    private static void AddSocketRecipe(
        D2TsvTable cubeMain,
        string itemType,
        string? quality,
        int sockets,
        string socketTokenCode)
    {
        var qualityDescription = quality is null ? string.Empty : $" {quality}";
        var socketRecipe = CreateRecipe(
            cubeMain,
            $"JM workbench {itemType}{qualityDescription} add {sockets} sockets");
        var qualityFilter = quality is null ? string.Empty : $",{quality}";
        cubeMain.Set(socketRecipe, "input 1", $"\"{itemType}{qualityFilter},nos\"");
        cubeMain.Set(socketRecipe, "input 2", socketTokenCode);
        cubeMain.Set(socketRecipe, "output", "useitem");
        cubeMain.Set(socketRecipe, "mod 1", "sock");
        cubeMain.Set(socketRecipe, "mod 1 min", sockets.ToString());
        cubeMain.Set(socketRecipe, "mod 1 max", sockets.ToString());
        cubeMain.Rows.Add(socketRecipe);
    }

    private static void AddDowngradeRecipe(
        D2TsvTable cubeMain,
        string description,
        string inputCode,
        string outputCode,
        int quantity)
    {
        var row = CreateRecipe(cubeMain, description);
        cubeMain.Set(row, "input 1", inputCode);
        cubeMain.Set(row, "input 2", "isc");
        cubeMain.Set(row, "output", $"\"{outputCode},qty={quantity}\"");
        cubeMain.Set(row, "output b", "isc");
        cubeMain.Rows.Add(row);
    }

    private static void ClearVendorColumns(D2TsvTable misc, string[] row)
    {
        foreach (var vendor in new[]
                 {
                     "Charsi", "Gheed", "Akara", "Fara", "Lysander", "Drognan", "Hratli",
                     "Alkor", "Ormus", "Elzix", "Asheara", "Cain", "Halbu", "Malah", "Larzuk",
                     "Anya", "Jamella"
                 })
        {
            foreach (var suffix in new[] { "Min", "Max", "MagicMin", "MagicMax", "MagicLvl" })
            {
                misc.Set(row, vendor + suffix, string.Empty);
            }
        }
    }

    private static void AddCycleRecipe(
        D2TsvTable cubeMain,
        string categoryKey,
        string selectorCode,
        string controlCode,
        string outputSelectorCode,
        string direction)
    {
        var row = CreateRecipe(cubeMain, $"JM {categoryKey} {direction} {selectorCode}");
        cubeMain.Set(row, "input 1", selectorCode);
        cubeMain.Set(row, "input 2", controlCode);
        cubeMain.Set(row, "output", outputSelectorCode);
        cubeMain.Set(row, "output b", controlCode);
        cubeMain.Set(row, "b lvl", "1");
        cubeMain.Rows.Add(row);
    }

    private static void AddCreateRecipe(
        D2TsvTable cubeMain,
        SupplyCategory category,
        Selector selector)
    {
        var row = CreateRecipe(cubeMain, $"JM {category.Key} create {selector.Code}");
        cubeMain.Set(row, "input 1", selector.Code);
        cubeMain.Set(row, "input 2", "key");
        cubeMain.Set(row, "output", selector.Target.Name);
        cubeMain.Set(row, "lvl", "100");
        cubeMain.Set(row, "ilvl", "100");
        cubeMain.Set(row, "output b", selector.Code);
        cubeMain.Set(row, "b lvl", "1");
        cubeMain.Set(row, "output c", "key");
        cubeMain.Set(row, "c lvl", "1");
        cubeMain.Rows.Add(row);
    }

    private static string[] CreateRecipe(D2TsvTable cubeMain, string description)
    {
        var row = cubeMain.NewRow();
        cubeMain.Set(row, "description", description);
        cubeMain.Set(row, "enabled", "1");
        cubeMain.Set(row, "version", "100");
        cubeMain.Set(row, "numinputs", "2");
        cubeMain.Set(row, "*eol", "0");
        return row;
    }

    private static string AllocateSelectorCode(
        char prefix,
        ref int nextIndex,
        ISet<string> reservedCodes)
    {
        while (true)
        {
            var code = CreateSelectorCode(prefix, nextIndex++);
            if (reservedCodes.Add(code))
            {
                return code;
            }
        }
    }

    private static string CreateSelectorCode(char prefix, int index)
    {
        const string digits = "0123456789abcdefghijklmnopqrstuvwxyz";
        Span<char> result = stackalloc char[3];
        result[0] = prefix;
        for (var position = 2; position >= 1; position--)
        {
            result[position] = digits[index % digits.Length];
            index /= digits.Length;
        }

        if (index != 0)
        {
            throw new InvalidDataException("A selector catalog exceeds its three-character D2 item-code space.");
        }

        return new string(result);
    }

    private static Dictionary<string, string> CreateHdItemAssetIndex(JsonArray hdItems)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var node in hdItems)
        {
            if (node is not JsonObject entry)
            {
                continue;
            }

            foreach (var mapping in entry)
            {
                var asset = mapping.Value?["asset"]?.GetValue<string>();
                if (!string.IsNullOrWhiteSpace(asset))
                {
                    result[mapping.Key] = asset;
                }
            }
        }

        return result;
    }

    private static void AddHdItemMapping(JsonArray hdItems, string code, string asset)
    {
        hdItems.Add(new JsonObject
        {
            [code] = new JsonObject { ["asset"] = asset }
        });
    }

    private static string ResolveCategorySelectorAsset(string categoryKey)
    {
        if (!CategorySelectorAssets.TryGetValue(categoryKey, out var asset))
        {
            throw new InvalidDataException(
                $"Selector category '{categoryKey}' does not have a dedicated HD icon.");
        }

        return ResolveSelectorAsset(asset);
    }

    private static string ResolveSelectorAsset(string asset) => SelectorAssetRoot + asset;

    private static string[] CreateStaticHdSelectorAssets()
    {
        var assets = CategorySelectorAssets.Values
            .Concat(BaseModeSelectorAssets.Values)
            .Concat(CharmBonusSelectorAssets.Values)
            .Concat(Enumerable.Range(1, 6).Select(sockets => $"socket_{sockets}"))
            .Append("quick_craft")
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();
        return assets.SelectMany(asset => new[]
            {
                $"data/hd/global/ui/items/misc/jm_selectors/{asset}.sprite",
                $"data/hd/global/ui/items/misc/jm_selectors/{asset}.lowend.sprite",
                $"data/hd/items/misc/jm_selectors/{asset}.json"
            })
            .ToArray();
    }

    private static async Task CopyStaticHdSelectorAssetsAsync(
        string outputDirectory,
        CancellationToken cancellationToken)
    {
        foreach (var relativePath in StaticHdSelectorAssets)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var destinationPath = Path.Combine(
                outputDirectory,
                relativePath.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);

            await using var source = JmSupplyModPackage.OpenFile(relativePath);
            await using var destination = new FileStream(
                destinationPath,
                FileMode.Create,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 64 * 1024,
                useAsync: true);
            await source.CopyToAsync(destination, cancellationToken);
        }
    }

    private static Task WriteLootFilterAsync(
        string path,
        CancellationToken cancellationToken)
    {
        var equipmentQualities = new[] { "normal", "exceptional", "elite" };
        object[] rules =
        [
            new
            {
                name = "유니크와 세트",
                enabled = true,
                ruleType = "show",
                equipmentRarity = new[] { "unique", "set" },
                equipmentQuality = equipmentQualities,
                filterEtherealSocketed = false
            },
            new
            {
                name = "고급 룬 (20-33)",
                enabled = true,
                ruleType = "show",
                itemCode = Enumerable.Range(20, 14).Select(number => $"r{number:00}").ToArray(),
                filterEtherealSocketed = false
            },
            new
            {
                name = "모든 룬",
                enabled = true,
                ruleType = "show",
                itemCode = Enumerable.Range(1, 33).Select(number => $"r{number:00}").ToArray(),
                filterEtherealSocketed = false
            },
            new
            {
                name = "상급과 최상급 보석",
                enabled = true,
                ruleType = "show",
                itemCode = new[]
                {
                    "gzv", "gly", "glb", "glg", "glr", "glw", "skl",
                    "gpv", "gpy", "gpb", "gpg", "gpr", "gpw", "skz"
                },
                filterEtherealSocketed = false
            },
            new
            {
                name = "참과 주얼",
                enabled = true,
                ruleType = "show",
                equipmentRarity = new[]
                {
                    "normal", "hiQuality", "magic", "rare", "unique", "set"
                },
                equipmentItemCode = new[] { "cm1", "cm2", "cm3", "jew" },
                filterEtherealSocketed = false
            },
            new
            {
                name = "소켓 또는 에테리얼 엘리트 베이스",
                enabled = true,
                ruleType = "show",
                equipmentRarity = new[] { "normal", "hiQuality" },
                equipmentQuality = new[] { "elite" },
                equipmentCategory = new[] { "armo", "weap" },
                filterEtherealSocketed = true
            },
            new
            {
                name = "열쇠와 제작 재료",
                enabled = true,
                ruleType = "show",
                itemCode = new[]
                {
                    "pk1", "pk2", "pk3", "tes", "ceh", "bet", "fed", "toa"
                },
                filterEtherealSocketed = false
            },
            new
            {
                name = "낮은 등급 물약 숨기기 (선택)",
                enabled = false,
                ruleType = "hide",
                itemCode = new[]
                {
                    "hp1", "hp2", "hp3", "hp4", "mp1", "mp2", "mp3", "mp4"
                },
                filterEtherealSocketed = false
            }
        ];
        return WriteJsonAsync(
            path,
            new { name = "정만서버 안전 기본", rules },
            cancellationToken);
    }

    private static async Task WriteJsonAsync<T>(
        string path,
        T value,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        await using var stream = File.Create(path);
        await JsonSerializer.SerializeAsync(
            stream,
            value,
            new JsonSerializerOptions { WriteIndented = true },
            cancellationToken);
    }

    private enum SupplyQuality
    {
        Unique,
        Set
    }

    private enum BaseItemKind
    {
        Armor,
        Weapon,
        Misc
    }

    private sealed record BaseItem(
        string Code,
        string Name,
        string NameStringKey,
        string Type,
        BaseItemKind Kind,
        bool IsQuestItem,
        bool IsSpawnable,
        bool SupportsEthereal,
        string InventoryWidth,
        string InventoryHeight,
        string InventoryFile,
        string FlippyFile)
    {
        public int LevelSortKey => Kind switch
        {
            BaseItemKind.Misc => 0,
            BaseItemKind.Armor => 1,
            BaseItemKind.Weapon => 2,
            _ => 3
        };
    }

    private sealed record SupplyTarget(
        string Name,
        string Code,
        BaseItem BaseItem,
        SupplyQuality Quality,
        SupplyCategory Category);

    private sealed record Selector(SupplyTarget Target, string Code);

    private enum ConvenienceKind
    {
        Base,
        Material
    }

    private sealed record ConvenienceTarget(
        string Name,
        string NameStringKey,
        string Code,
        string ItemType,
        int SelectorOrder,
        bool SupportsEthereal,
        string InventoryWidth,
        string InventoryHeight,
        string InventoryFile,
        string FlippyFile);

    private sealed record ConvenienceSelector(ConvenienceTarget Target, string Code);

    private sealed record CharmSelector(CharmSelectorTarget Target, string Code);

    private sealed record ConvenienceCategory(
        string Key,
        string DisplayName,
        string Vendor,
        ConvenienceKind Kind,
        IReadOnlyList<ConvenienceTarget> Targets);

    private sealed record SupplyCategory(
        string Key,
        string DisplayName,
        string Vendor,
        SupplyQuality Quality,
        IReadOnlyList<string> ItemTypes);

    private enum BaseCreationMode
    {
        Normal,
        Superior,
        Magic,
        Ethereal,
        SuperiorEthereal
    }

    private sealed record BaseCreationModeDefinition(
        BaseCreationMode Mode,
        string EnglishName,
        string KoreanName,
        string OutputQuality,
        bool IsEthereal);

    private sealed record TokenVisual(
        string InventoryFile,
        string FlippyFile,
        string Asset)
    {
        public static TokenVisual PerfectAmethyst { get; } =
            new("invgsve", "flpgsv", "gem/perfect_amethyst");

        public static TokenVisual PerfectTopaz { get; } =
            new("invgsye", "flpgsy", "gem/perfect_topaz");

        public static TokenVisual PerfectSapphire { get; } =
            new("invgsbe", "flpgsb", "gem/perfect_saphire");

        public static TokenVisual PerfectEmerald { get; } =
            new("invgsge", "flpgsg", "gem/perfect_emerald");

        public static TokenVisual PerfectRuby { get; } =
            new("invgsre", "flpgsr", "gem/perfect_ruby");

        public static TokenVisual PerfectDiamond { get; } =
            new("invgswe", "flpgsw", "gem/perfect_diamond");

        public static TokenVisual PerfectSkull { get; } =
            new("invskz", "flpskl", "gem/perfect_skull");
    }
}

public sealed record SupplyBuildResult(
    int UniqueItemCount,
    int SetItemCount,
    int SelectorCount,
    int BaseSelectorCount,
    int MaterialSelectorCount,
    int CharmSelectorCount,
    int ControlTokenCount,
    int CustomItemCount,
    int WorkbenchRecipeCount,
    int QuickCraftRecipeCount,
    int FileCount,
    string OutputDirectory);

public sealed record SupplyCatalog(
    int SchemaVersion,
    string GameVersion,
    string SourceRevision,
    int UniqueItemCount,
    int SetItemCount,
    int SelectorCount,
    int BaseSelectorCount,
    int MaterialSelectorCount,
    int CharmSelectorCount,
    int ControlTokenCount,
    int CustomItemCount,
    int WorkbenchRecipeCount,
    int QuickCraftRecipeCount,
    int GambleUniqueWeight,
    int GambleSetWeight,
    int GambleRareWeight,
    IReadOnlyList<SupplyCatalogCategory> Categories);

public sealed record SupplyCatalogCategory(
    string Key,
    string DisplayName,
    string Vendor,
    int ItemCount,
    string? FirstItemName);

public sealed record SupplyPackageManifest(
    int SchemaVersion,
    string GameVersion,
    string SourceRevision,
    int UniqueItemCount,
    int SetItemCount,
    int SelectorCount,
    int BaseSelectorCount,
    int MaterialSelectorCount,
    int CharmSelectorCount,
    int ControlTokenCount,
    int CustomItemCount,
    int WorkbenchRecipeCount,
    int QuickCraftRecipeCount,
    IReadOnlyList<SupplyPackageFile> Files);

public sealed record SupplyPackageFile(string RelativePath, string Sha256);
