using System.Text.Json.Nodes;
using JmServer.GameIntegration;

namespace JmServer.GameIntegration.Tests;

public sealed class D2RModSettingsTests
{
    [Fact]
    public async Task ExistingModSettingsKeepUserValuesAndGainFirstRunMarkers()
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
                """{"First Time":1,"Tutorial":3,"Help Menu":1,"Gamma":155,"VSync":1}""");
            await File.WriteAllTextAsync(
                Path.Combine(target, "Settings.json"),
                """{"Gamma":222,"VSync":0}""");

            await D2RModSettings.EnsureInitializedAsync(source, target);

            var settings = JsonNode.Parse(await File.ReadAllTextAsync(
                Path.Combine(target, "Settings.json")))!.AsObject();
            Assert.Equal(222, settings["Gamma"]!.GetValue<int>());
            Assert.Equal(0, settings["VSync"]!.GetValue<int>());
            Assert.Equal(1, settings["First Time"]!.GetValue<int>());
            Assert.Equal(3, settings["Tutorial"]!.GetValue<int>());
            Assert.Equal(1, settings["Help Menu"]!.GetValue<int>());
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

    [Fact]
    public async Task SyncedSettingsAreValidatedAndInstalledExactly()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            "jm-settings-sync-test-" + Guid.NewGuid().ToString("N"));
        var settings = """{"Gamma":241,"VSync":0,"Sound Volume":77}"""u8.ToArray();
        try
        {
            await D2RModSettings.InstallSyncedAsync(settings, root);

            var saved = await D2RModSettings.ReadForSyncAsync(root);
            Assert.Equal(settings, saved);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Theory]
    [InlineData("[]")]
    [InlineData("invalid")]
    public async Task InvalidSyncedSettingsAreRejected(string value)
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            "jm-settings-sync-invalid-test-" + Guid.NewGuid().ToString("N"));
        try
        {
            await Assert.ThrowsAsync<InvalidDataException>(() =>
                D2RModSettings.InstallSyncedAsync(
                    System.Text.Encoding.UTF8.GetBytes(value),
                    root));
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
