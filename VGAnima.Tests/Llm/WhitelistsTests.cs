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

    // -------- Intent (v2) --------
    [Fact]
    public void IntentWhitelist_HasExactlySevenEntries()
    {
        // v2 intents. escort_to_station deferred until the vanilla escort
        // pattern (CreateFixedPayload + CreateEscortLocation +
        // EscortUnitCargoUnloaded trigger) is wired up — shipping 7
        // that all work beats shipping 8 where the 8th is flaky.
        Assert.Equal(7, IntentWhitelist.All.Count);
    }

    [Theory]
    [InlineData("clear_combat_site")]
    [InlineData("gather_ore")]
    [InlineData("gather_salvage")]
    [InlineData("defended_gather_ore")]
    [InlineData("defended_gather_salvage")]
    [InlineData("deliver_to_station")]
    [InlineData("haul_goods")]
    public void IntentWhitelist_Contains_Allowed(string intent)
    {
        Assert.True(IntentWhitelist.Contains(intent));
    }

    [Theory]
    [InlineData("escort_to_station")] // deferred; MUST be rejected until it ships
    [InlineData("KillEnemies")]       // old v1 objective type
    [InlineData("ClearPoi")]          // old v1 objective type
    [InlineData("GATHER_ORE")]        // case-sensitive
    [InlineData("")]
    public void IntentWhitelist_Rejects_Others(string intent)
    {
        Assert.False(IntentWhitelist.Contains(intent));
    }

    [Theory]
    // Simple archetype intents — single entry.
    [InlineData("clear_combat_site", new[] { "combat" })]
    [InlineData("gather_ore",        new[] { "gather" })]
    [InlineData("gather_salvage",    new[] { "salvage" })]
    [InlineData("deliver_to_station", new[] { "deliver" })]
    // Composite — forbidding EITHER archetype must block the intent.
    [InlineData("defended_gather_ore",     new[] { "combat", "gather" })]
    [InlineData("defended_gather_salvage", new[] { "combat", "salvage" })]
    [InlineData("haul_goods",              new[] { "deliver" })]
    public void IntentWhitelist_Archetypes_Correct(string intent, string[] expected)
    {
        var archetypes = IntentWhitelist.Archetypes(intent);
        Assert.Equal(expected, archetypes);
    }

    // -------- RewardType --------
    [Fact]
    public void RewardTypeWhitelist_HasExactlyFourEntries()
    {
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
