using System.Collections.Generic;
using Source.MissionSystem;
using Source.MissionSystem.Objectives;
using Source.MissionSystem.Rewards;
using VGAnima.Llm;
using VGAnima.Missions;
using Xunit;
// Rewards.Reputation conflicts with the game's other Reputation — alias for
// clarity in assertions below.
using ReputationReward = Source.MissionSystem.Rewards.Reputation;

namespace VGAnima.Tests.Missions;

public class MissionFactoryFromJsonTests
{
    // Decomp-stub limitation: `Source.Galaxy.Faction..cctor()` NREs in the
    // xUnit appdomain because its static init calls `Factions.Player..ctor()`
    // (a Unity-dependent subclass ctor). Every factory path below calls
    // `Faction.Get(...)` on its first line, so the NRE surfaces before any
    // assertion can run. These tests stay in the file for documentation /
    // future-proofing (if the stub ever exposes a testable path) but must
    // be Skipped under the current stub. Matches the plan's own pattern for
    // Experience-reward tests (GameMath.GetExperienceRewardValue NREs for
    // the same reason — left to manual E2E).
    private const string FactionCctorSkip =
        "Faction..cctor NREs without Unity runtime — deferred to E2E, like Experience scaling";

    [Fact(Skip = FactionCctorSkip)]
    public void Build_CopiesTopLevelFields()
    {
        var block = new LlmMissionBlock(
            Name:           "Smash and Grab",
            Description:    "A job.",
            CompletionText: "Nicely done.",
            SourceFaction:  "TradingGuild",
            Steps:          new[] { StepWithTrigger("DockedWithSpaceStation") },
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
    public void Build_CreatesTriggerObjective()
    {
        var block = new LlmMissionBlock(
            Name: "T", Description: "d", CompletionText: "c", SourceFaction: "TradingGuild",
            Steps: new[]
            {
                new LlmMissionStep(new LlmObjective[]
                {
                    new LlmTriggerObjective("ArrivedAtSpaceStation", 2, "Arrive twice."),
                }),
            },
            Rewards: new LlmReward[] { new LlmCreditsReward(50) });

        var mission = MissionFactoryFromJson.Build(block, 5, null, "seed");

        Assert.Single(mission.steps);
        var objs = mission.steps[0].objectives;
        Assert.Single(objs);
        var trig = Assert.IsType<TriggerObjective>(objs[0]);
        Assert.Equal(MissionTrigger.ArrivedAtSpaceStation, trig.trigger);
        Assert.Equal(2, trig.requiredAmount);
        Assert.Equal("Arrive twice.", trig.description);
    }

    [Fact(Skip = FactionCctorSkip)]
    public void Build_CreatesKillEnemies()
    {
        var block = new LlmMissionBlock(
            Name: "T", Description: "d", CompletionText: "c", SourceFaction: "TradingGuild",
            Steps: new[]
            {
                new LlmMissionStep(new LlmObjective[]
                {
                    new LlmKillEnemies("Marauders", 3, "Kill marauders."),
                }),
            },
            Rewards: new LlmReward[] { new LlmCreditsReward(50) });

        var mission = MissionFactoryFromJson.Build(block, 5, null, "seed");
        var kill = Assert.IsType<KillEnemies>(mission.steps[0].objectives[0]);
        Assert.Equal("Marauders", kill.enemyFaction.identifier);
        Assert.Equal(3, kill.requiredAmount);
    }

    [Fact(Skip = FactionCctorSkip)]
    public void Build_CreatesProtectUnit()
    {
        var block = new LlmMissionBlock(
            Name: "T", Description: "d", CompletionText: "c", SourceFaction: "TradingGuild",
            Steps: new[]
            {
                new LlmMissionStep(new LlmObjective[]
                {
                    new LlmProtectUnit("Keep the convoy alive."),
                    new LlmKillEnemies("Marauders", 1, "Kill one."),
                }),
            },
            Rewards: new LlmReward[] { new LlmCreditsReward(50) });

        var mission = MissionFactoryFromJson.Build(block, 5, null, "seed");
        var protect = Assert.IsType<ProtectUnit>(mission.steps[0].objectives[0]);
        Assert.Equal("Keep the convoy alive.", protect.protectText);
    }

    [Fact(Skip = FactionCctorSkip)]
    public void Build_CreatesCollectItemTypes_WithCategory()
    {
        var block = new LlmMissionBlock(
            Name: "T", Description: "d", CompletionText: "c", SourceFaction: "TradingGuild",
            Steps: new[]
            {
                new LlmMissionStep(new LlmObjective[]
                {
                    new LlmCollectItemTypes("Ore", 5, "Collect ore."),
                }),
            },
            Rewards: new LlmReward[] { new LlmCreditsReward(50) });

        var mission = MissionFactoryFromJson.Build(block, 5, null, "seed");
        var collect = Assert.IsType<CollectItemTypes>(mission.steps[0].objectives[0]);
        Assert.Equal(Source.Item.ItemCategory.Ore, collect.itemCategory);
        Assert.Equal(5, collect.requiredAmount);
    }

    [Fact(Skip = FactionCctorSkip)]
    public void Build_CreditsReward_Scaled_ByMissionLevel()
    {
        var block = new LlmMissionBlock(
            Name: "T", Description: "d", CompletionText: "c", SourceFaction: "TradingGuild",
            Steps:   new[] { StepWithTrigger("DockedWithSpaceStation") },
            Rewards: new LlmReward[] { new LlmCreditsReward(100) });

        var mission = MissionFactoryFromJson.Build(block, missionLevel: 5, null, "seed");
        var credits = Assert.IsType<Credits>(mission.rewards[0]);
        // GameMath.GetCreditsValue(100, 5) — exact number depends on the game's
        // CostMultiplier curve, but must be strictly positive and much larger
        // than the base_value input (it's scaled by 100 + the curve).
        Assert.True(credits.amount > 100,
            $"scaled credits {credits.amount} should be > base_value 100");
    }

    [Fact(Skip = FactionCctorSkip)]
    public void Build_ReputationReward_CarriesFaction()
    {
        var block = new LlmMissionBlock(
            Name: "T", Description: "d", CompletionText: "c", SourceFaction: "TradingGuild",
            Steps:   new[] { StepWithTrigger("DockedWithSpaceStation") },
            Rewards: new LlmReward[]
            {
                new LlmReputationReward("Marauders", -200),
            });

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
                new LlmMissionStep(new LlmObjective[]
                {
                    new LlmTriggerObjective("DockedWithSpaceStation", 1, "Dock."),
                }),
                new LlmMissionStep(new LlmObjective[]
                {
                    new LlmKillEnemies("Marauders", 2, "Kill two."),
                }),
            },
            Rewards: new LlmReward[] { new LlmCreditsReward(50) });

        var mission = MissionFactoryFromJson.Build(block, 5, null, "seed");
        Assert.Equal(2, mission.steps.Count);
        Assert.IsType<TriggerObjective>(mission.steps[0].objectives[0]);
        Assert.IsType<KillEnemies>(mission.steps[1].objectives[0]);
    }

    [Fact(Skip = FactionCctorSkip)]
    public void Build_StoryId_IsUniqueAcrossCalls()
    {
        var block = Minimal();
        var a = MissionFactoryFromJson.Build(block, 5, null, "same-seed").storyId;
        var b = MissionFactoryFromJson.Build(block, 5, null, "same-seed").storyId;
        Assert.NotEqual(a, b);  // Guid suffix differs
    }

    // ---------- Helpers ----------

    private static LlmMissionStep StepWithTrigger(string trigger) =>
        new(new LlmObjective[]
        {
            new LlmTriggerObjective(trigger, 1, "Do the thing."),
        });

    private static LlmMissionBlock Minimal() => new(
        Name: "T", Description: "d", CompletionText: "c", SourceFaction: "TradingGuild",
        Steps:   new[] { StepWithTrigger("DockedWithSpaceStation") },
        Rewards: new LlmReward[] { new LlmCreditsReward(50) });
}
