using System.Text.Json.Nodes;
using JmServer.GameIntegration;

namespace JmServer.GameIntegration.Tests;

public sealed class D2RModSettingsTests
{
    [Fact]
    public async Task ExistingModSettingsAreNotRewrittenWithoutDisplayOverride()
    {
        var root = Path.Combine(Path.GetTempPath(), "jm-settings-test-" + Guid.NewGuid().ToString("N"));
        var source = Path.Combine(root, "base");
        var target = Path.Combine(root, "mod");
        Directory.CreateDirectory(source);
        Directory.CreateDirectory(target);
        try
        {
            await File.WriteAllTextAsync(
                Path.Combine(source, "Settings.json"),
                """{"First Time":1,"Tutorial":3,"Help Menu":1,"Gamma":155,"VSync":1,"Quick Cast Enabled":0}""");
            var settingsText =
                """{"Gamma":222,"VSync":0,"Quick Cast Enabled":1,"Auto Party Invite":0}""";
            await File.WriteAllTextAsync(
                Path.Combine(target, "Settings.json"),
                settingsText);
            var settingsPath = Path.Combine(target, "Settings.json");
            var writeTime = File.GetLastWriteTimeUtc(settingsPath);

            await D2RModSettings.EnsureInitializedAsync(source, target);

            Assert.Equal(settingsText, await File.ReadAllTextAsync(settingsPath));
            Assert.Equal(writeTime, File.GetLastWriteTimeUtc(settingsPath));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task SubsequentInitializationKeepsGameplayChangesWrittenToModSettings()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            "jm-settings-gameplay-test-" + Guid.NewGuid().ToString("N"));
        var source = Path.Combine(root, "base");
        var target = Path.Combine(root, "mod");
        Directory.CreateDirectory(source);
        Directory.CreateDirectory(target);
        var targetPath = Path.Combine(target, "Settings.json");
        try
        {
            await File.WriteAllTextAsync(
                Path.Combine(source, "Settings.json"),
                """{"First Time":1,"Quick Cast Enabled":0,"Auto Party Invite":0}""");
            await File.WriteAllTextAsync(
                targetPath,
                """{"Quick Cast Enabled":0,"Auto Party Invite":0}""");

            await D2RModSettings.EnsureInitializedAsync(source, target);
            await File.WriteAllTextAsync(
                targetPath,
                """{"Quick Cast Enabled":1,"Auto Party Invite":1}""");
            await D2RModSettings.EnsureInitializedAsync(source, target);

            var settings = JsonNode.Parse(await File.ReadAllTextAsync(targetPath))!.AsObject();
            Assert.Equal(1, settings["Quick Cast Enabled"]!.GetValue<int>());
            Assert.Equal(1, settings["Auto Party Invite"]!.GetValue<int>());
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task MatchingLauncherDisplaySettingsDoNotRewriteLocalSettings()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            "jm-settings-display-match-test-" + Guid.NewGuid().ToString("N"));
        var source = Path.Combine(root, "base");
        var target = Path.Combine(root, "mod");
        Directory.CreateDirectory(source);
        Directory.CreateDirectory(target);
        var targetPath = Path.Combine(target, "Settings.json");
        var settingsText =
            """{"Window Mode":0,"Screen Resolution (Windowed)":"1920x1080","Item Name Display":1,"Auto Party Invite":0,"Display Active Skill Bindings":1}""";
        try
        {
            await File.WriteAllTextAsync(targetPath, settingsText);
            var writeTime = File.GetLastWriteTimeUtc(targetPath);

            await D2RModSettings.EnsureInitializedAsync(
                source,
                target,
                displaySettings: new D2RDisplaySettings(
                    D2RWindowMode.Windowed,
                    1920,
                    1080));

            Assert.Equal(settingsText, await File.ReadAllTextAsync(targetPath));
            Assert.Equal(writeTime, File.GetLastWriteTimeUtc(targetPath));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task MissingModSettingsAreCopiedFromBaseSettings()
    {
        var root = Path.Combine(Path.GetTempPath(), "jm-settings-copy-test-" + Guid.NewGuid().ToString("N"));
        var source = Path.Combine(root, "base");
        var target = Path.Combine(root, "mod");
        Directory.CreateDirectory(source);
        try
        {
            await File.WriteAllTextAsync(
                Path.Combine(source, "Settings.json"),
                """{"First Time":1,"Tutorial":3,"GammaHD":2400}""");

            await D2RModSettings.EnsureInitializedAsync(source, target);

            var settings = JsonNode.Parse(await File.ReadAllTextAsync(
                Path.Combine(target, "Settings.json")))!.AsObject();
            Assert.Equal(1, settings["First Time"]!.GetValue<int>());
            Assert.Equal(2400, settings["GammaHD"]!.GetValue<int>());
            Assert.Equal(1, settings["Help Menu"]!.GetValue<int>());
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Theory]
    [InlineData(D2RWindowMode.Windowed, 0, 1280, 720)]
    [InlineData(D2RWindowMode.Windowed, 0, 2560, 1440)]
    [InlineData(D2RWindowMode.Fullscreen, 1, 3840, 2160)]
    public async Task SelectedDisplaySettingsOverrideOnlyDisplayValues(
        D2RWindowMode mode,
        int expectedModeValue,
        int width,
        int height)
    {
        var root = Path.Combine(Path.GetTempPath(), "jm-display-settings-test-" + Guid.NewGuid().ToString("N"));
        var source = Path.Combine(root, "base");
        var target = Path.Combine(root, "mod");
        Directory.CreateDirectory(source);
        Directory.CreateDirectory(target);
        try
        {
            await File.WriteAllTextAsync(
                Path.Combine(source, "Settings.json"),
                """{"Window Mode":1,"Screen Resolution (Windowed)":"1920x1080","GammaHD":2400}""");
            await File.WriteAllTextAsync(
                Path.Combine(target, "Settings.json"),
                """{"Window Mode":1,"Screen Resolution (Windowed)":"1600x900","GammaHD":3200}""");

            await D2RModSettings.EnsureInitializedAsync(
                source,
                target,
                displaySettings: new D2RDisplaySettings(mode, width, height));

            var settings = JsonNode.Parse(await File.ReadAllTextAsync(
                Path.Combine(target, "Settings.json")))!.AsObject();
            Assert.Equal(expectedModeValue, settings["Window Mode"]!.GetValue<int>());
            Assert.Equal($"{width}x{height}", settings["Screen Resolution (Windowed)"]!.GetValue<string>());
            Assert.Equal(3200, settings["GammaHD"]!.GetValue<int>());
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Theory]
    [InlineData(1279, 720)]
    [InlineData(1280, 719)]
    [InlineData(7681, 2160)]
    [InlineData(3840, 4321)]
    public void InvalidDisplayResolutionIsRejected(int width, int height)
    {
        var settings = new D2RDisplaySettings(D2RWindowMode.Windowed, width, height);

        Assert.Throws<ArgumentOutOfRangeException>(settings.Validate);
    }

}
