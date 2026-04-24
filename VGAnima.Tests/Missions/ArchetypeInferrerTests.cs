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
    public void GatherOre_Alone_IsMining()
    {
        var b = Block(new GatherOreIntent(10, "desc"));
        Assert.Equal(MissionArchetypes.Mining, ArchetypeInferrer.Infer(b));
    }

    [Fact]
    public void GatherSalvage_Alone_IsSalvage()
    {
        var b = Block(new GatherSalvageIntent(10, "desc"));
        Assert.Equal(MissionArchetypes.Salvage, ArchetypeInferrer.Infer(b));
    }

    [Fact]
    public void DefendedGatherSalvage_IsSalvage()
    {
        // Economic identity wins: the player's narrative self is a
        // salvager who also fought defenders, not a fighter who
        // happened to salvage.
        var b = Block(new DefendedGatherSalvageIntent(10, "Marauders", "desc"));
        Assert.Equal(MissionArchetypes.Salvage, ArchetypeInferrer.Infer(b));
    }

    [Fact]
    public void DefendedGatherOre_IsMining()
    {
        var b = Block(new DefendedGatherOreIntent(10, "Marauders", "desc"));
        Assert.Equal(MissionArchetypes.Mining, ArchetypeInferrer.Infer(b));
    }

    [Fact]
    public void MultiStep_CombatThenSalvage_IsSalvage()
    {
        // Cross-step combat + salvage → salvage (economic identity).
        // The combat step is framing, the salvage is the deliverable.
        var b = Block(
            new ClearCombatSiteIntent("Marauders", "clear"),
            new GatherSalvageIntent(10, "collect"));
        Assert.Equal(MissionArchetypes.Salvage, ArchetypeInferrer.Infer(b));
    }

    [Fact]
    public void MultiStep_CombatThenOre_IsMining()
    {
        var b = Block(
            new ClearCombatSiteIntent("Marauders", "clear"),
            new GatherOreIntent(10, "collect"));
        Assert.Equal(MissionArchetypes.Mining, ArchetypeInferrer.Infer(b));
    }

    [Fact]
    public void HaulGoods_Alone_IsTrade()
    {
        // Commodity turn-in (Mining objective with TradeGoods category +
        // TravelToPOI) = trade, not deliver. The player hauled goods
        // between markets, they didn't just dock somewhere.
        var b = Block(new HaulGoodsIntent(10, "dest_0", "d"));
        Assert.Equal(MissionArchetypes.Trade, ArchetypeInferrer.Infer(b));
    }

    [Fact]
    public void DeliverToStation_Alone_IsDeliver()
    {
        var b = Block(new DeliverToStationIntent("dest_0", "d"));
        Assert.Equal(MissionArchetypes.Deliver, ArchetypeInferrer.Infer(b));
    }

    [Fact]
    public void EmptyBlock_IsOther()
    {
        var b = new LlmMissionBlock(
            Name: "Empty", Description: "x", CompletionText: "x",
            SourceFaction: "SalvageGuild",
            Steps:   new List<LlmMissionStep>(),
            Rewards: new List<LlmReward>());
        Assert.Equal(MissionArchetypes.Other, ArchetypeInferrer.Infer(b));
    }
}
