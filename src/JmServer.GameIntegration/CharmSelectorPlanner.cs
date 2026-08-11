namespace JmServer.GameIntegration;

internal static class CharmSelectorPlanner
{
    public const int CharacterClassCount = 8;
    public const int SkillTabsPerClass = 3;
    public const int SkillTabCount = CharacterClassCount * SkillTabsPerClass;

    private static readonly CharacterClassDefinition[] CharacterClasses =
    [
        new("amazon", "Amazon", "아마존", ["활·쇠뇌", "지속·마법", "투창·창"]),
        new("sorceress", "Sorceress", "원소술사", ["화염", "번개", "냉기"]),
        new("necromancer", "Necromancer", "강령술사", ["저주", "독·뼈", "소환"]),
        new("paladin", "Paladin", "성기사", ["전투", "공격 오라", "방어 오라"]),
        new("barbarian", "Barbarian", "야만용사", ["숙련", "전투", "함성"]),
        new("druid", "Druid", "드루이드", ["소환", "변신", "원소"]),
        new("assassin", "Assassin", "암살자", ["덫", "그림자 단련", "무술"]),
        new("warlock", "Warlock", "악마술사", ["악마 지배", "사술", "혼돈"])
    ];

    private static readonly PrefixDefinition[] SmallCharmPrefixes =
    [
        new("fine", "Fine (damage / attack rating)", "정교한: 최대 피해·명중률", "Fine", "att", "dmg-max"),
        new("shimmering", "Shimmering (all resistance)", "반짝이는: 모든 저항", "Shimmering", "res-all"),
        new("sapphire", "Sapphire (cold resistance)", "사파이어: 냉기 저항", "Sapphire", "res-cold"),
        new("ruby", "Ruby (fire resistance)", "루비: 화염 저항", "Ruby", "res-fire"),
        new("amber", "Amber (lightning resistance)", "호박색: 번개 저항", "Amber", "res-ltng"),
        new("emerald", "Emerald (poison resistance)", "에메랄드: 독 저항", "Emerald", "res-pois"),
        new("serpents", "Serpent's (mana)", "뱀의: 마나", "Serpent's", "mana"),
        new("pestilent", "Pestilent (poison damage)", "역병의: 독 피해", "Pestilent", "dmg-pois"),
        new("flaming", "Flaming (fire damage)", "불타는: 화염 피해", "Flaming", "fire-min", "fire-max"),
        new("shocking", "Shocking (lightning damage)", "충격의: 번개 피해", "Shocking", "ltng-min", "ltng-max"),
        new("boreal", "Boreal (cold damage)", "북풍의: 냉기 피해", "Boreal", "cold-len", "cold-min")
    ];

    private static readonly PrefixDefinition[] LargeCharmPrefixes =
    [
        new("sharp", "Sharp (damage / attack rating)", "날카로운: 최대 피해·명중률", "Sharp", "att", "dmg-max"),
        new("shimmering", "Shimmering (all resistance)", "반짝이는: 모든 저항", "Shimmering", "res-all"),
        new("sapphire", "Sapphire (cold resistance)", "사파이어: 냉기 저항", "Sapphire", "res-cold"),
        new("ruby", "Ruby (fire resistance)", "루비: 화염 저항", "Ruby", "res-fire"),
        new("amber", "Amber (lightning resistance)", "호박색: 번개 저항", "Amber", "res-ltng"),
        new("emerald", "Emerald (poison resistance)", "에메랄드: 독 저항", "Emerald", "res-pois"),
        new("serpents", "Serpent's (mana)", "뱀의: 마나", "Serpent's", "mana"),
        new("pestilent", "Pestilent (poison damage)", "역병의: 독 피해", "Pestilent", "dmg-pois"),
        new("flaming", "Flaming (fire damage)", "불타는: 화염 피해", "Flaming", "fire-min", "fire-max"),
        new("shocking", "Shocking (lightning damage)", "충격의: 번개 피해", "Shocking", "ltng-min", "ltng-max"),
        new("hibernal", "Hibernal (cold damage)", "동면의: 냉기 피해", "Hibernal", "cold-len", "cold-min")
    ];

    public static CharmSelectorPlan Create(D2TsvTable prefixes, D2TsvTable suffixes)
    {
        var skillAffixes = EnumerateAffixes(prefixes)
            .Where(affix =>
                prefixes.Get(affix.Row, "spawnable") == "1" &&
                Supports(prefixes, affix.Row, "lcha") &&
                prefixes.Get(affix.Row, "mod1code") == "skilltab" &&
                prefixes.Get(affix.Row, "mod1min") == "1" &&
                prefixes.Get(affix.Row, "mod1max") == "1" &&
                int.TryParse(prefixes.Get(affix.Row, "mod1param"), out _))
            .Select(affix => new
            {
                Affix = affix,
                SkillTab = int.Parse(prefixes.Get(affix.Row, "mod1param"))
            })
            .OrderBy(entry => entry.SkillTab)
            .ToArray();

        var expectedTabs = Enumerable.Range(0, SkillTabCount).ToArray();
        if (skillAffixes.Length != SkillTabCount ||
            !skillAffixes.Select(entry => entry.SkillTab).SequenceEqual(expectedTabs))
        {
            throw new InvalidDataException(
                $"Expected one grand-charm skill affix for every tab 0-{SkillTabCount - 1}.");
        }

        var categories = new List<CharmSelectorCategory>();
        for (var classId = 0; classId < CharacterClasses.Length; classId++)
        {
            var definition = CharacterClasses[classId];
            var targets = skillAffixes
                .Where(entry => entry.SkillTab / SkillTabsPerClass == classId)
                .Select(entry =>
                {
                    var tabIndex = entry.SkillTab % SkillTabsPerClass;
                    return new CharmSelectorTarget(
                        $"{definition.EnglishName}: {prefixes.Get(entry.Affix.Row, "Name")}",
                        $"{definition.KoreanName}: {definition.KoreanSkillTabs[tabIndex]} +1",
                        "cm3",
                        CharmKind.Grand,
                        entry.Affix.Id,
                        entry.SkillTab);
                })
                .ToArray();
            if (targets.Length != SkillTabsPerClass)
            {
                throw new InvalidDataException(
                    $"Class id {classId} does not have exactly {SkillTabsPerClass} skill charms.");
            }

            categories.Add(new CharmSelectorCategory(
                $"skill-charms-{definition.Key}",
                $"{definition.EnglishName} skill charms",
                $"{definition.KoreanName} 스킬참",
                targets));
        }

        categories.Add(CreatePopularCharmCategory(
            prefixes,
            "popular-small-charms",
            "Popular small charms",
            "인기 스몰참",
            "cm1",
            "scha",
            CharmKind.Small,
            SmallCharmPrefixes));
        categories.Add(CreatePopularCharmCategory(
            prefixes,
            "popular-large-charms",
            "Popular large charms",
            "인기 라지참",
            "cm2",
            "mcha",
            CharmKind.Large,
            LargeCharmPrefixes));

        var bonusModes = new[]
        {
            new CharmBonusMode("random", "Preferred random suffix", "선호 옵션 랜덤", null),
            CreateBonusMode(suffixes, "vitality", "Vitality", "생명력", "hp"),
            CreateBonusMode(suffixes, "fhr", "Faster hit recovery", "빠른 타격 회복", "balance"),
            CreateBonusMode(suffixes, "movement", "Faster run / walk", "달리기·걷기 속도", "move"),
            CreateBonusMode(suffixes, "strength", "Strength", "힘", "str"),
            CreateBonusMode(suffixes, "dexterity", "Dexterity", "민첩", "dex"),
            CreateSmallOnlyBonusMode(suffixes, "magic-find", "Magic find", "마법 아이템 발견", "mag%")
        };

        var preferredSuffixIds = bonusModes
            .SelectMany(mode => mode.SuffixIds?.Values ?? [])
            .ToHashSet();
        return new CharmSelectorPlan(categories, bonusModes, preferredSuffixIds);
    }

    private static CharmSelectorCategory CreatePopularCharmCategory(
        D2TsvTable prefixes,
        string key,
        string englishName,
        string koreanName,
        string itemCode,
        string itemType,
        CharmKind kind,
        IReadOnlyList<PrefixDefinition> definitions)
    {
        var targets = definitions.Select((definition, index) =>
        {
            var affix = FindBestAffix(prefixes, itemType, row =>
                prefixes.Get(row, "Name") == definition.AffixName &&
                prefixes.Get(row, "mod1code") == definition.Mod1Code &&
                (definition.Mod2Code is null ||
                 prefixes.Get(row, "mod2code") == definition.Mod2Code));
            return new CharmSelectorTarget(
                definition.EnglishName,
                definition.KoreanName,
                itemCode,
                kind,
                affix.Id,
                index);
        }).ToArray();
        return new CharmSelectorCategory(key, englishName, koreanName, targets);
    }

    private static CharmBonusMode CreateBonusMode(
        D2TsvTable suffixes,
        string key,
        string englishName,
        string koreanName,
        string propertyFamily)
    {
        var suffixIds = new Dictionary<CharmKind, int>
        {
            [CharmKind.Small] = FindBestSuffix(suffixes, "scha", propertyFamily).Id,
            [CharmKind.Large] = FindBestSuffix(suffixes, "mcha", propertyFamily).Id,
            [CharmKind.Grand] = FindBestSuffix(suffixes, "lcha", propertyFamily).Id
        };
        return new CharmBonusMode(key, englishName, koreanName, suffixIds);
    }

    private static CharmBonusMode CreateSmallOnlyBonusMode(
        D2TsvTable suffixes,
        string key,
        string englishName,
        string koreanName,
        string propertyFamily) =>
        new(
            key,
            englishName,
            koreanName,
            new Dictionary<CharmKind, int>
            {
                [CharmKind.Small] = FindBestSuffix(suffixes, "scha", propertyFamily).Id
            });

    private static AffixRow FindBestSuffix(
        D2TsvTable suffixes,
        string itemType,
        string propertyFamily) =>
        FindBestAffix(suffixes, itemType, row =>
        {
            var property = suffixes.Get(row, "mod1code");
            return propertyFamily switch
            {
                "balance" => property.StartsWith("balance", StringComparison.Ordinal),
                "move" => property.StartsWith("move", StringComparison.Ordinal),
                _ => property == propertyFamily
            };
        });

    private static AffixRow FindBestAffix(
        D2TsvTable table,
        string itemType,
        Func<string[], bool> predicate)
    {
        var candidates = EnumerateAffixes(table)
            .Where(affix =>
                table.Get(affix.Row, "spawnable") == "1" &&
                Supports(table, affix.Row, itemType) &&
                int.TryParse(table.Get(affix.Row, "level"), out var level) &&
                level <= 99 &&
                predicate(affix.Row))
            .OrderByDescending(affix => int.Parse(table.Get(affix.Row, "level")))
            .ThenByDescending(affix => affix.Id)
            .ToArray();
        return candidates.Length == 0
            ? throw new InvalidDataException($"No affix matches item type '{itemType}'.")
            : candidates[0];
    }

    private static IEnumerable<AffixRow> EnumerateAffixes(D2TsvTable table)
    {
        for (var index = 0; index < table.Rows.Count; index++)
        {
            yield return new AffixRow(index, table.Rows[index]);
        }
    }

    private static bool Supports(D2TsvTable table, string[] row, string itemType)
    {
        for (var index = 1; index <= 7; index++)
        {
            if (table.Get(row, $"itype{index}") == itemType)
            {
                return true;
            }
        }

        return false;
    }

    private sealed record CharacterClassDefinition(
        string Key,
        string EnglishName,
        string KoreanName,
        IReadOnlyList<string> KoreanSkillTabs);

    private sealed record PrefixDefinition(
        string Key,
        string EnglishName,
        string KoreanName,
        string AffixName,
        string Mod1Code,
        string? Mod2Code = null);

    private sealed record AffixRow(int Id, string[] Row);
}

internal enum CharmKind
{
    Small,
    Large,
    Grand
}

internal sealed record CharmSelectorTarget(
    string EnglishName,
    string KoreanName,
    string ItemCode,
    CharmKind Kind,
    int PrefixId,
    int SelectorOrder);

internal sealed record CharmSelectorCategory(
    string Key,
    string EnglishName,
    string KoreanName,
    IReadOnlyList<CharmSelectorTarget> Targets);

internal sealed record CharmBonusMode(
    string Key,
    string EnglishName,
    string KoreanName,
    IReadOnlyDictionary<CharmKind, int>? SuffixIds);

internal sealed record CharmSelectorPlan(
    IReadOnlyList<CharmSelectorCategory> Categories,
    IReadOnlyList<CharmBonusMode> BonusModes,
    IReadOnlySet<int> PreferredSuffixIds);
