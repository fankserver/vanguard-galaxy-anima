using System.Collections.Generic;
using VGAnima.Llm;
using VGAnima.Missions;
using VGAnima.Persistence;
using Xunit;

namespace VGAnima.Tests.Missions;

public class ArchetypeInferrerTests
{
    private static LlmMissionBlock Block(params LlmObjective[] objectives) =>
        new(Name: "Test", Description: "x", CompletionText: "x",
            SourceFaction: "SalvageGuild",
            Steps:   new[] { new LlmMissionStep(objectives) },
            Rewards: new List<LlmReward>());

    [Fact]
    public void ClearPoi_Alone_IsCombat()
    {
        var b = Block(new LlmClearPoi("Marauders", "desc"));
        Assert.Equal(MissionArchetypes.Combat, ArchetypeInferrer.Infer(b));
    }

    [Fact]
    public void KillEnemies_Alone_IsCombat()
    {
        var b = Block(new LlmKillEnemies("Marauders", 3, "desc"));
        Assert.Equal(MissionArchetypes.Combat, ArchetypeInferrer.Infer(b));
    }

    [Fact]
    public void CollectOre_Alone_IsGather()
    {
        var b = Block(new LlmCollectItemTypes("Ore", 10, "desc"));
        Assert.Equal(MissionArchetypes.Gather, ArchetypeInferrer.Infer(b));
    }

    [Fact]
    public void CollectSalvage_Alone_IsSalvage()
    {
        var b = Block(new LlmCollectItemTypes("Salvage", 10, "desc"));
        Assert.Equal(MissionArchetypes.Salvage, ArchetypeInferrer.Infer(b));
    }

    [Fact]
    public void CollectSalvage_WithGuards_IsDefendedCollect()
    {
        // Single-step defended gather — guards_faction set makes it the
        // hybrid shape even without a separate combat objective.
        var b = Block(new LlmCollectItemTypes(
            "Salvage", 10, "desc", GuardsFaction: "Marauders"));
        Assert.Equal(MissionArchetypes.DefendedCollect, ArchetypeInferrer.Infer(b));
    }

    [Fact]
    public void CollectOre_WithGuards_IsDefendedCollect()
    {
        var b = Block(new LlmCollectItemTypes(
            "Ore", 10, "desc", GuardsFaction: "Marauders"));
        Assert.Equal(MissionArchetypes.DefendedCollect, ArchetypeInferrer.Infer(b));
    }

    [Fact]
    public void ClearPoi_PlusCollect_SameStep_IsDefendedCollect()
    {
        // Hybrid detected at mission-wide scope, not just step-level.
        var b = Block(
            new LlmClearPoi("Marauders", "clear"),
            new LlmCollectItemTypes("Salvage", 10, "collect"));
        Assert.Equal(MissionArchetypes.DefendedCollect, ArchetypeInferrer.Infer(b));
    }

    [Fact]
    public void MultiStep_CombatThenGather_IsDefendedCollect()
    {
        // Combat step + gather step = hybrid. Inferrer looks across all steps.
        var b = new LlmMissionBlock(
            Name: "Multi", Description: "x", CompletionText: "x",
            SourceFaction: "SalvageGuild",
            Steps: new[]
            {
                new LlmMissionStep(new LlmObjective[] { new LlmClearPoi("Marauders", "d") }),
                new LlmMissionStep(new LlmObjective[] { new LlmCollectItemTypes("Salvage", 10, "d") }),
            },
            Rewards: new List<LlmReward>());
        Assert.Equal(MissionArchetypes.DefendedCollect, ArchetypeInferrer.Infer(b));
    }

    [Fact]
    public void ProtectUnit_IsEscort_EvenWithCombat()
    {
        var b = Block(
            new LlmProtectUnit("Protect the freighter."),
            new LlmClearPoi("Marauders", "also fight"));
        Assert.Equal(MissionArchetypes.Escort, ArchetypeInferrer.Infer(b));
    }

    [Fact]
    public void TriggerObjective_Alone_IsDeliver()
    {
        var b = Block(new LlmTriggerObjective("DockedWithSpaceStation", 1, "d"));
        Assert.Equal(MissionArchetypes.Deliver, ArchetypeInferrer.Infer(b));
    }

    [Fact]
    public void CollectTradeGoods_Alone_IsDeliver()
    {
        // TradeGoods / RefinedProduct don't spawn POIs — treated as deliver
        // archetype even without a separate TriggerObjective.
        var b = Block(new LlmCollectItemTypes("TradeGoods", 10, "d"));
        Assert.Equal(MissionArchetypes.Deliver, ArchetypeInferrer.Infer(b));
    }
}
