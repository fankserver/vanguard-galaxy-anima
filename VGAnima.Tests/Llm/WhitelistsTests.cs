using VGAnima.Llm;
using Xunit;

namespace VGAnima.Tests.Llm;

public class WhitelistsTests
{
    // -------- Faction --------
    [Fact]
    public void FactionWhitelist_HasExpectedCount()
    {
        // 18 identifiers: all vanilla factions except Player.
        Assert.Equal(18, FactionWhitelist.All.Count);
    }

    [Theory]
    [InlineData("Marauders")]
    [InlineData("PoliceGuild")]
    [InlineData("BountyGuild")]
    [InlineData("TradingGuild")]
    [InlineData("MiningGuild")]
    [InlineData("IndustrialGuild")]
    [InlineData("SalvageGuild")]
    [InlineData("Stranded")]
    [InlineData("MercenaryGuild")]
    [InlineData("Smugglers")]
    [InlineData("Darkspacers")]
    [InlineData("Puppeteers")]
    [InlineData("Fanatics")]
    [InlineData("HolyRadicals")]
    [InlineData("Amalgam")]
    [InlineData("Gold")]
    [InlineData("Red")]
    [InlineData("Blue")]
    public void FactionWhitelist_Contains_AllowedIdentifier(string id)
    {
        Assert.True(FactionWhitelist.Contains(id));
    }

    [Theory]
    [InlineData("Player")]           // deliberately excluded
    [InlineData("marauders")]        // lowercase — not a valid identifier
    [InlineData("")]
    [InlineData("NotAFaction")]
    public void FactionWhitelist_Rejects_Unknown(string id)
    {
        Assert.False(FactionWhitelist.Contains(id));
    }

    // -------- Trigger --------
    [Fact]
    public void TriggerWhitelist_HasExactlyThreeEntries()
    {
        Assert.Equal(3, TriggerWhitelist.All.Count);
    }

    [Theory]
    [InlineData("DockedWithSpaceStation")]
    [InlineData("ArrivedAtSpaceStation")]
    [InlineData("MoveToArea")]
    public void TriggerWhitelist_Contains_Allowed(string trigger)
    {
        Assert.True(TriggerWhitelist.Contains(trigger));
    }

    [Theory]
    [InlineData("UnitDestroyed")]
    [InlineData("TravelToPOI")]      // spec typo: real enum is MoveToArea
    [InlineData("dockedWithSpaceStation")]  // case-sensitive
    [InlineData("")]
    public void TriggerWhitelist_Rejects_Others(string trigger)
    {
        Assert.False(TriggerWhitelist.Contains(trigger));
    }

    // -------- ItemCategory --------
    [Fact]
    public void ItemCategoryWhitelist_HasExactlyFourEntries()
    {
        Assert.Equal(4, ItemCategoryWhitelist.All.Count);
    }

    [Theory]
    [InlineData("Ore")]
    [InlineData("Salvage")]
    [InlineData("RefinedProduct")]
    [InlineData("TradeGoods")]
    public void ItemCategoryWhitelist_Contains_Allowed(string cat)
    {
        Assert.True(ItemCategoryWhitelist.Contains(cat));
    }

    [Theory]
    [InlineData("Ammo")]
    [InlineData("Crystal")]
    [InlineData("Empty")]
    [InlineData("IronOre")]   // item identifier, not a category
    [InlineData("Junk")]      // reserved for special-quest variants (docs/special-quest-ideas.md)
    [InlineData("")]
    public void ItemCategoryWhitelist_Rejects_Others(string cat)
    {
        Assert.False(ItemCategoryWhitelist.Contains(cat));
    }

    // -------- ObjectiveType --------
    [Fact]
    public void ObjectiveTypeWhitelist_HasExactlyFiveEntries()
    {
        Assert.Equal(5, ObjectiveTypeWhitelist.All.Count);
    }

    [Theory]
    [InlineData("KillEnemies")]
    [InlineData("ProtectUnit")]
    [InlineData("TriggerObjective")]
    [InlineData("CollectItemTypes")]
    [InlineData("ClearPoi")]
    public void ObjectiveTypeWhitelist_Contains_Allowed(string type)
    {
        Assert.True(ObjectiveTypeWhitelist.Contains(type));
    }

    [Theory]
    [InlineData("TradeOffer")]
    [InlineData("Mining")]
    [InlineData("Salvage")]
    [InlineData("kill_enemies")]
    [InlineData("")]
    public void ObjectiveTypeWhitelist_Rejects_Others(string type)
    {
        Assert.False(ObjectiveTypeWhitelist.Contains(type));
    }

    // -------- RewardType --------
    [Fact]
    public void RewardTypeWhitelist_HasExactlyFourEntries()
    {
        // v1-item-rewards bumped this from 3 to 4 — Item was added.
        Assert.Equal(4, RewardTypeWhitelist.All.Count);
    }

    [Theory]
    [InlineData("Credits")]
    [InlineData("Experience")]
    [InlineData("Reputation")]
    [InlineData("Item")]
    public void RewardTypeWhitelist_Contains_Allowed(string type)
    {
        Assert.True(RewardTypeWhitelist.Contains(type));
    }

    [Theory]
    [InlineData("Skillpoint")]
    [InlineData("StoryMission")]
    [InlineData("WorkshopCredit")]
    [InlineData("")]
    public void RewardTypeWhitelist_Rejects_Others(string type)
    {
        Assert.False(RewardTypeWhitelist.Contains(type));
    }

    // -------- ItemRewardKind --------
    [Fact]
    public void ItemRewardKindWhitelist_HasExactlyThreeEntries()
    {
        Assert.Equal(3, ItemRewardKindWhitelist.All.Count);
    }

    [Theory]
    [InlineData("MiningClaim")]
    [InlineData("SalvageClaim")]
    [InlineData("MaterialMiningClaim")]
    public void ItemRewardKindWhitelist_Contains_Allowed(string kind)
    {
        Assert.True(ItemRewardKindWhitelist.Contains(kind));
    }

    [Theory]
    [InlineData("Blueprint")]
    [InlineData("Ship")]
    [InlineData("Crew")]
    [InlineData("")]
    public void ItemRewardKindWhitelist_Rejects_Others(string kind)
    {
        Assert.False(ItemRewardKindWhitelist.Contains(kind));
    }
}
