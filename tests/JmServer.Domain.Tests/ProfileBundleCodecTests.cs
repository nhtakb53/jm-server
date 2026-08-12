using JmServer.Domain;

namespace JmServer.Domain.Tests;

public sealed class ProfileBundleCodecTests
{
    [Fact]
    public void CustomKeyBindingsAreManagedAsGlobalControls()
    {
        Assert.True(ProfileSavePolicy.IsManagedFileName("Custom.key"));
        Assert.True(ProfileSavePolicy.IsCharacterCompanion("Custom.key"));
        Assert.True(ProfileSavePolicy.IsCharacterControl("Custom.key"));
        Assert.True(ProfileSavePolicy.IsGlobalControl("Custom.key"));
    }

    [Theory]
    [InlineData("Hero.key", "Hero", true)]
    [InlineData("Hero0.keyo", "Hero", true)]
    [InlineData("Hero12.ctlo", "Hero", true)]
    [InlineData("Hero0.ma0", "Hero", false)]
    [InlineData("Heroine0.keyo", "Hero", false)]
    [InlineData("Hero0.keyo", "Hero0", false)]
    [InlineData("Hero00.keyo", "Hero0", true)]
    public void BelongsToCharacter_HandlesOfflineControlSlotSuffix(
        string fileName,
        string characterName,
        bool expected)
    {
        Assert.Equal(expected, ProfileSavePolicy.BelongsToCharacter(fileName, characterName));
    }

    [Theory]
    [InlineData("Hero.ma0")]
    [InlineData("Hero.ma1")]
    [InlineData("Hero.ma2")]
    [InlineData("Hero.ma3")]
    [InlineData("Hero.map")]
    [InlineData("Hero.keyo")]
    [InlineData("Hero.ctlo")]
    public void AutomapCompanionFilesAreManaged(string fileName)
    {
        Assert.True(ProfileSavePolicy.IsManagedFileName(fileName));
        Assert.True(ProfileSavePolicy.IsCharacterCompanion(fileName));
    }

    [Fact]
    public void SelectPreferredSoftcoreSharedStash_PrefersModernFileWhenLegacyAlsoExists()
    {
        var legacy = new ProfileFile("SharedStashSoftCoreV2.d2i", [1]);
        var modern = new ProfileFile(
            ProfileSavePolicy.PreferredSoftcoreSharedStashName,
            [2]);

        var selected = ProfileSavePolicy.SelectPreferredSoftcoreSharedStash(
            [legacy, modern]);

        Assert.Same(modern, selected);
    }

    [Fact]
    public void SelectPreferredSoftcoreSharedStash_FallsBackToLegacyFile()
    {
        var legacy = new ProfileFile("SharedStashSoftCoreV2.d2i", [1]);

        var selected = ProfileSavePolicy.SelectPreferredSoftcoreSharedStash([legacy]);

        Assert.Same(legacy, selected);
    }

    [Fact]
    public void RoundTrip_PreservesCharacterAndSharedStashFiles()
    {
        var source = new[]
        {
            new ProfileFile("악콩이.d2s", [1, 2, 3]),
            new ProfileFile("악콩이.key", [4, 5]),
            new ProfileFile("ModernSharedStashSoftCoreV2.d2i", [6])
        };

        var result = ProfileBundleCodec.Decode(ProfileBundleCodec.Encode(source));

        Assert.Equal(3, result.Count);
        Assert.Equal([1, 2, 3], result.Single(file => file.RelativePath == "악콩이.d2s").Data);
        Assert.Equal([6], result.Single(file => file.RelativePath.EndsWith(".d2i", StringComparison.Ordinal)).Data);
    }

    [Fact]
    public void Encode_RejectsPathTraversal()
    {
        Assert.Throws<InvalidDataException>(() =>
            ProfileBundleCodec.Encode([new ProfileFile("..\\outside.d2s", [1])]));
    }

    [Fact]
    public void Encode_RejectsCaseInsensitiveDuplicatePaths()
    {
        Assert.Throws<InvalidDataException>(() =>
            ProfileBundleCodec.Encode(
            [
                new ProfileFile("Hero.d2s", [1]),
                new ProfileFile("hero.D2S", [2])
            ]));
    }

    [Fact]
    public void Encode_RejectsUnmanagedFiles()
    {
        Assert.Throws<InvalidDataException>(() =>
            ProfileBundleCodec.Encode([new ProfileFile("settings.json", [1])]));
    }
}
