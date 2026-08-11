using System.Reflection;
using System.Text.Json;

namespace JmServer.GameIntegration;

public static class JmLootFilterProfile
{
    private const string ResourceName =
        "JmServer.GameIntegration.ModData.jm-loot-filter.json";

    private static readonly Lazy<string> ProfileJson = new(LoadAndValidate);

    public static string GetJson() => ProfileJson.Value;

    private static string LoadAndValidate()
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(ResourceName)
                           ?? throw new InvalidDataException(
                               "The embedded JM loot-filter profile is missing.");
        using var reader = new StreamReader(stream);
        var json = reader.ReadToEnd();
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        if (!root.TryGetProperty("name", out var name) ||
            string.IsNullOrWhiteSpace(name.GetString()) ||
            !root.TryGetProperty("rules", out var rules) ||
            rules.ValueKind != JsonValueKind.Array ||
            rules.GetArrayLength() == 0)
        {
            throw new InvalidDataException("The embedded JM loot-filter profile is invalid.");
        }

        foreach (var rule in rules.EnumerateArray())
        {
            if (rule.TryGetProperty("ruleType", out var ruleType) &&
                ruleType.GetString() == "hide" &&
                rule.TryGetProperty("enabled", out var enabled) &&
                enabled.GetBoolean())
            {
                throw new InvalidDataException(
                    "The default JM loot-filter must not enable hiding rules.");
            }
        }

        return json;
    }
}
