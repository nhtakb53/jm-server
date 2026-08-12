using JmServer.GameIntegration;

namespace JmServer.GameIntegration.Tests;

public sealed class D2RLoaderInstallerTests
{
    [Fact]
    public async Task SynchronizeSupplyModDirectoryRestoresMissingAndChangedNestedFiles()
    {
        var temporaryDirectory = Path.Combine(
            Path.GetTempPath(),
            "jm-supply-sync-test-" + Guid.NewGuid().ToString("N"));
        try
        {
            await JmSupplyModPackage.WriteToDirectoryAsync(temporaryDirectory);
            var missingSprite = Path.Combine(
                temporaryDirectory,
                "data", "hd", "global", "ui", "items", "misc", "jm_selectors",
                "unique_sword.lowend.sprite");
            File.Delete(missingSprite);
            var changedItems = Path.Combine(
                temporaryDirectory,
                "data", "hd", "items", "items.json");
            await File.AppendAllTextAsync(changedItems, "changed");

            await D2RLoaderInstaller.SynchronizeSupplyModDirectoryAsync(temporaryDirectory);

            var verification = await JmSupplyModPackage.VerifyAsync(temporaryDirectory);
            Assert.True(verification.IsValid, verification.Message);
            Assert.True(File.Exists(missingSprite));
        }
        finally
        {
            if (Directory.Exists(temporaryDirectory))
            {
                Directory.Delete(temporaryDirectory, recursive: true);
            }
        }
    }

    private const string ReleaseConfiguration =
        """
        [d2rloader]
        default_mod = ""
        show_tcpip_button = false

        [d2rloader.advanced]
        allow_global_extensions = true
        allow_mod_extensions = true
        monitor_startup_crashes = false
        write_crash_dumps = false

        [d2rloader.developer]
        enable_console = false

        [d2rcore.items]
        show_ground_sockets = false

        [d2rcore.stash]
        set_materials_limit = 255
        """;

    [Fact]
    public void RestrictedConfigurationSelectsIsolatedModAndDisablesExtensions()
    {
        var result = D2RLoaderInstaller.CreateRestrictedConfiguration(ReleaseConfiguration);

        Assert.Contains("default_mod = \"JMServer\"", result, StringComparison.Ordinal);
        Assert.Contains("show_tcpip_button = true", result, StringComparison.Ordinal);
        Assert.Contains("allow_global_extensions = false", result, StringComparison.Ordinal);
        Assert.Contains("allow_mod_extensions = false", result, StringComparison.Ordinal);
        Assert.Contains(
            "[d2rcore.stash]\nset_materials_limit = 99",
            result,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "[d2rcore.items]\nset_materials_limit",
            result,
            StringComparison.Ordinal);
        Assert.True(D2RLoaderInstaller.HasRestrictedConfiguration(result));
    }

    [Fact]
    public void RestrictedConfigurationRejectsMissingSetting()
    {
        var incomplete = ReleaseConfiguration.Replace(
            "enable_console = false",
            string.Empty,
            StringComparison.Ordinal);

        Assert.Throws<InvalidDataException>(
            () => D2RLoaderInstaller.CreateRestrictedConfiguration(incomplete));
    }

    [Fact]
    public void CharacterSavePathRejectsTraversal()
    {
        Assert.Throws<InvalidDataException>(
            () => D2RClientLayout.GetCharacterSavePath("..\\outside"));
    }
}
