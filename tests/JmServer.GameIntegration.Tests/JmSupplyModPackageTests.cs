using System.Text.RegularExpressions;
using JmServer.GameIntegration;

namespace JmServer.GameIntegration.Tests;

public sealed partial class JmSupplyModPackageTests
{
    [Theory]
    [InlineData(20, 30, 28)]
    [InlineData(20, 35, 32)]
    [InlineData(1, 2, 2)]
    [InlineData(50, 50, 50)]
    [InlineData(-90, -70, -75)]
    [InlineData(-20, -10, -12)]
    [InlineData(-65, -45, -50)]
    public void PreferredMinimumUsesTopQuarterOfOriginalRange(
        int minimum,
        int maximum,
        int expected)
    {
        Assert.Equal(
            expected,
            InGameSupplyModBuilder.CalculatePreferredMinimum(minimum, maximum));
    }

    [Theory]
    [InlineData(20, 30, 29)]
    [InlineData(20, 35, 34)]
    [InlineData(1, 2, 2)]
    [InlineData(50, 50, 50)]
    [InlineData(-90, -70, -72)]
    [InlineData(-20, -10, -11)]
    [InlineData(-65, -45, -47)]
    public void PreferredAffixMinimumUsesTopTenthOfOriginalRange(
        int minimum,
        int maximum,
        int expected)
    {
        Assert.Equal(
            expected,
            InGameSupplyModBuilder.CalculatePreferredAffixMinimum(minimum, maximum));
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(19, 1)]
    [InlineData(20, 3)]
    [InlineData(40, 6)]
    [InlineData(60, 10)]
    [InlineData(80, 15)]
    [InlineData(110, 15)]
    public void AffixWeightMultiplierFavorsHighLevelAffixes(int level, int expected)
    {
        Assert.Equal(expected, InGameSupplyModBuilder.CalculateAffixWeightMultiplier(level));
    }

    [Theory]
    [InlineData("res-all", 1)]
    [InlineData("skilltab", 10)]
    [InlineData("sock", 14)]
    [InlineData("dmg-min", 5)]
    [InlineData("dmg-max", 6)]
    [InlineData("dmg-fire", 15)]
    [InlineData("hp/lvl", 17)]
    [InlineData("ac/time", 18)]
    [InlineData("war", 21)]
    [InlineData("oskill", 22)]
    public void PreferredRollAllowsGameplayPowerValues(string propertyCode, int function)
    {
        Assert.True(InGameSupplyModBuilder.ShouldApplyPreferredRoll(propertyCode, function));
    }

    [Theory]
    [InlineData("randclassskill", 36)]
    [InlineData("skill-rand", 12)]
    [InlineData("bloody", 1)]
    [InlineData("color", 1)]
    [InlineData("state", 24)]
    [InlineData("ease", 1)]
    [InlineData("levelreq", 1)]
    [InlineData("hit-skill", 11)]
    [InlineData("charged", 19)]
    public void PreferredRollRejectsStructuralCosmeticAndCompoundValues(
        string propertyCode,
        int function)
    {
        Assert.False(InGameSupplyModBuilder.ShouldApplyPreferredRoll(propertyCode, function));
    }

    [Fact]
    public async Task EmbeddedPackageIsCompleteAndInternallyConsistent()
    {
        var temporaryDirectory = Path.Combine(
            Path.GetTempPath(),
            "jm-supply-test-" + Guid.NewGuid().ToString("N"));
        try
        {
            await JmSupplyModPackage.WriteToDirectoryAsync(temporaryDirectory);

            var verification = await JmSupplyModPackage.VerifyAsync(temporaryDirectory);
            Assert.True(verification.IsValid, verification.Message);
            Assert.Equal("3.2.92777", JmSupplyModPackage.Manifest.GameVersion);
            Assert.Equal(410, JmSupplyModPackage.Manifest.UniqueItemCount);
            Assert.Equal(140, JmSupplyModPackage.Manifest.SetItemCount);
            Assert.Equal(550, JmSupplyModPackage.Manifest.SelectorCount);
            Assert.Equal(508, JmSupplyModPackage.Manifest.BaseSelectorCount);
            Assert.Equal(71, JmSupplyModPackage.Manifest.MaterialSelectorCount);
            Assert.Equal(46, JmSupplyModPackage.Manifest.CharmSelectorCount);
            Assert.Equal(19, JmSupplyModPackage.Manifest.ControlTokenCount);
            Assert.Equal(1_194, JmSupplyModPackage.Manifest.CustomItemCount);
            Assert.Equal(91, JmSupplyModPackage.Manifest.WorkbenchRecipeCount);
            Assert.Equal(36, JmSupplyModPackage.Manifest.QuickCraftRecipeCount);
            Assert.Equal(22, JmSupplyModPackage.Manifest.Files.Count);
            Assert.Contains(
                JmSupplyModPackage.Manifest.Files,
                file => file.RelativePath ==
                        "data/hd/global/ui/items/misc/jm_selectors/unique_sword.sprite");
            Assert.Contains(
                JmSupplyModPackage.Manifest.Files,
                file => file.RelativePath ==
                        "data/hd/global/ui/items/misc/jm_selectors/unique_sword.lowend.sprite");
            Assert.Contains(
                JmSupplyModPackage.Manifest.Files,
                file => file.RelativePath ==
                        "data/hd/items/misc/jm_selectors/unique_sword.json");

            var uniqueSwordSprite = File.ReadAllBytes(Path.Combine(
                temporaryDirectory,
                "data", "hd", "global", "ui", "items", "misc", "jm_selectors",
                "unique_sword.sprite"));
            Assert.Equal("SpA1", System.Text.Encoding.ASCII.GetString(uniqueSwordSprite, 0, 4));
            Assert.Equal(31, BitConverter.ToUInt16(uniqueSwordSprite, 4));
            Assert.Equal(98, BitConverter.ToInt32(uniqueSwordSprite, 8));
            Assert.Equal(98, BitConverter.ToInt32(uniqueSwordSprite, 12));
            Assert.Equal(40 + (98 * 98 * 4), uniqueSwordSprite.Length);

            var uniqueSwordLowEndSprite = File.ReadAllBytes(Path.Combine(
                temporaryDirectory,
                "data", "hd", "global", "ui", "items", "misc", "jm_selectors",
                "unique_sword.lowend.sprite"));
            Assert.Equal("SpA1", System.Text.Encoding.ASCII.GetString(
                uniqueSwordLowEndSprite,
                0,
                4));
            Assert.Equal(31, BitConverter.ToUInt16(uniqueSwordLowEndSprite, 4));
            Assert.Equal(49, BitConverter.ToInt32(uniqueSwordLowEndSprite, 8));
            Assert.Equal(49, BitConverter.ToInt32(uniqueSwordLowEndSprite, 12));
            Assert.Equal(40 + (49 * 49 * 4), uniqueSwordLowEndSprite.Length);

            using var uniqueSwordDefinition = System.Text.Json.JsonDocument.Parse(
                await File.ReadAllTextAsync(Path.Combine(
                    temporaryDirectory,
                    "data", "hd", "items", "misc", "jm_selectors", "unique_sword.json")));
            Assert.Equal(
                "UnitDefinition",
                uniqueSwordDefinition.RootElement.GetProperty("type").GetString());

            var excelDirectory = Path.Combine(temporaryDirectory, "data", "global", "excel");
            var armor = D2TsvTable.Load(Path.Combine(excelDirectory, "armor.txt"));
            var weapons = D2TsvTable.Load(Path.Combine(excelDirectory, "weapons.txt"));
            var misc = D2TsvTable.Load(Path.Combine(excelDirectory, "misc.txt"));
            var cube = D2TsvTable.Load(Path.Combine(excelDirectory, "cubemain.txt"));
            var uniqueItems = D2TsvTable.Load(Path.Combine(excelDirectory, "uniqueitems.txt"));
            var setItems = D2TsvTable.Load(Path.Combine(excelDirectory, "setitems.txt"));
            var gamble = D2TsvTable.Load(Path.Combine(excelDirectory, "gamble.txt"));
            var difficulties = D2TsvTable.Load(Path.Combine(excelDirectory, "difficultylevels.txt"));
            var magicPrefixes = D2TsvTable.Load(Path.Combine(excelDirectory, "magicprefix.txt"));
            var magicSuffixes = D2TsvTable.Load(Path.Combine(excelDirectory, "magicsuffix.txt"));
            var autoMagic = D2TsvTable.Load(Path.Combine(excelDirectory, "automagic.txt"));
            var qualityItems = D2TsvTable.Load(Path.Combine(excelDirectory, "qualityitems.txt"));
            var runewords = D2TsvTable.Load(Path.Combine(excelDirectory, "runes.txt"));
            var levelPresets = D2TsvTable.Load(Path.Combine(excelDirectory, "lvlprest.txt"));

            var actualLevelPresets = levelPresets.Rows
                .Where(row => int.TryParse(levelPresets.Get(row, "Def"), out _))
                .ToArray();
            Assert.Equal(InGameSupplyModBuilder.FullAutomapPresetCount, actualLevelPresets.Length);
            Assert.All(actualLevelPresets, row => Assert.Equal("1", levelPresets.Get(row, "AutoMap")));

            var duskShroud = armor.Rows.Single(row => armor.Get(row, "name") == "Dusk Shroud");
            Assert.Equal("441", armor.Get(duskShroud, "minac"));
            Assert.Equal("467", armor.Get(duskShroud, "maxac"));

            var callToArms = runewords.Rows.Single(row =>
                runewords.Get(row, "*Rune Name") == "Call to Arms");
            Assert.Equal("5", runewords.Get(callToArms, "T1Min5"));
            Assert.Equal("6", runewords.Get(callToArms, "T1Max5"));
            var grief = runewords.Rows.Single(row => runewords.Get(row, "*Rune Name") == "Grief");
            Assert.Equal("385", runewords.Get(grief, "T1Min2"));
            Assert.Equal("400", runewords.Get(grief, "T1Max2"));

            var coldRupture = uniqueItems.Rows.Single(row =>
                uniqueItems.Get(row, "index") == "Cold Rupture");
            Assert.Equal("-75", uniqueItems.Get(coldRupture, "min2"));
            Assert.Equal("-70", uniqueItems.Get(coldRupture, "max2"));
            var boneBreak = uniqueItems.Rows.Single(row =>
                uniqueItems.Get(row, "index") == "Bone Break");
            Assert.Equal("-12", uniqueItems.Get(boneBreak, "min2"));
            Assert.Equal("-10", uniqueItems.Get(boneBreak, "max2"));

            var mechanists = magicPrefixes.Rows.Single(row =>
                magicPrefixes.Get(row, "Name") == "Mechanist's");
            Assert.Equal("2", magicPrefixes.Get(mechanists, "mod1min"));
            Assert.Equal("2", magicPrefixes.Get(mechanists, "mod1max"));
            var colossus = autoMagic.Rows.Single(row =>
                autoMagic.Get(row, "Name") == "of the Colossus");
            Assert.Equal("59", autoMagic.Get(colossus, "mod1min"));
            Assert.Equal("60", autoMagic.Get(colossus, "mod1max"));

            Assert.Equal(29, qualityItems.Rows.Count);
            var superiorRanges = new Dictionary<string, (string Minimum, string Maximum)>
            {
                ["att"] = ("3", "3"),
                ["dmg%"] = ("14", "15"),
                ["ac%"] = ("14", "15"),
                ["dur%"] = ("15", "15")
            };
            Assert.All(qualityItems.Rows, row =>
            {
                for (var slot = 1; slot <= 2; slot++)
                {
                    var property = qualityItems.Get(row, $"mod{slot}code");
                    if (property.Length == 0)
                    {
                        continue;
                    }

                    var expected = superiorRanges[property];
                    Assert.Equal(expected.Minimum, qualityItems.Get(row, $"mod{slot}min"));
                    Assert.Equal(expected.Maximum, qualityItems.Get(row, $"mod{slot}max"));
                }
            });

            var superiorArmorRolls = qualityItems.Rows
                .Where(row => qualityItems.Get(row, "armor") == "1")
                .ToArray();
            Assert.Equal(10, superiorArmorRolls.Length);
            Assert.Equal(8, superiorArmorRolls.Count(row =>
                qualityItems.Get(row, "mod2code").Length != 0));

            var superiorWeaponRolls = qualityItems.Rows
                .Where(row => qualityItems.Get(row, "weapon") == "1")
                .ToArray();
            Assert.Equal(20, superiorWeaponRolls.Length);
            Assert.Equal(17, superiorWeaponRolls.Count(row =>
                qualityItems.Get(row, "mod2code").Length != 0));
            Assert.Equal(16, superiorWeaponRolls.Count(row =>
                qualityItems.Get(row, "mod1code") == "dmg%" ||
                qualityItems.Get(row, "mod2code") == "dmg%"));

            var bloodHelm = cube.Rows.Single(row => cube.Get(row, "description") ==
                "1 Magic Helm + 1 Jewel + 1 Ral Rune + 1 Perfect Ruby -> Blood Helm");
            Assert.Equal("3", cube.Get(bloodHelm, "mod 1 min"));
            Assert.Equal("3", cube.Get(bloodHelm, "mod 1 max"));

            var selectors = misc.Rows
                .Where(row => SelectorCode().IsMatch(misc.Get(row, "code")))
                .ToArray();
            Assert.Equal(JmSupplyModPackage.Manifest.SelectorCount, selectors.Length);
            Assert.Equal(
                selectors.Length,
                selectors.Select(row => misc.Get(row, "code"))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .Count());

            var selectorCodes = selectors
                .Select(row => misc.Get(row, "code"))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            var uniqueSwordSelectorCodes = cube.Rows
                .Where(row => cube.Get(row, "description").StartsWith(
                    "JM unique-sword next ",
                    StringComparison.Ordinal))
                .Select(row => cube.Get(row, "input 1"))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            Assert.NotEmpty(uniqueSwordSelectorCodes);
            var recipes = cube.Rows
                .Where(row => selectorCodes.Contains(cube.Get(row, "input 1")))
                .ToArray();
            Assert.Equal(selectors.Length * 3, recipes.Length);

            var uniqueNames = uniqueItems.Rows
                .Select(row => uniqueItems.Get(row, "index"))
                .Where(name => name.Length != 0)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            var rainbowFacetVariants = uniqueItems.Rows
                .Where(row => uniqueItems.Get(row, "index").StartsWith(
                    "JM Rainbow Facet ",
                    StringComparison.Ordinal))
                .ToArray();
            Assert.Equal(8, rainbowFacetVariants.Length);
            Assert.Equal(
                8,
                rainbowFacetVariants.Select(row => uniqueItems.Get(row, "index"))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .Count());
            var rainbowFacetRecipes = cube.Rows
                .Where(row => cube.Get(row, "output").StartsWith(
                    "JM Rainbow Facet ",
                    StringComparison.Ordinal))
                .ToArray();
            Assert.Equal(8, rainbowFacetRecipes.Length);
            Assert.All(rainbowFacetVariants, variant => Assert.Contains(
                rainbowFacetRecipes,
                recipe => cube.Get(recipe, "output") == uniqueItems.Get(variant, "index")));
            var setNames = setItems.Rows
                .Select(row => setItems.Get(row, "index"))
                .Where(name => name.Length != 0)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var recipe in recipes)
            {
                Assert.Equal("1", cube.Get(recipe, "enabled"));
                Assert.Equal("100", cube.Get(recipe, "version"));
                Assert.Equal("2", cube.Get(recipe, "numinputs"));
                Assert.Contains(cube.Get(recipe, "input 1"), selectorCodes);

                var control = cube.Get(recipe, "input 2");
                var output = cube.Get(recipe, "output");
                if (control is "tsc" or "isc")
                {
                    Assert.Contains(output, selectorCodes);
                    Assert.Equal(control, cube.Get(recipe, "output b"));
                }
                else
                {
                    Assert.Equal("key", control);
                    Assert.True(
                        uniqueNames.Contains(output) || setNames.Contains(output),
                        $"Exact cube output '{output}' is not a unique or set row key.");
                    Assert.Equal(cube.Get(recipe, "input 1"), cube.Get(recipe, "output b"));
                    Assert.Equal("key", cube.Get(recipe, "output c"));
                }
            }

            var baseSelectors = misc.Rows
                .Where(row => BaseSelectorCode().IsMatch(misc.Get(row, "code")))
                .ToArray();
            var materialSelectors = misc.Rows
                .Where(row => MaterialSelectorCode().IsMatch(misc.Get(row, "code")))
                .ToArray();
            var charmSelectors = misc.Rows
                .Where(row => CharmSelectorCode().IsMatch(misc.Get(row, "code")))
                .ToArray();
            var workstoneTokens = misc.Rows
                .Where(row => misc.Get(row, "name").StartsWith(
                    "JM control token ",
                    StringComparison.Ordinal))
                .ToArray();
            Assert.Equal(JmSupplyModPackage.Manifest.BaseSelectorCount, baseSelectors.Length);
            Assert.Equal(JmSupplyModPackage.Manifest.MaterialSelectorCount, materialSelectors.Length);
            Assert.Equal(JmSupplyModPackage.Manifest.CharmSelectorCount, charmSelectors.Length);
            Assert.Equal(13, workstoneTokens.Length);

            var allSelectors = selectors.Concat(baseSelectors).Concat(materialSelectors)
                .Concat(charmSelectors)
                .ToArray();
            var socketTokens = misc.Rows
                .Where(row => misc.Get(row, "name").StartsWith(
                    "JM socket token ",
                    StringComparison.Ordinal))
                .OrderBy(row => int.Parse(misc.Get(row, "name")["JM socket token ".Length..]))
                .ToArray();
            var controlTokens = workstoneTokens.Concat(socketTokens).ToArray();
            Assert.Equal(JmSupplyModPackage.Manifest.ControlTokenCount, controlTokens.Length);
            var allLocalizedItems = allSelectors.Concat(controlTokens).ToArray();
            Assert.Equal(JmSupplyModPackage.Manifest.CustomItemCount, allLocalizedItems.Length);
            using var itemNameStrings = System.Text.Json.JsonDocument.Parse(
                await File.ReadAllTextAsync(Path.Combine(
                    temporaryDirectory,
                    "data",
                    "local",
                    "lng",
                    "strings",
                    "item-names.json")));
            var allItemNameStrings = itemNameStrings.RootElement.EnumerateArray().ToArray();
            using var uiStrings = System.Text.Json.JsonDocument.Parse(
                await File.ReadAllTextAsync(Path.Combine(
                    temporaryDirectory,
                    "data",
                    "local",
                    "lng",
                    "strings",
                    "ui.json")));
            var hostLecture = uiStrings.RootElement.EnumerateArray().Single(entry =>
                entry.GetProperty("Key").GetString() == "strCreateServerLecture");
            Assert.Equal(
                "정만서버 런처에서 호스트를 시작한 뒤 '게임 호스트'를 누르세요. " +
                "친구는 런처가 안내한 주소로 참가합니다.",
                hostLecture.GetProperty("koKR").GetString());
            var joinLecture = uiStrings.RootElement.EnumerateArray().Single(entry =>
                entry.GetProperty("Key").GetString() == "strJoinServerLecture");
            Assert.Equal(
                "정만서버 런처에서 방 참가를 시작한 뒤 '게임 참가'를 누르세요. " +
                "주소에는 런처가 안내한 127.0.0.1을 입력합니다.",
                joinLecture.GetProperty("koKR").GetString());
            var localizedSelectors = allItemNameStrings
                .Where(entry => entry.GetProperty("id").GetInt32() >=
                                InGameSupplyModBuilder.FirstCustomStringId)
                .ToArray();
            Assert.Equal(
                1_660 + allLocalizedItems.Length + rainbowFacetVariants.Length,
                allItemNameStrings.Length);
            Assert.Equal(
                allLocalizedItems.Length + rainbowFacetVariants.Length,
                localizedSelectors.Length);
            Assert.Equal(
                localizedSelectors.Length,
                localizedSelectors.Select(entry => entry.GetProperty("id").GetInt32()).Distinct().Count());
            Assert.Equal(
                localizedSelectors.Length,
                localizedSelectors.Select(entry => entry.GetProperty("Key").GetString())
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .Count());
            Assert.Equal(
                InGameSupplyModBuilder.FirstCustomStringId,
                localizedSelectors.Min(entry => entry.GetProperty("id").GetInt32()));
            Assert.Equal(
                InGameSupplyModBuilder.FirstCustomStringId + localizedSelectors.Length - 1,
                localizedSelectors.Max(entry => entry.GetProperty("id").GetInt32()));
            var localizedKeys = localizedSelectors
                .Select(entry => entry.GetProperty("Key").GetString()!)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            Assert.All(allSelectors, row =>
            {
                var code = misc.Get(row, "code");
                var nameStringKey = $"jm_selector_{code}";
                Assert.Equal(nameStringKey, misc.Get(row, "namestr"));
                Assert.Equal("torc", misc.Get(row, "type"));
                Assert.Contains(nameStringKey, localizedKeys);
            });
            Assert.All(localizedSelectors, entry =>
            {
                Assert.False(string.IsNullOrWhiteSpace(entry.GetProperty("enUS").GetString()));
                Assert.False(string.IsNullOrWhiteSpace(entry.GetProperty("koKR").GetString()));
            });
            Assert.DoesNotContain(localizedSelectors.Where(entry =>
                entry.GetProperty("Key").GetString()!.StartsWith(
                    "jm_selector_",
                    StringComparison.Ordinal)), entry =>
            {
                var koreanName = entry.GetProperty("koKR").GetString()!;
                var separatorIndex = koreanName.IndexOf(": ", StringComparison.Ordinal);
                return separatorIndex < 0 ||
                       koreanName[(separatorIndex + 2)..].Any(character =>
                           character is >= 'A' and <= 'Z' or >= 'a' and <= 'z');
            });
            Assert.Equal(
                Enumerable.Range(1, 6).Select(value => $"{value}소켓 작업석").ToArray(),
                socketTokens.Select(token => localizedSelectors.Single(entry =>
                        entry.GetProperty("Key").GetString() == misc.Get(token, "namestr"))
                    .GetProperty("koKR").GetString()).ToArray());
            Assert.Equal(
                new[]
                {
                    "무지개 자락 (번개·사망 시)",
                    "무지개 자락 (냉기·사망 시)",
                    "무지개 자락 (화염·사망 시)",
                    "무지개 자락 (독·사망 시)",
                    "무지개 자락 (번개·레벨 상승 시)",
                    "무지개 자락 (냉기·레벨 상승 시)",
                    "무지개 자락 (화염·레벨 상승 시)",
                    "무지개 자락 (독·레벨 상승 시)"
                },
                rainbowFacetVariants.Select(variant => localizedSelectors.Single(entry =>
                        entry.GetProperty("Key").GetString() == uniqueItems.Get(variant, "index"))
                    .GetProperty("koKR").GetString()).ToArray());
            var firstAkaraSelectorCode = cube.Rows.Single(row =>
                cube.Get(row, "description").StartsWith(
                    "JM set-weapons create ",
                    StringComparison.Ordinal) &&
                cube.Get(row, "output") == "Aldur's Gauntlet");
            var firstAkaraSelector = localizedSelectors.Single(entry =>
                entry.GetProperty("Key").GetString() ==
                $"jm_selector_{cube.Get(firstAkaraSelectorCode, "input 1")}");
            Assert.Equal(
                "Set selector: Aldur's Rhythm",
                firstAkaraSelector.GetProperty("enUS").GetString());
            Assert.Equal(
                "세트 선택: 알두르의 운율",
                firstAkaraSelector.GetProperty("koKR").GetString());
            Assert.Contains(allItemNameStrings, entry =>
                entry.GetProperty("Key").GetString() == "isc" &&
                entry.GetProperty("koKR").GetString() == "감별의 두루마리");
            var baseSwordSelector = localizedSelectors.Single(entry =>
                entry.GetProperty("Key").GetString() == "jm_selector_301");
            Assert.Equal(
                "베이스 검 선택: 에인션트 소드",
                baseSwordSelector.GetProperty("koKR").GetString());
            var gheedsSelectorRecipe = cube.Rows.Single(row =>
                cube.Get(row, "input 2") == "key" &&
                cube.Get(row, "output") == "Gheed's Fortune");
            var gheedsSelector = localizedSelectors.Single(entry =>
                entry.GetProperty("Key").GetString() ==
                $"jm_selector_{cube.Get(gheedsSelectorRecipe, "input 1")}");
            Assert.Equal(
                "유니크 선택: 기드의 행운",
                gheedsSelector.GetProperty("koKR").GetString());
            var elRuneSelectorRecipe = cube.Rows.Single(row =>
                cube.Get(row, "description").StartsWith(
                    "JM materials-runes create ",
                    StringComparison.Ordinal) &&
                cube.Get(row, "output") == "r01");
            var elRuneSelector = localizedSelectors.Single(entry =>
                entry.GetProperty("Key").GetString() ==
                $"jm_selector_{cube.Get(elRuneSelectorRecipe, "input 1")}");
            Assert.Equal(
                "재료 선택: 엘 룬",
                elRuneSelector.GetProperty("koKR").GetString());
            var sapphireSelectorRecipe = cube.Rows.Single(row =>
                cube.Get(row, "description").StartsWith(
                    "JM materials-gems create ",
                    StringComparison.Ordinal) &&
                cube.Get(row, "output") == "gsb");
            var sapphireSelector = localizedSelectors.Single(entry =>
                entry.GetProperty("Key").GetString() ==
                $"jm_selector_{cube.Get(sapphireSelectorRecipe, "input 1")}");
            Assert.Equal(
                "재료 선택: 사파이어",
                sapphireSelector.GetProperty("koKR").GetString());

            using var hdItems = System.Text.Json.JsonDocument.Parse(
                await File.ReadAllTextAsync(Path.Combine(
                    temporaryDirectory,
                    "data",
                    "hd",
                    "items",
                    "items.json")));
            var hdItemAssets = hdItems.RootElement.EnumerateArray()
                .SelectMany(entry => entry.EnumerateObject())
                .ToDictionary(
                    mapping => mapping.Name,
                    mapping => mapping.Value.GetProperty("asset").GetString()!,
                    StringComparer.OrdinalIgnoreCase);
            Assert.All(
                allSelectors,
                row => Assert.Contains(misc.Get(row, "code"), hdItemAssets.Keys));

            var baseSelectorCodes = baseSelectors
                .Select(row => misc.Get(row, "code"))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            var materialSelectorCodes = materialSelectors
                .Select(row => misc.Get(row, "code"))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            Assert.DoesNotContain(
                cube.Rows,
                row => cube.Get(row, "description").StartsWith(
                           "JM materials-charms create ",
                           StringComparison.Ordinal) &&
                       cube.Get(row, "output") == "cm3");

            var expectedBaseCategoryCounts = new Dictionary<string, int>(StringComparer.Ordinal)
            {
                ["base-sword"] = 42,
                ["base-axe"] = 36,
                ["base-blunt"] = 33,
                ["base-bow"] = 36,
                ["base-pole"] = 48,
                ["base-dagger"] = 18,
                ["base-caster"] = 42,
                ["base-class-weapon"] = 36,
                ["base-body"] = 45,
                ["base-helm"] = 28,
                ["base-shield"] = 24,
                ["base-gloves"] = 15,
                ["base-boots"] = 15,
                ["base-belts"] = 15,
                ["base-class-armor"] = 75
            };
            using var catalog = System.Text.Json.JsonDocument.Parse(
                await File.ReadAllTextAsync(Path.Combine(
                    temporaryDirectory,
                    "jm-supply-catalog.json")));
            var catalogCategories = catalog.RootElement
                .GetProperty("Categories")
                .EnumerateArray()
                .ToArray();
            Assert.Equal(55, catalogCategories.Length);
            var charmAndJewelCategory = catalogCategories.Single(category =>
                category.GetProperty("Key").GetString() == "materials-charms");
            Assert.Equal(3, charmAndJewelCategory.GetProperty("ItemCount").GetInt32());
            Assert.Equal("Jewel", charmAndJewelCategory.GetProperty("FirstItemName").GetString());
            Assert.All(
                catalogCategories,
                category => Assert.Equal(
                    InGameSupplyModBuilder.SelectorVendor,
                    category.GetProperty("Vendor").GetString()));

            var nonSelectorVendors = new[]
            {
                "Charsi", "Gheed", "Fara", "Lysander", "Drognan", "Hratli", "Alkor",
                "Ormus", "Elzix", "Asheara", "Cain", "Halbu", "Malah", "Larzuk",
                "Anya", "Jamella"
            };
            Assert.All(allSelectors, selector =>
            {
                Assert.All(nonSelectorVendors, vendor =>
                {
                    Assert.Equal(string.Empty, misc.Get(selector, $"{vendor}Min"));
                    Assert.Equal(string.Empty, misc.Get(selector, $"{vendor}Max"));
                });
            });
            var akaraStockedSelectors = allSelectors
                .Where(row => misc.Get(row, "AkaraMin") == "1")
                .ToArray();
            Assert.Equal(catalogCategories.Length, akaraStockedSelectors.Length);
            Assert.All(
                akaraStockedSelectors,
                row => Assert.Equal("1", misc.Get(row, "AkaraMax")));

            foreach (var category in catalogCategories)
            {
                var categoryKey = category.GetProperty("Key").GetString()!;
                var categoryNavigationRows = cube.Rows
                    .Where(row => cube.Get(row, "description").StartsWith(
                        $"JM {categoryKey} next ",
                        StringComparison.Ordinal))
                    .ToArray();
                Assert.Equal(category.GetProperty("ItemCount").GetInt32(), categoryNavigationRows.Length);
                var stockedNavigationRows = categoryNavigationRows
                    .Where(navigationRow =>
                    {
                        var selectorCode = cube.Get(navigationRow, "input 1");
                        var selector = allSelectors.Single(
                            row => misc.Get(row, "code") == selectorCode);
                        return misc.Get(selector, "AkaraMin") == "1";
                    })
                    .ToArray();
                Assert.Single(stockedNavigationRows);
                Assert.Equal(
                    cube.Get(categoryNavigationRows[0], "input 1"),
                    cube.Get(stockedNavigationRows[0], "input 1"));
            }

            var actualBaseCategoryCounts = catalogCategories
                .Where(entry => entry.GetProperty("Key").GetString()!
                    .StartsWith("base-", StringComparison.Ordinal))
                .ToDictionary(
                    entry => entry.GetProperty("Key").GetString()!,
                    entry => entry.GetProperty("ItemCount").GetInt32(),
                    StringComparer.Ordinal);
            Assert.Equal(expectedBaseCategoryCounts.Count, actualBaseCategoryCounts.Count);
            Assert.Equal(baseSelectors.Length, actualBaseCategoryCounts.Values.Sum());
            foreach (var expectedCategory in expectedBaseCategoryCounts)
            {
                Assert.True(
                    actualBaseCategoryCounts.TryGetValue(expectedCategory.Key, out var actualCount),
                    $"Missing base selector category '{expectedCategory.Key}'.");
                Assert.Equal(expectedCategory.Value, actualCount);

                var categoryNavigationRows = cube.Rows
                    .Where(row => cube.Get(row, "description").StartsWith(
                        $"JM {expectedCategory.Key} next ",
                        StringComparison.Ordinal))
                    .ToArray();
                Assert.Equal(expectedCategory.Value, categoryNavigationRows.Length);
                var categorySelectorCodes = categoryNavigationRows
                    .Select(row => cube.Get(row, "input 1"))
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);
                var stockedCategorySelectors = baseSelectors
                    .Where(row => categorySelectorCodes.Contains(misc.Get(row, "code")) &&
                                  misc.Get(row, "AkaraMin") == "1")
                    .Select(row => misc.Get(row, "code"))
                    .ToArray();
                Assert.Single(stockedCategorySelectors);
                Assert.Equal(
                    cube.Get(categoryNavigationRows[0], "input 1"),
                    stockedCategorySelectors[0]);
                Assert.All(categorySelectorCodes, selectorCode =>
                {
                    var nextRecipe = cube.Rows.Single(row =>
                        cube.Get(row, "input 1") == selectorCode &&
                        cube.Get(row, "input 2") == "tsc");
                    var previousRecipe = cube.Rows.Single(row =>
                        cube.Get(row, "input 1") == selectorCode &&
                        cube.Get(row, "input 2") == "isc");
                    Assert.Contains(cube.Get(nextRecipe, "output"), categorySelectorCodes);
                    Assert.Contains(cube.Get(previousRecipe, "output"), categorySelectorCodes);
                });
            }

            var legacySelector309 = cube.Rows.Single(row =>
                cube.Get(row, "input 1") == "309" &&
                cube.Get(row, "description") == "JM base-bow Normal 309");
            Assert.Equal("800", cube.Get(legacySelector309, "input 2"));
            Assert.Equal("\"8hx,nor\"", cube.Get(legacySelector309, "output"));

            Assert.All(selectors, row =>
            {
                Assert.Equal("1", misc.Get(row, "invwidth"));
                Assert.Equal("1", misc.Get(row, "invheight"));
                Assert.Equal(
                    uniqueSwordSelectorCodes.Contains(misc.Get(row, "code"))
                        ? InGameSupplyModBuilder.UniqueSwordSelectorAsset
                        : misc.Get(row, "code")[0] == '5'
                        ? "gem/perfect_topaz"
                        : "gem/perfect_emerald",
                    hdItemAssets[misc.Get(row, "code")]);
            });

            var visualTargetItems = armor.Rows.Select(row => (armor, row))
                .Concat(weapons.Rows.Select(row => (weapons, row)))
                .Concat(misc.Rows.Select(row => (misc, row)))
                .Where(item => item.Item1.Get(item.row, "code").Length != 0)
                .ToDictionary(
                    item => item.Item1.Get(item.row, "code"),
                    item => item,
                    StringComparer.OrdinalIgnoreCase);
            Assert.All(baseSelectors, selector =>
            {
                var selectorCode = misc.Get(selector, "code");
                Assert.Equal("1", misc.Get(selector, "invwidth"));
                Assert.Equal("1", misc.Get(selector, "invheight"));
                Assert.Equal("invgswe", misc.Get(selector, "invfile"));
                Assert.Equal("flpgsw", misc.Get(selector, "flippyfile"));
                Assert.Equal("gem/perfect_diamond", hdItemAssets[selectorCode]);
            });
            Assert.All(materialSelectors, selector =>
            {
                var selectorCode = misc.Get(selector, "code");
                var createRecipe = cube.Rows.Single(row =>
                    cube.Get(row, "input 1") == selectorCode &&
                    cube.Get(row, "input 2") == "key" &&
                    cube.Get(row, "description").Contains(" create ", StringComparison.Ordinal));
                var targetCode = cube.Get(createRecipe, "output")
                    .Trim('"')
                    .Split(',')[0];
                var target = visualTargetItems[targetCode];

                Assert.Equal(target.Item1.Get(target.row, "invwidth"), misc.Get(selector, "invwidth"));
                Assert.Equal(target.Item1.Get(target.row, "invheight"), misc.Get(selector, "invheight"));
                Assert.Equal(target.Item1.Get(target.row, "invfile"), misc.Get(selector, "invfile"));
                Assert.Equal(target.Item1.Get(target.row, "flippyfile"), misc.Get(selector, "flippyfile"));
                Assert.Equal(hdItemAssets[targetCode], hdItemAssets[selectorCode]);
            });

            var baseModeTokenCodes = workstoneTokens
                .Where(row => misc.Get(row, "name").StartsWith(
                    "JM control token base-mode-",
                    StringComparison.Ordinal))
                .Select(row => misc.Get(row, "code"))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            Assert.Equal(5, baseModeTokenCodes.Count);
            Assert.All(baseSelectors, selector =>
            {
                var selectorCode = misc.Get(selector, "code");
                var modeRecipes = cube.Rows
                    .Where(row =>
                        cube.Get(row, "input 1") == selectorCode &&
                        baseModeTokenCodes.Contains(cube.Get(row, "input 2")))
                    .ToArray();
                var normalRecipe = modeRecipes.Single(row =>
                    cube.Get(row, "description").Contains(" Normal ", StringComparison.Ordinal));
                var targetCode = cube.Get(normalRecipe, "output").Trim('"').Split(',')[0];
                var target = visualTargetItems[targetCode];
                var targetType = target.Item1.Get(target.row, "type");
                var supportsEthereal =
                    (ReferenceEquals(target.Item1, armor) || ReferenceEquals(target.Item1, weapons)) &&
                    targetType is not "bow" and not "xbow" and not "abow" &&
                    int.TryParse(target.Item1.Get(target.row, "durability"), out var durability) &&
                    durability > 0;

                Assert.Equal(supportsEthereal ? 5 : 3, modeRecipes.Length);
                Assert.All(modeRecipes, recipe =>
                {
                    Assert.Equal("99", cube.Get(recipe, "lvl"));
                    Assert.Equal(selectorCode, cube.Get(recipe, "output b"));
                    Assert.Equal(cube.Get(recipe, "input 2"), cube.Get(recipe, "output c"));
                });
                Assert.Contains(modeRecipes, row => cube.Get(row, "output") == $"\"{targetCode},nor\"");
                Assert.Contains(modeRecipes, row => cube.Get(row, "output") == $"\"{targetCode},hiq\"");
                Assert.Contains(modeRecipes, row => cube.Get(row, "output") == $"\"{targetCode},mag\"");
                Assert.Equal(
                    supportsEthereal ? 2 : 0,
                    modeRecipes.Count(row =>
                        cube.Get(row, "output").Contains(",eth", StringComparison.Ordinal)));
            });

            var baseSelectorRecipeCount = cube.Rows.Count(row =>
                baseSelectorCodes.Contains(cube.Get(row, "input 1")));
            var etherealBaseRecipeCount = cube.Rows.Count(row =>
                baseSelectorCodes.Contains(cube.Get(row, "input 1")) &&
                cube.Get(row, "output").Contains(",eth", StringComparison.Ordinal));
            Assert.Equal(baseSelectors.Length * 5 + etherealBaseRecipeCount, baseSelectorRecipeCount);
            Assert.Equal(0, etherealBaseRecipeCount % 2);
            Assert.Equal(
                materialSelectors.Length * 3,
                cube.Rows.Count(row => materialSelectorCodes.Contains(cube.Get(row, "input 1"))));

            var charmSelectorCodes = charmSelectors
                .Select(row => misc.Get(row, "code"))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            var charmRecipes = cube.Rows
                .Where(row => charmSelectorCodes.Contains(cube.Get(row, "input 1")))
                .ToArray();
            Assert.Equal(379, charmRecipes.Length);
            var charmCreationRecipes = charmRecipes
                .Where(row => cube.Get(row, "input 2") is not "tsc" and not "isc")
                .ToArray();
            Assert.Equal(287, charmCreationRecipes.Length);
            Assert.Equal(144, charmCreationRecipes.Count(row =>
                cube.Get(row, "output").StartsWith("\"cm3,mag,", StringComparison.Ordinal)));
            Assert.Equal(77, charmCreationRecipes.Count(row =>
                cube.Get(row, "output").StartsWith("\"cm1,mag,", StringComparison.Ordinal)));
            Assert.Equal(66, charmCreationRecipes.Count(row =>
                cube.Get(row, "output").StartsWith("\"cm2,mag,", StringComparison.Ordinal)));

            var skillTabParameters = new HashSet<int>();
            Assert.All(charmCreationRecipes, recipe =>
            {
                Assert.Equal("99", cube.Get(recipe, "lvl"));
                Assert.Equal(cube.Get(recipe, "input 1"), cube.Get(recipe, "output b"));
                Assert.Equal(cube.Get(recipe, "input 2"), cube.Get(recipe, "output c"));

                var outputParts = cube.Get(recipe, "output").Trim('"').Split(',');
                Assert.True(outputParts.Length is 3 or 4);
                Assert.Equal("mag", outputParts[1]);
                var expectedCharmType = outputParts[0] switch
                {
                    "cm1" => "scha",
                    "cm2" => "mcha",
                    "cm3" => "lcha",
                    _ => throw new Xunit.Sdk.XunitException($"Unexpected charm code '{outputParts[0]}'.")
                };
                var prefixId = int.Parse(outputParts[2]["pre=".Length..]);
                Assert.InRange(prefixId, 0, magicPrefixes.Rows.Count - 1);
                var prefix = magicPrefixes.Rows[prefixId];
                Assert.Equal("1", magicPrefixes.Get(prefix, "spawnable"));
                Assert.True(AffixSupportsType(magicPrefixes, prefix, expectedCharmType));

                var isRandomBonus = cube.Get(recipe, "description").Contains(
                    " random ",
                    StringComparison.Ordinal);
                Assert.Equal(isRandomBonus ? 3 : 4, outputParts.Length);
                if (outputParts.Length == 4)
                {
                    var suffixId = int.Parse(outputParts[3]["suf=".Length..]);
                    Assert.InRange(suffixId, 0, magicSuffixes.Rows.Count - 1);
                    var suffix = magicSuffixes.Rows[suffixId];
                    Assert.Equal("1", magicSuffixes.Get(suffix, "spawnable"));
                    Assert.True(AffixSupportsType(magicSuffixes, suffix, expectedCharmType));
                    Assert.False(
                        expectedCharmType != "scha" &&
                        magicSuffixes.Get(suffix, "mod1code") == "mag%",
                        "Magic-find suffix must stay small-charm-only.");
                }

                if (magicPrefixes.Get(prefix, "mod1code") == "skilltab")
                {
                    Assert.Equal("cm3", outputParts[0]);
                    Assert.Equal("1", magicPrefixes.Get(prefix, "mod1min"));
                    Assert.Equal("1", magicPrefixes.Get(prefix, "mod1max"));
                    skillTabParameters.Add(int.Parse(magicPrefixes.Get(prefix, "mod1param")));
                }
            });
            Assert.Equal(Enumerable.Range(0, 24), skillTabParameters.Order());
            Assert.Equal(
                Enumerable.Range(0, 8),
                skillTabParameters.Select(parameter => parameter / 3).Distinct().Order());
            Assert.All(Enumerable.Range(0, 8), classId => Assert.Equal(
                3,
                skillTabParameters.Count(parameter => parameter / 3 == classId)));

            Assert.All(allLocalizedItems, row =>
            {
                var itemCode = MakeItemCode(misc.Get(row, "code"));
                Assert.True(
                    JmD2ExternalData.Instance.GetItemIndex(itemCode, 0x69) != -1,
                    $"D2S parser does not recognize custom item '{misc.Get(row, "code")}'.");
            });

            var workbenchRecipes = cube.Rows
                .Where(row => cube.Get(row, "description").StartsWith(
                    "JM workbench ",
                    StringComparison.Ordinal))
                .ToArray();
            Assert.Equal(JmSupplyModPackage.Manifest.WorkbenchRecipeCount, workbenchRecipes.Length);
            Assert.Contains(workbenchRecipes, row => cube.Get(row, "output") == "\"useitem,rem\"");
            var socketRecipes = workbenchRecipes
                .Where(row => cube.Get(row, "description").Contains(" add ", StringComparison.Ordinal) &&
                              cube.Get(row, "description").EndsWith(" sockets", StringComparison.Ordinal))
                .ToArray();
            Assert.Equal(28, socketRecipes.Length);
            Assert.Equal(6, socketTokens.Length);
            Assert.All(socketTokens, token =>
            {
                Assert.Equal("0", misc.Get(token, "stackable"));
                Assert.Equal("1", misc.Get(token, "AkaraMin"));
                Assert.Equal("1", misc.Get(token, "AkaraMax"));
                Assert.Equal("1", misc.Get(token, "invwidth"));
                Assert.Equal("1", misc.Get(token, "invheight"));
            });
            var socketTokenCodes = socketTokens
                .Select(row => misc.Get(row, "code"))
                .ToArray();
            Assert.All(socketRecipes, row =>
                Assert.Contains(cube.Get(row, "input 2"), socketTokenCodes));
            Assert.Equal(
                socketTokenCodes,
                socketRecipes
                    .Where(row => cube.Get(row, "input 1") == "\"weap,nos\"" ||
                                  cube.Get(row, "input 1") == "\"weap,nor,nos\"")
                    .Select(row => cube.Get(row, "input 2"))
                    .ToArray());
            Assert.Equal(
                socketTokenCodes.Take(4).ToArray(),
                socketRecipes
                    .Where(row => cube.Get(row, "input 1") == "\"armo,nos\"" ||
                                  cube.Get(row, "input 1") == "\"armo,nor,nos\"")
                    .Select(row => cube.Get(row, "input 2"))
                    .ToArray());
            Assert.Equal(
                socketTokenCodes[1],
                cube.Get(
                    socketRecipes.Single(row => cube.Get(row, "input 1") == "\"weap,mag,nos\""),
                    "input 2"));
            Assert.Equal(
                socketTokenCodes[1],
                cube.Get(
                    socketRecipes.Single(row => cube.Get(row, "input 1") == "\"armo,mag,nos\""),
                    "input 2"));
            Assert.DoesNotContain(socketRecipes, row =>
                cube.Get(row, "input 2").Contains("key", StringComparison.OrdinalIgnoreCase));

            var quickCraftToken = workstoneTokens.Single(row =>
                misc.Get(row, "name") == "JM control token quick-craft");
            var quickCraftTokenCode = misc.Get(quickCraftToken, "code");
            var vanillaCraftRecipes = cube.Rows
                .Where(row =>
                    cube.Get(row, "enabled") == "1" &&
                    !cube.Get(row, "description").StartsWith("JM ", StringComparison.Ordinal) &&
                    cube.Get(row, "output").Contains("crf", StringComparison.Ordinal))
                .ToArray();
            var quickCraftRecipes = cube.Rows
                .Where(row => cube.Get(row, "description").StartsWith(
                    "JM quick craft: ",
                    StringComparison.Ordinal))
                .ToArray();
            Assert.Equal(InGameSupplyModBuilder.ExpectedVanillaCraftRecipeCount, vanillaCraftRecipes.Length);
            Assert.Equal(JmSupplyModPackage.Manifest.QuickCraftRecipeCount, quickCraftRecipes.Length);
            Assert.All(vanillaCraftRecipes, sourceRecipe =>
            {
                var quickRecipe = quickCraftRecipes.Single(row =>
                    cube.Get(row, "description") == $"JM quick craft: {cube.Get(sourceRecipe, "description")}");
                Assert.Equal("2", cube.Get(quickRecipe, "numinputs"));
                Assert.Equal(cube.Get(sourceRecipe, "input 1"), cube.Get(quickRecipe, "input 1"));
                Assert.Equal(quickCraftTokenCode, cube.Get(quickRecipe, "input 2"));
                Assert.Equal(quickCraftTokenCode, cube.Get(quickRecipe, "output b"));
                Assert.Equal(cube.Get(sourceRecipe, "output"), cube.Get(quickRecipe, "output"));
                Assert.Equal(cube.Get(sourceRecipe, "lvl"), cube.Get(quickRecipe, "lvl"));
                Assert.Equal(cube.Get(sourceRecipe, "plvl"), cube.Get(quickRecipe, "plvl"));
                Assert.Equal(cube.Get(sourceRecipe, "ilvl"), cube.Get(quickRecipe, "ilvl"));
                for (var modifier = 1; modifier <= 5; modifier++)
                {
                    Assert.Equal(
                        cube.Get(sourceRecipe, $"mod {modifier}"),
                        cube.Get(quickRecipe, $"mod {modifier}"));
                    Assert.Equal(
                        cube.Get(sourceRecipe, $"mod {modifier} param"),
                        cube.Get(quickRecipe, $"mod {modifier} param"));
                    Assert.Equal(
                        cube.Get(sourceRecipe, $"mod {modifier} min"),
                        cube.Get(quickRecipe, $"mod {modifier} min"));
                    Assert.Equal(
                        cube.Get(sourceRecipe, $"mod {modifier} max"),
                        cube.Get(quickRecipe, $"mod {modifier} max"));
                }
            });

            Assert.Equal("100", misc.Get(
                misc.Rows.Single(row => misc.Get(row, "code") == "tbk"),
                "maxstack"));
            Assert.Equal("100", misc.Get(
                misc.Rows.Single(row => misc.Get(row, "code") == "ibk"),
                "maxstack"));
            Assert.Equal("50", misc.Get(
                misc.Rows.Single(row => misc.Get(row, "code") == "key"),
                "maxstack"));
            Assert.All(armor.Rows, row => Assert.Equal("1", armor.Get(row, "ShowLevel")));
            Assert.All(
                weapons.Rows.Where(row => weapons.Get(row, "type") != "tpot"),
                row => Assert.Equal("1", weapons.Get(row, "ShowLevel")));

            using var lootFilter = System.Text.Json.JsonDocument.Parse(JmLootFilterProfile.GetJson());
            var lootFilterRules = lootFilter.RootElement.GetProperty("rules").EnumerateArray().ToArray();
            Assert.NotEmpty(lootFilterRules);
            Assert.DoesNotContain(lootFilterRules, rule =>
                rule.GetProperty("ruleType").GetString() == "hide" &&
                rule.GetProperty("enabled").GetBoolean());

            var gambleCodes = gamble.Rows
                .Select(row => gamble.Get(row, "code"))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            var baseItems = armor.Rows.Select(row => (armor, row))
                .Concat(weapons.Rows.Select(row => (weapons, row)))
                .Concat(misc.Rows.Select(row => (misc, row)))
                .Where(item => item.Item1.Get(item.row, "code").Length != 0)
                .ToDictionary(
                    item => item.Item1.Get(item.row, "code"),
                    item => item,
                    StringComparer.OrdinalIgnoreCase);
            var catalogCodes = uniqueItems.Rows
                .Where(row => IsCatalogUnique(uniqueItems, row))
                .Select(row => uniqueItems.Get(row, "code"))
                .Concat(setItems.Rows
                    .Where(row => IsCatalogSet(setItems, row))
                    .Select(row => setItems.Get(row, "item")))
                .Where(code => baseItems.TryGetValue(code, out var item) &&
                               string.IsNullOrWhiteSpace(item.Item1.Get(item.row, "quest")))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            var questCodes = baseItems
                .Where(item => !string.IsNullOrWhiteSpace(item.Value.Item1.Get(item.Value.row, "quest")))
                .Select(item => item.Key)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            var baseOutputs = cube.Rows
                .Where(row => baseSelectorCodes.Contains(cube.Get(row, "input 1")) &&
                              cube.Get(row, "input 2") is "key" or "yps")
                .Select(row => cube.Get(row, "output").Trim('"').Split(',')[0])
                .ToArray();
            Assert.DoesNotContain(baseOutputs, code => questCodes.Contains(code));

            Assert.DoesNotContain(gambleCodes, code => questCodes.Contains(code));
            foreach (var code in catalogCodes.Where(code => code is not "cm1" and not "cm2" and not "cm3"))
            {
                Assert.Contains(code, gambleCodes);
                var baseItem = baseItems[code];
                Assert.Equal(
                    InGameSupplyModBuilder.GambleCost.ToString(),
                    baseItem.Item1.Get(baseItem.row, "cost"));
                Assert.Equal(
                    InGameSupplyModBuilder.GambleCost.ToString(),
                    baseItem.Item1.Get(baseItem.row, "gamble cost"));

                if (baseItem.Item1.Columns.Contains("normcode", StringComparer.Ordinal))
                {
                    foreach (var familyCode in new[]
                             {
                                 baseItem.Item1.Get(baseItem.row, "normcode"),
                                 baseItem.Item1.Get(baseItem.row, "ubercode"),
                                 baseItem.Item1.Get(baseItem.row, "ultracode")
                             }
                                 .Select(value => value.Trim())
                                 .Where(value => value.Length is 3 or 4 &&
                                                 !value.Equals("xxx", StringComparison.OrdinalIgnoreCase)))
                    {
                        var familyItem = baseItems[familyCode];
                        Assert.Equal(
                            InGameSupplyModBuilder.GambleCost.ToString(),
                            familyItem.Item1.Get(familyItem.row, "cost"));
                        Assert.Equal(
                            InGameSupplyModBuilder.GambleCost.ToString(),
                            familyItem.Item1.Get(familyItem.row, "gamble cost"));
                    }
                }
            }

            Assert.All(difficulties.Rows, row =>
            {
                Assert.Equal(
                    InGameSupplyModBuilder.GambleUniqueWeight.ToString(),
                    difficulties.Get(row, "GambleUnique"));
                Assert.Equal(
                    InGameSupplyModBuilder.GambleSetWeight.ToString(),
                    difficulties.Get(row, "GambleSet"));
                Assert.Equal(
                    InGameSupplyModBuilder.GambleRareWeight.ToString(),
                    difficulties.Get(row, "GambleRare"));
                Assert.Equal(
                    100_000,
                    InGameSupplyModBuilder.GambleUniqueWeight +
                    InGameSupplyModBuilder.GambleSetWeight +
                    InGameSupplyModBuilder.GambleRareWeight +
                    InGameSupplyModBuilder.GambleMagicWeight);
                Assert.Equal(
                    InGameSupplyModBuilder.GambleMagicWeight,
                    100_000 -
                    InGameSupplyModBuilder.GambleUniqueWeight -
                    InGameSupplyModBuilder.GambleSetWeight -
                    InGameSupplyModBuilder.GambleRareWeight);
            });

            var valkyries = magicPrefixes.Rows.Single(row =>
                magicPrefixes.Get(row, "Name") == "Valkyrie's" &&
                magicPrefixes.Get(row, "level") == "90" &&
                magicPrefixes.Get(row, "frequency") != "0");
            Assert.Equal("30", magicPrefixes.Get(valkyries, "frequency"));

            var vita = magicSuffixes.Rows.Single(row =>
                magicSuffixes.Get(row, "Name") == "of Vita" &&
                magicSuffixes.Get(row, "level") == "91" &&
                magicSuffixes.Get(row, "frequency") != "0");
            Assert.Equal(
                (60 * InGameSupplyModBuilder.PreferredCharmAffixWeightMultiplier).ToString(),
                magicSuffixes.Get(vita, "frequency"));

            var maras = uniqueItems.Rows.Single(row =>
                uniqueItems.Get(row, "index") == "Mara's Kaleidoscope");
            var marasResistanceSlot = Enumerable.Range(1, 12).Single(index =>
                uniqueItems.Get(maras, $"prop{index}") == "res-all");
            Assert.Equal("28", uniqueItems.Get(maras, $"min{marasResistanceSlot}"));
            Assert.Equal("30", uniqueItems.Get(maras, $"max{marasResistanceSlot}"));

            var vipermagi = uniqueItems.Rows.Single(row =>
                uniqueItems.Get(row, "index") == "Skin of the Vipermagi");
            var vipermagiResistanceSlot = Enumerable.Range(1, 12).Single(index =>
                uniqueItems.Get(vipermagi, $"prop{index}") == "res-all");
            Assert.Equal("32", uniqueItems.Get(vipermagi, $"min{vipermagiResistanceSlot}"));
            Assert.Equal("35", uniqueItems.Get(vipermagi, $"max{vipermagiResistanceSlot}"));

            var stormrider = uniqueItems.Rows.Single(row =>
                uniqueItems.Get(row, "index") == "Stormrider");
            var lightningDamageSlot = Enumerable.Range(1, 12).Single(index =>
                uniqueItems.Get(stormrider, $"prop{index}") == "dmg-ltng");
            Assert.Equal("151", uniqueItems.Get(stormrider, $"min{lightningDamageSlot}"));
            Assert.Equal("200", uniqueItems.Get(stormrider, $"max{lightningDamageSlot}"));

            var natalyasTotem = setItems.Rows.Single(row =>
                setItems.Get(row, "index") == "Natalya's Totem");
            var defenseSlot = Enumerable.Range(1, 9).Single(index =>
                setItems.Get(natalyasTotem, $"prop{index}") == "ac");
            Assert.Equal("165", setItems.Get(natalyasTotem, $"min{defenseSlot}"));
            Assert.Equal("175", setItems.Get(natalyasTotem, $"max{defenseSlot}"));

            var gheedsFortune = uniqueItems.Rows.Single(row =>
                uniqueItems.Get(row, "index") == "Gheed's Fortune");
            var discountPropertyIndex = Enumerable.Range(1, 12).Single(index =>
                uniqueItems.Get(gheedsFortune, $"prop{index}") == "cheap");
            Assert.Equal(
                InGameSupplyModBuilder.VendorPriceReductionPercent.ToString(),
                uniqueItems.Get(gheedsFortune, $"min{discountPropertyIndex}"));
            Assert.Equal(
                InGameSupplyModBuilder.VendorPriceReductionPercent.ToString(),
                uniqueItems.Get(gheedsFortune, $"max{discountPropertyIndex}"));
            var singleCarryUniqueCharms = new Dictionary<string, string>
            {
                ["Gheed's Fortune"] = "1001",
                ["Annihilus"] = "1002",
                ["Hellfire Torch"] = "1003"
            };
            Assert.All(singleCarryUniqueCharms, charm =>
            {
                var row = uniqueItems.Rows.Single(item =>
                    uniqueItems.Get(item, "index") == charm.Key);
                Assert.Equal(string.Empty, uniqueItems.Get(row, "nolimit"));
                Assert.Equal(charm.Value, uniqueItems.Get(row, "carry1"));
            });
            var hellfireTorch = uniqueItems.Rows.Single(row =>
                uniqueItems.Get(row, "index") == "Hellfire Torch");
            var randomClassSkillSlot = Enumerable.Range(1, 12).Single(index =>
                uniqueItems.Get(hellfireTorch, $"prop{index}") == "randclassskill");
            Assert.Equal("0", uniqueItems.Get(hellfireTorch, $"min{randomClassSkillSlot}"));
            Assert.Equal("7", uniqueItems.Get(hellfireTorch, $"max{randomClassSkillSlot}"));
            var ormusRobes = uniqueItems.Rows.Single(row =>
                uniqueItems.Get(row, "index") == "Ormus' Robes");
            var randomSkillSlot = Enumerable.Range(1, 12).Single(index =>
                uniqueItems.Get(ormusRobes, $"prop{index}") == "skill-rand");
            Assert.Equal("3", uniqueItems.Get(ormusRobes, $"par{randomSkillSlot}"));
            Assert.Equal("36", uniqueItems.Get(ormusRobes, $"min{randomSkillSlot}"));
            Assert.Equal("60", uniqueItems.Get(ormusRobes, $"max{randomSkillSlot}"));
            var gorefoot = uniqueItems.Rows.Single(row =>
                uniqueItems.Get(row, "index") == "Gorefoot");
            var cosmeticBloodSlot = Enumerable.Range(1, 12).Single(index =>
                uniqueItems.Get(gorefoot, $"prop{index}") == "bloody");
            Assert.Equal("3", uniqueItems.Get(gorefoot, $"min{cosmeticBloodSlot}"));
            Assert.Equal("5", uniqueItems.Get(gorefoot, $"max{cosmeticBloodSlot}"));
            Assert.All(
                uniqueItems.Rows.Where(row =>
                    IsCatalogUnique(uniqueItems, row) &&
                    catalogCodes.Contains(uniqueItems.Get(row, "code")) &&
                    !singleCarryUniqueCharms.ContainsKey(uniqueItems.Get(row, "index"))),
                row =>
                {
                    Assert.Equal("1", uniqueItems.Get(row, "nolimit"));
                    Assert.Equal("0", uniqueItems.Get(row, "carry1"));
                    Assert.True(ParseLevel(uniqueItems, row) <= 85);
                });
            Assert.All(
                setItems.Rows.Where(row =>
                    IsCatalogSet(setItems, row) &&
                    catalogCodes.Contains(setItems.Get(row, "item"))),
                row => Assert.True(ParseLevel(setItems, row) <= 85));
        }
        finally
        {
            if (Directory.Exists(temporaryDirectory))
            {
                Directory.Delete(temporaryDirectory, recursive: true);
            }
        }
    }

    [Fact]
    public async Task VerificationRejectsChangedPackageFile()
    {
        var temporaryDirectory = Path.Combine(
            Path.GetTempPath(),
            "jm-supply-tamper-test-" + Guid.NewGuid().ToString("N"));
        try
        {
            await JmSupplyModPackage.WriteToDirectoryAsync(temporaryDirectory);
            var catalogPath = Path.Combine(temporaryDirectory, "jm-supply-catalog.json");
            await File.AppendAllTextAsync(catalogPath, "changed");

            var verification = await JmSupplyModPackage.VerifyAsync(temporaryDirectory);

            Assert.False(verification.IsValid);
            Assert.Contains("was changed", verification.Message, StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(temporaryDirectory))
            {
                Directory.Delete(temporaryDirectory, recursive: true);
            }
        }
    }

    private static bool IsCatalogUnique(D2TsvTable table, string[] row)
    {
        var name = table.Get(row, "index");
        return name.Length != 0 &&
               table.Get(row, "code").Length != 0 &&
               table.Get(row, "disabled") != "1" &&
               table.Get(row, "spawnable") != "0" &&
               !name.StartsWith("Crafted ", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsCatalogSet(D2TsvTable table, string[] row) =>
        table.Get(row, "index").Length != 0 &&
        table.Get(row, "item").Length != 0 &&
        table.Get(row, "disabled") != "1" &&
        table.Get(row, "spawnable") != "0";

    private static int ParseLevel(D2TsvTable table, string[] row) =>
        int.TryParse(table.Get(row, "lvl"), out var level) ? level : 0;

    private static bool AffixSupportsType(D2TsvTable table, string[] row, string itemType) =>
        Enumerable.Range(1, 7).Any(index => table.Get(row, $"itype{index}") == itemType);

    private static uint MakeItemCode(string value)
    {
        Span<char> code = stackalloc char[4];
        code.Fill(' ');
        value.AsSpan().CopyTo(code);
        return code[0] |
               (uint)code[1] << 8 |
               (uint)code[2] << 16 |
               (uint)code[3] << 24;
    }

    [GeneratedRegex("^[15][0-9a-z]{2}$", RegexOptions.CultureInvariant)]
    private static partial Regex SelectorCode();

    [GeneratedRegex("^3[0-9a-z]{2}$", RegexOptions.CultureInvariant)]
    private static partial Regex BaseSelectorCode();

    [GeneratedRegex("^4[0-9a-z]{2}$", RegexOptions.CultureInvariant)]
    private static partial Regex MaterialSelectorCode();

    [GeneratedRegex("^7[0-9a-z]{2}$", RegexOptions.CultureInvariant)]
    private static partial Regex CharmSelectorCode();
}
