using D2SSharp.Model;
using JmServer.Domain;

namespace JmServer.GameIntegration;

public static class D2UniqueCharmInspector
{
    private static readonly IReadOnlyDictionary<string, string> UniqueCharmNames =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["cm1"] = "Annihilus",
            ["cm2"] = "Hellfire Torch",
            ["cm3"] = "Gheed's Fortune"
        };

    public static IReadOnlyList<D2UniqueCharmLocation> Inspect(
        string relativePath,
        ReadOnlySpan<byte> data)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(relativePath);
        if (relativePath.EndsWith(".d2s", StringComparison.OrdinalIgnoreCase))
        {
            var save = D2Save.Read(data, JmD2ExternalData.Instance);
            return Find(
                relativePath,
                "character",
                save.Items);
        }

        if (ProfileSavePolicy.IsSharedStash(relativePath))
        {
            var stash = D2StashSave.Read(data, JmD2ExternalData.Instance);
            return stash.SelectMany((tab, index) => Find(
                    relativePath,
                    $"shared-tab-{index + 1}",
                    tab.Items))
                .ToArray();
        }

        return [];
    }

    private static IReadOnlyList<D2UniqueCharmLocation> Find(
        string relativePath,
        string container,
        IEnumerable<Item> items) =>
        items
            .Where(item => UniqueCharmNames.ContainsKey(item.ItemCodeString))
            .Select(item => new D2UniqueCharmLocation(
                relativePath,
                container,
                UniqueCharmNames[item.ItemCodeString],
                item.ItemCodeString,
                item.Position.StorePage.ToString(),
                item.Position.InvX,
                item.Position.InvY))
            .ToArray();
}

public sealed record D2UniqueCharmLocation(
    string RelativePath,
    string Container,
    string ItemName,
    string ItemCode,
    string StorePage,
    uint X,
    uint Y);
