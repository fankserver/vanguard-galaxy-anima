using System.Collections.Generic;
using VGAnima.Llm;
using VGAnima.Missions;
using VGAnima.Persistence;
using Xunit;

namespace VGAnima.Tests.Missions;

public class ArchetypeInferrerTests
{
    private static LlmMissionBlock Block(params LlmIntent[] intents)
    {
        var steps = new List<LlmMissionStep>(intents.Length);
        foreach (var i in intents) steps.Add(new LlmMissionStep(i));
        return new LlmMissionBlock(
            Name: "Test", Description: "x", CompletionText: "x",
            SourceFaction: "SalvageGuild",
            Steps:   steps,
            Rewards: new List<LlmReward>());
    }

    [Fact]
    public void ClearCombatSite_Alone_IsCombat()
    {
        var b = Block(new ClearCombatSiteIntent("Marauders", "desc"));
        Assert.Equal(MissionArchetypes.Combat, ArchetypeInferrer.Infer(b));
    }

    [Fact]
    public void GatherOre_Alone_IsGather()
    {
        var b = Block(new GatherOreIntent(10, "desc"));
        Assert.Equal(MissionArchetypes.Gather, ArchetypeInferrer.Infer(b));
    }

    [Fact]
    public void GatherSalvage_Alone_IsSalvage()
    {
        var b = Block(new GatherSalvageIntent(10, "desc"));
        Assert.Equal(MissionArchetypes.Salvage, ArchetypeInferrer.Infer(b));
    }

    [Fact]
    public void DefendedGatherSalvage_IsDefendedCollect()
    {
        // Single-step defended gather: the intent itself is the hybrid
        // shape, no separate combat step needed.
        var b = Block(new DefendedGatherSalvageIntent(10, "Marauders", "desc"));
        Assert.Equal(MissionArchetypes.DefendedCollect, ArchetypeInferrer.Infer(b));
    }

    [Fact]
    public void DefendedGatherOre_IsDefendedCollect()
    {
        var b = Block(new DefendedGatherOreIntent(10, "Marauders", "desc"));
        Assert.Equal(MissionArchetypes.DefendedCollect, ArchetypeInferrer.Infer(b));
    }

    [Fact]
    public void MultiStep_CombatThenSalvage_IsDefendedCollect()
    {
        // Cross-step combat + gather should also collapse to the hybrid
        // tag — journal queries asking for combat OR salvage history match.
        var b = Block(
            new ClearCombatSiteIntent("Marauders", "clear"),
            new GatherSalvageIntent(10, "collect"));
        Assert.Equal(MissionArchetypes.DefendedCollect, ArchetypeInferrer.Infer(b));
    }

    [Fact]
    public void MultiStep_CombatThenOre_IsDefendedCollect()
    {
        var b = Block(
            new ClearCombatSiteIntent("Marauders", "clear"),
            new GatherOreIntent(10, "collect"));
        Assert.Equal(MissionArchetypes.DefendedCollect, ArchetypeInferrer.Infer(b));
    }

    [Fact]
    public void DeliverToStation_Alone_IsDeliver()
    {
        var b = Block(new DeliverToStationIntent("dest_0", "d"));
        Assert.Equal(MissionArchetypes.Deliver, ArchetypeInferrer.Infer(b));
    }

    [Fact]
    public void HaulGoods_Alone_IsDeliver()
    {
        // HaulGoods is a composite (gather trade goods + deliver), but the
        // resolved archetype is Deliver since there's no Ore/Salvage POI.
        var b = Block(new HaulGoodsIntent(10, "dest_0", "d"));
        Assert.Equal(MissionArchetypes.Deliver, ArchetypeInferrer.Infer(b));
    }

    [Fact]
    public void EmptyBlock_IsOther()
    {
        // Degenerate fallback — should never happen in production (validator
        // requires >=1 step) but the inferrer handles it gracefully.
        var b = new LlmMissionBlock(
            Name: "Empty", Description: "x", CompletionText: "x",
            SourceFaction: "SalvageGuild",
            Steps:   new List<LlmMissionStep>(),
            Rewards: new List<LlmReward>());
        Assert.Equal(MissionArchetypes.Other, ArchetypeInferrer.Infer(b));
    }
}
