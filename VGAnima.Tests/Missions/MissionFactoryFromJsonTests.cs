using System.Collections.Generic;
using Source.MissionSystem;
using Source.MissionSystem.Objectives;
using Source.MissionSystem.Rewards;
using VGAnima.Llm;
using VGAnima.Missions;
using Xunit;
using ReputationReward = Source.MissionSystem.Rewards.Reputation;
using MiningObjective  = Source.MissionSystem.Objectives.Mining;

namespace VGAnima.Tests.Missions;

public class MissionFactoryFromJsonTests
{
    // Decomp-stub limitation: `Source.Galaxy.Faction..cctor()` NREs in the
    // xUnit appdomain because its static init calls Unity-dependent subclass
    // ctors. Every factory path calls `Faction.Get(...)` on its first line,
    // so the NRE surfaces before any assertion. These tests document the
    // intended shape of the v2 intent → mechanical translation; they run
    // only under live BepInEx.
    private const string FactionCctorSkip =
        "Faction..cctor NREs without Unity runtime — deferred to E2E";

    [Fact(Skip = FactionCctorSkip)]
    public void Build_CopiesTopLevelFields()
    {
        var block = new LlmMissionBlock(
            Name:           "Smash and Grab",
            Description:    "A job.",
            CompletionText: "Nicely done.",
            SourceFaction:  "TradingGuild",
            Steps:          new[] { Step(new GatherOreIntent(5, "Mine.")) },
            Rewards:        new LlmReward[] { new LlmCreditsReward(50) });

        var mission = MissionFactoryFromJson.Build(
            block, missionLevel: 5, brokerStation: null, brokerSeed: "broker-0");

        Assert.Equal("Smash and Grab", mission.name);
        Assert.Equal("A job.",         mission.description);
        Assert.Equal("Nicely done.",   mission.completionText);
        Assert.Equal("TradingGuild",   mission.sourceFaction.identifier);
        Assert.True(mission.trackedOnHud);
        Assert.False(mission.dynamicLevel);
        Assert.False(mission.canBeIdled);
        Assert.Equal(MissionDifficulty.Story, mission.difficulty);
        Assert.StartsWith("vganima_llm_", mission.storyId);
        Assert.Contains("broker-0",       mission.storyId);
    }

    [Fact(Skip = FactionCctorSkip)]
    public void Build_ClearCombatSite_SpawnsKillEnemiesObjective()
    {
        // clear_combat_site → AddCombat + AddGuards + KillEnemies whose
        // requiredAmount = totalUnitCount. Mirrors vanilla BountyHunt.
        var block = Minimal(new ClearCombatSiteIntent("Marauders", "Clear."));
        var mission = MissionFactoryFromJson.Build(block, 5, null, "seed");
        Assert.Single(mission.steps);
        Assert.IsType<KillEnemies>(mission.steps[0].objectives[0]);
    }

    [Fact(Skip = FactionCctorSkip)]
    public void Build_GatherOre_EmitsMiningObjective()
    {
        // v2 switched from CollectItemTypes (diversity) to Mining (quantity)
        // after a live bug where Fragments didn't count. Verified shape
        // matters.
        var block = Minimal(new GatherOreIntent(10, "Mine."));
        var mission = MissionFactoryFromJson.Build(block, 5, null, "seed");
        var collect = Assert.IsType<MiningObjective>(mission.steps[0].objectives[0]);
        Assert.Equal(Source.Item.ItemCategory.Ore, collect.itemCategory);
        Assert.Equal(10, collect.requiredAmount);
    }

    [Fact(Skip = FactionCctorSkip)]
    public void Build_DefendedGatherSalvage_EmitsMiningObjective()
    {
        // Defended variant still ends up with a single Mining objective —
        // guards are attached to the POI, not surfaced as a separate step.
        var block = Minimal(new DefendedGatherSalvageIntent(10, "Marauders", "Fight and loot."));
        var mission = MissionFactoryFromJson.Build(block, 5, null, "seed");
        var collect = Assert.IsType<MiningObjective>(mission.steps[0].objectives[0]);
        Assert.Equal(Source.Item.ItemCategory.Salvage, collect.itemCategory);
    }

    [Fact(Skip = FactionCctorSkip)]
    public void Build_DeliverToStation_EmitsTravelToPOI()
    {
        var dests = new[]
        {
            new AccessibleDestination(
                ShortId: "dest_0", StationName: "Sarus Prime", SystemName: "Sarus",
                FactionIdentifier: "Gold", FactionDisplayName: "Luminate",
                JumpsAway: 1, SameFactionAsBroker: false, Guid: "guid_sarus"),
        };
        var block = Minimal(new DeliverToStationIntent("dest_0", "Courier."));
        var mission = MissionFactoryFromJson.Build(block, 5, null, "seed",
            accessibleDestinations: dests);
        var travel = Assert.IsType<TravelToPOI>(mission.steps[0].objectives[0]);
        Assert.Equal("guid_sarus", travel.targetPOI);
    }

    [Fact(Skip = FactionCctorSkip)]
    public void Build_HaulGoods_EmitsCollectPlusTravel()
    {
        var dests = new[]
        {
            new AccessibleDestination(
                ShortId: "dest_0", StationName: "Sarus Prime", SystemName: "Sarus",
                FactionIdentifier: "Gold", FactionDisplayName: "Luminate",
                JumpsAway: 1, SameFactionAsBroker: false, Guid: "guid_sarus"),
        };
        var block = Minimal(new HaulGoodsIntent(10, "dest_0", "Haul."));
        var mission = MissionFactoryFromJson.Build(block, 5, null, "seed",
            accessibleDestinations: dests);
        // Two objectives in one step: collect then travel.
        Assert.Equal(2, mission.steps[0].objectives.Count);
        var collect = Assert.IsType<MiningObjective>(mission.steps[0].objectives[0]);
        Assert.Equal(Source.Item.ItemCategory.TradeGoods, collect.itemCategory);
        Assert.IsType<TravelToPOI>(mission.steps[0].objectives[1]);
    }

    [Fact(Skip = FactionCctorSkip)]
    public void Build_CreditsReward_Scaled_ByMissionLevel()
    {
        var block = Minimal(new GatherOreIntent(5, "d"),
            new LlmCreditsReward(100));
        var mission = MissionFactoryFromJson.Build(block, missionLevel: 5, null, "seed");
        var credits = Assert.IsType<Credits>(mission.rewards[0]);
        Assert.True(credits.amount > 100,
            $"scaled credits {credits.amount} should be > base_value 100");
    }

    [Fact(Skip = FactionCctorSkip)]
    public void Build_ReputationReward_CarriesFaction()
    {
        var block = Minimal(new GatherOreIntent(5, "d"),
            new LlmReputationReward("Marauders", -200));
        var mission = MissionFactoryFromJson.Build(block, 5, null, "seed");
        var rep = Assert.IsType<ReputationReward>(mission.rewards[0]);
        Assert.Equal("Marauders", rep.faction.identifier);
        Assert.Equal(-200, rep.amount);
    }

    [Fact(Skip = FactionCctorSkip)]
    public void Build_MultipleSteps_PreservesOrder()
    {
        var block = new LlmMissionBlock(
            Name: "T", Description: "d", CompletionText: "c", SourceFaction: "TradingGuild",
            Steps: new[]
            {
                Step(new ClearCombatSiteIntent("Marauders", "Clear.")),
                Step(new GatherSalvageIntent(10, "Loot.")),
            },
            Rewards: new LlmReward[] { new LlmCreditsReward(50) });
        var mission = MissionFactoryFromJson.Build(block, 5, null, "seed");
        Assert.Equal(2, mission.steps.Count);
        Assert.IsType<KillEnemies>(mission.steps[0].objectives[0]);
        Assert.IsType<MiningObjective>(mission.steps[1].objectives[0]);
    }

    [Fact(Skip = FactionCctorSkip)]
    public void Build_StoryId_IsUniqueAcrossCalls()
    {
        var block = Minimal(new GatherOreIntent(5, "d"));
        var a = MissionFactoryFromJson.Build(block, 5, null, "same-seed").storyId;
        var b = MissionFactoryFromJson.Build(block, 5, null, "same-seed").storyId;
        Assert.NotEqual(a, b);  // Guid suffix differs
    }

    // ---------- Helpers ----------

    private static LlmMissionStep Step(LlmIntent intent) => new(intent);

    private static LlmMissionBlock Minimal(LlmIntent intent, LlmReward? reward = null) =>
        new(
            Name: "T", Description: "d", CompletionText: "c", SourceFaction: "TradingGuild",
            Steps:   new[] { Step(intent) },
            Rewards: new[] { reward ?? new LlmCreditsReward(50) });
}
