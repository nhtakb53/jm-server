using JmServer.Domain;
using JmServer.GameIntegration;

namespace JmServer.GameIntegration.Tests;

public sealed class ProfileDirectoryManagerTests
{
    [Fact]
    public async Task InstallCheckout_PreservesLocalControlsForRegisteredCharacter()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            "jm-profile-control-merge-test-" + Guid.NewGuid().ToString("N"));
        var saveDirectory = Path.Combine(root, "save");
        var quarantineRoot = Path.Combine(root, "quarantine");
        Directory.CreateDirectory(saveDirectory);
        try
        {
            await File.WriteAllBytesAsync(Path.Combine(saveDirectory, "Hero0.keyo"), [9, 8]);
            await File.WriteAllBytesAsync(Path.Combine(saveDirectory, "Other0.keyo"), [7]);
            var bundle = ProfileBundleCodec.Encode(
            [
                new ProfileFile("Hero.d2s", [1]),
                new ProfileFile("Hero0.keyo", [2])
            ]);

            var installed = await ProfileDirectoryManager.InstallCheckoutAsync(
                bundle,
                saveDirectory,
                quarantineRoot);

            Assert.Equal([9, 8], await File.ReadAllBytesAsync(
                Path.Combine(saveDirectory, "Hero0.keyo")));
            Assert.False(File.Exists(Path.Combine(saveDirectory, "Other0.keyo")));
            Assert.NotNull(installed.QuarantineDirectory);
            Assert.True(File.Exists(Path.Combine(installed.QuarantineDirectory!, "Other0.keyo")));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task InstallCheckout_PreservesGlobalCustomKeyBindings()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            "jm-profile-custom-key-test-" + Guid.NewGuid().ToString("N"));
        var saveDirectory = Path.Combine(root, "save");
        var quarantineRoot = Path.Combine(root, "quarantine");
        Directory.CreateDirectory(saveDirectory);
        try
        {
            await File.WriteAllBytesAsync(Path.Combine(saveDirectory, "Custom.key"), [9, 8]);
            var bundle = ProfileBundleCodec.Encode(
            [
                new ProfileFile("Hero.d2s", [1]),
                new ProfileFile("Custom.key", [2])
            ]);

            _ = await ProfileDirectoryManager.InstallCheckoutAsync(
                bundle,
                saveDirectory,
                quarantineRoot);

            Assert.Equal([9, 8], await File.ReadAllBytesAsync(
                Path.Combine(saveDirectory, "Custom.key")));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void CollectForCheckin_PreservesNumberedOfflineControlFiles()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            "jm-profile-controls-test-" + Guid.NewGuid().ToString("N"));
        var saveDirectory = Path.Combine(root, "save");
        var quarantineRoot = Path.Combine(root, "quarantine");
        Directory.CreateDirectory(saveDirectory);
        try
        {
            File.WriteAllBytes(Path.Combine(saveDirectory, "Hero.d2s"), [1]);
            File.WriteAllBytes(Path.Combine(saveDirectory, "Hero.key"), [2]);
            File.WriteAllBytes(Path.Combine(saveDirectory, "Hero.ctl"), [3]);
            File.WriteAllBytes(Path.Combine(saveDirectory, "Hero0.keyo"), [4]);
            File.WriteAllBytes(Path.Combine(saveDirectory, "Hero0.ctlo"), [5]);
            File.WriteAllBytes(Path.Combine(saveDirectory, "Hero12.keyo"), [6]);
            File.WriteAllBytes(Path.Combine(saveDirectory, "Custom.key"), [7]);

            var collected = ProfileDirectoryManager.CollectForCheckin(
                ["Hero"],
                saveDirectory,
                quarantineRoot);
            var files = ProfileBundleCodec.Decode(collected.BundleData);

            Assert.Null(collected.QuarantineDirectory);
            Assert.Contains(files, file => file.RelativePath == "Hero.key");
            Assert.Contains(files, file => file.RelativePath == "Hero.ctl");
            Assert.Contains(files, file => file.RelativePath == "Hero0.keyo");
            Assert.Contains(files, file => file.RelativePath == "Hero0.ctlo");
            Assert.Contains(files, file => file.RelativePath == "Hero12.keyo");
            Assert.Contains(files, file => file.RelativePath == "Custom.key");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void QuarantineManagedProfile_MovesManagedFilesAndAdditionalLeaseAtomically()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            "jm-profile-quarantine-test-" + Guid.NewGuid().ToString("N"));
        var saveDirectory = Path.Combine(root, "save");
        var quarantineRoot = Path.Combine(root, "quarantine");
        Directory.CreateDirectory(saveDirectory);
        try
        {
            var characterPath = Path.Combine(saveDirectory, "Hero.d2s");
            var stashPath = Path.Combine(saveDirectory, "ModernSharedStashSoftCoreV2.d2i");
            var leasePath = Path.Combine(saveDirectory, ".jmprofile-lease.json");
            var settingsPath = Path.Combine(saveDirectory, "Settings.json");
            File.WriteAllBytes(characterPath, [1, 2, 3]);
            File.WriteAllBytes(stashPath, [4, 5, 6]);
            File.WriteAllText(leasePath, "{}");
            File.WriteAllText(settingsPath, "{}");

            var quarantined = ProfileDirectoryManager.QuarantineManagedProfile(
                saveDirectory,
                quarantineRoot,
                [leasePath]);

            Assert.NotNull(quarantined);
            Assert.True(File.Exists(Path.Combine(quarantined!, "Hero.d2s")));
            Assert.True(File.Exists(Path.Combine(
                quarantined,
                "ModernSharedStashSoftCoreV2.d2i")));
            Assert.True(File.Exists(Path.Combine(quarantined, ".jmprofile-lease.json")));
            Assert.True(File.Exists(settingsPath));
            Assert.False(File.Exists(characterPath));
            Assert.False(File.Exists(stashPath));
            Assert.False(File.Exists(leasePath));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task InstallAndCollect_QuarantinesLocalCharactersAndPreservesNonSaveFiles()
    {
        var root = Path.Combine(Path.GetTempPath(), "jm-profile-test-" + Guid.NewGuid().ToString("N"));
        var saveDirectory = Path.Combine(root, "save");
        var quarantineRoot = Path.Combine(root, "quarantine");
        Directory.CreateDirectory(saveDirectory);
        try
        {
            await File.WriteAllBytesAsync(Path.Combine(saveDirectory, "로컬.d2s"), [9]);
            await File.WriteAllBytesAsync(
                Path.Combine(saveDirectory, "ModernSharedStashSoftCoreV2.d2i"),
                [8]);
            await File.WriteAllTextAsync(Path.Combine(saveDirectory, "lootfilter.txt"), "keep");
            var bundle = ProfileBundleCodec.Encode(
            [
                new ProfileFile("서버.d2s", [1, 2]),
                new ProfileFile("ModernSharedStashSoftCoreV2.d2i", [3])
            ]);

            var installed = await ProfileDirectoryManager.InstallCheckoutAsync(
                bundle,
                saveDirectory,
                quarantineRoot);

            Assert.Equal(["서버"], installed.RegisteredCharacterNames);
            Assert.NotNull(installed.QuarantineDirectory);
            Assert.True(File.Exists(Path.Combine(installed.QuarantineDirectory!, "로컬.d2s")));
            Assert.True(File.Exists(Path.Combine(installed.QuarantineDirectory!, "ModernSharedStashSoftCoreV2.d2i")));
            Assert.True(File.Exists(Path.Combine(saveDirectory, "lootfilter.txt")));
            Assert.Equal([1, 2], await File.ReadAllBytesAsync(Path.Combine(saveDirectory, "서버.d2s")));

            await File.WriteAllBytesAsync(Path.Combine(saveDirectory, "새캐릭.d2s"), [4]);
            await File.WriteAllBytesAsync(Path.Combine(saveDirectory, "새캐릭.key"), [5]);
            var collected = ProfileDirectoryManager.CollectForCheckin(
                installed.RegisteredCharacterNames,
                saveDirectory,
                quarantineRoot);
            var files = ProfileBundleCodec.Decode(collected.BundleData);

            Assert.DoesNotContain(files, file => file.RelativePath.StartsWith("새캐릭", StringComparison.Ordinal));
            Assert.NotNull(collected.QuarantineDirectory);
            Assert.True(File.Exists(Path.Combine(collected.QuarantineDirectory!, "새캐릭.d2s")));
            Assert.True(File.Exists(Path.Combine(collected.QuarantineDirectory!, "새캐릭.key")));

            ProfileDirectoryManager.RemoveCheckedInProfile(saveDirectory);
            Assert.False(File.Exists(Path.Combine(saveDirectory, "서버.d2s")));
            Assert.True(File.Exists(Path.Combine(saveDirectory, "lootfilter.txt")));
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }
}
