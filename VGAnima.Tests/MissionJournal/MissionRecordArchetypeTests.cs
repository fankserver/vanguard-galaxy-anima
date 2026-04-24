using System.Collections.Generic;
using VGAnima.MissionJournal;
using VGMissionJournal.Logging;
using Xunit;

namespace VGAnima.Tests.MissionJournal;

public class MissionRecordArchetypeTests
{
    // ---- Single-objective → single tag ----

    [Fact]
    public void ObjectiveTags_KillEnemies()
    {
        Assert.Equal(new[] { "kill_enemies" }, Tags(Obj("KillEnemies")));
    }

    [Fact]
    public void ObjectiveTags_ProtectUnit()
    {
        Assert.Equal(new[] { "protect_unit" }, Tags(Obj("ProtectUnit")));
    }

    [Fact]
    public void ObjectiveTags_Salvage_RealClass()
    {
        // Post-AT-T1, VGAnima's factory emits the real Salvage subclass
        // for gather_salvage. Vanilla's own SalvageWreck missions do the
        // same. Type="Salvage" routes directly without peeking at fields.
        Assert.Equal(new[] { "collect_salvage" }, Tags(Obj("Salvage")));
    }

    [Fact]
    public void ObjectiveTags_CollectItemTypes()
    {
        Assert.Equal(new[] { "collect_items" }, Tags(Obj("CollectItemTypes")));
    }

    [Fact]
    public void ObjectiveTags_TravelToPOI()
    {
        Assert.Equal(new[] { "travel" }, Tags(Obj("TravelToPOI")));
    }

    // ---- Mining class + itemCategory disambiguator ----

    [Fact]
    public void ObjectiveTags_Mining_NoCategory_DefaultsToOre()
    {
        // Mining class with no itemCategory field — vanilla's default
        // interpretation (the class name's namesake).
        Assert.Equal(new[] { "mine_ore" }, Tags(Obj("Mining")));
    }

    [Fact]
    public void ObjectiveTags_Mining_OreCategory()
    {
        Assert.Equal(new[] { "mine_ore" }, Tags(Obj("Mining", cat: "Ore")));
    }

    [Fact]
    public void ObjectiveTags_Mining_SalvageCategory_LegacyPath()
    {
        // Pre-AT-T1 VGAnima records + any other producer that uses
        // Mining+Salvage land on collect_salvage via the field peek.
        Assert.Equal(new[] { "collect_salvage" }, Tags(Obj("Mining", cat: "Salvage")));
    }

    [Fact]
    public void ObjectiveTags_Mining_TradeGoodsCategory()
    {
        Assert.Equal(new[] { "haul_goods" }, Tags(Obj("Mining", cat: "TradeGoods")));
    }

    [Fact]
    public void ObjectiveTags_Mining_RefinedProductCategory()
    {
        Assert.Equal(new[] { "haul_goods" }, Tags(Obj("Mining", cat: "RefinedProduct")));
    }

    [Fact]
    public void ObjectiveTags_Mining_UnknownCategory_DefaultsToOre()
    {
        Assert.Equal(new[] { "mine_ore" }, Tags(Obj("Mining", cat: "Junk")));
    }

    // ---- Multi-objective: dedup + sort ----

    [Fact]
    public void ObjectiveTags_DefendedSalvage_YieldsBothTagsSorted()
    {
        // A defended-salvage mission (one step with salvage objective +
        // another step with KillEnemies) surfaces both tags. Alphabetical
        // sort puts collect_salvage before kill_enemies.
        var record = Record(
            Step(Obj("KillEnemies")),
            Step(Obj("Salvage")));
        Assert.Equal(new[] { "collect_salvage", "kill_enemies" },
            MissionRecordArchetype.ObjectiveTags(record));
    }

    [Fact]
    public void ObjectiveTags_DuplicateObjective_DedupedToOneTag()
    {
        var record = Record(
            Step(Obj("KillEnemies")),
            Step(Obj("KillEnemies")));
        Assert.Equal(new[] { "kill_enemies" },
            MissionRecordArchetype.ObjectiveTags(record));
    }

    [Fact]
    public void ObjectiveTags_UnknownType_Ignored()
    {
        // TriggerObjective / Reputation / CollectCredits etc. don't map
        // to any canonical tag — they drop out silently rather than
        // forcing an "other" bucket.
        Assert.Empty(Tags(Obj("TriggerObjective")));
    }

    [Fact]
    public void ObjectiveTags_EmptySteps_ReturnsEmpty()
    {
        Assert.Empty(MissionRecordArchetype.ObjectiveTags(Record()));
    }

    // ---- Fixtures ----

    private static IReadOnlyList<string> Tags(MissionObjectiveDefinition obj) =>
        MissionRecordArchetype.ObjectiveTags(Record(Step(obj)));

    private static MissionObjectiveDefinition Obj(string type, string? cat = null)
    {
        IReadOnlyDictionary<string, object?>? fields = cat is null
            ? null
            : new Dictionary<string, object?> { ["itemCategory"] = cat };
        return new MissionObjectiveDefinition(type, fields);
    }

    private static MissionStepDefinition Step(params MissionObjectiveDefinition[] objectives) =>
        new(Description: null, RequireAllObjectives: true, Hidden: false,
            Objectives: objectives);

    private static MissionRecord Record(params MissionStepDefinition[] steps) =>
        new(
            StoryId: "",
            MissionInstanceId: "test",
            MissionName: "Test",
            MissionSubclass: "Mission",
            MissionLevel: 10,
            SourceStationId: null, SourceStationName: null,
            SourceSystemId: null, SourceSystemName: null,
            SourceSectorId: null, SourceSectorName: null,
            SourceFaction: null,
            TargetStationId: null, TargetStationName: null, TargetSystemId: null,
            PlayerLevel: 0, PlayerShipName: null, PlayerShipLevel: null,
            PlayerCurrentSystemId: null,
            Steps: steps,
            Rewards: new List<MissionRewardSnapshot>(),
            Timeline: new List<TimelineEntry>
            {
                new(TimelineState.Accepted, 0, "x"),
                new(TimelineState.Completed, 10, "x"),
            });
}
