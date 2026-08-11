using D2SSharp.Enums;
using D2SSharp.Model;
using JmServer.GameIntegration;

namespace JmServer.GameIntegration.Tests;

public sealed class D2UniqueCharmInspectorTests
{
    [Fact]
    public void Inspect_FindsTorchInSharedStashAndReportsItsTab()
    {
        var stash = D2StashSave.Read(D2SharedStashGold.CreateModernSoftcore(0));
        stash[2].Items.Add(new Item
        {
            Flags = ItemFlags.Identified | ItemFlags.CompactSave,
            Version = 101,
            Position = new ItemPosition
            {
                Mode = ItemMode.Stored,
                StorePage = StorePage.Stash,
                InvX = 4,
                InvY = 5
            },
            ItemCodeString = "cm2",
            ItemLevel = 85,
            Quality = ItemQuality.Normal
        });

        var locations = D2UniqueCharmInspector.Inspect(
            D2SharedStashGold.SoftcoreFileName,
            stash.ToBytes(targetVersion: 105));

        var torch = Assert.Single(locations);
        Assert.Equal("Hellfire Torch", torch.ItemName);
        Assert.Equal("shared-tab-3", torch.Container);
        Assert.Equal("Stash", torch.StorePage);
        Assert.Equal(4u, torch.X);
        Assert.Equal(5u, torch.Y);
    }
}
