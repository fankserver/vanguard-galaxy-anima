using System.Collections.Generic;
using VGAnima.Llm;
using Xunit;

namespace VGAnima.Tests.Llm;

public class StationConditionInferrerTests
{
    private static LlmContext Ctx(
        IReadOnlyDictionary<string, LlmFactionEntry>? factions = null,
        IReadOnlyList<string>? forbiddenArchetypes = null,
        IReadOnlyList<string>? stationFacilities = null)
    {
        return new LlmContext
        {
            Factions = factions ?? new Dictionary<string, LlmFactionEntry>(),
            MissionGuidance = new LlmMissionGuidance
            {
                ArchetypeWeights = new Dictionary<string, double>(),
                ForbiddenArchetypes = forbiddenArchetypes ?? System.Array.Empty<string>(),
                Rationale = System.Array.Empty<string>(),
            },
            Location = new LlmLocationSection
            {
                StationFacilities = stationFacilities ?? new[] { "Bar", "MissionBoard" },
            },
        };
    }

    private static LlmFactionEntry F(string display, string rel, int rep) =>
        new(display, rel, rep);

    [Fact]
    public void Infer_HostileFactionPresent_YieldsWarTorn()
    {
        var factions = new Dictionary<string, LlmFactionEntry>
        {
            { "Marauders",    F("Corsair Syndicate", "hostile",  -6000) },
            { "SalvageGuild", F("Steel Vultures",    "friendly",  670)  },
        };
        Assert.Equal(StationConditionInferrer.WarTorn,
                     StationConditionInferrer.Infer(Ctx(factions: factions)));
    }

    [Fact]
    public void Infer_CombatForbidden_OverridesWarTornToPeaceful()
    {
        // combat-forbidden means the guidance builder decided combat isn't
        // viable here despite hostiles in the dict. War-torn vibe is
        // suppressed in favor of peaceful — mirrors scenario C semantics.
        var factions = new Dictionary<string, LlmFactionEntry>
        {
            { "Marauders", F("Corsair Syndicate", "hostile", -6000) },
        };
        Assert.Equal(StationConditionInferrer.Peaceful,
                     StationConditionInferrer.Infer(Ctx(
                         factions: factions,
                         forbiddenArchetypes: new[] { "combat" })));
    }

    [Fact]
    public void Infer_NoHostileNoCombatForbidden_LooksAtFacilities()
    {
        // With no hostiles + no combat-forbidden, facility count decides.
        var manyFacilities = new[]
        {
            "Bar", "Shipyard", "Refinery", "MissionBoard", "TradeCenter",
        };
        var result = StationConditionInferrer.Infer(Ctx(stationFacilities: manyFacilities));
        Assert.Equal(StationConditionInferrer.Bustling, result);
    }

    [Fact]
    public void Infer_FewFacilities_YieldsFrontier()
    {
        var few = new[] { "Bar", "MissionBoard" };
        var result = StationConditionInferrer.Infer(Ctx(stationFacilities: few));
        Assert.Equal(StationConditionInferrer.Frontier, result);
    }

    [Fact]
    public void Infer_MidFacilityCount_YieldsNormal()
    {
        var mid = new[] { "Bar", "MissionBoard", "Shipyard", "Refinery" };
        var result = StationConditionInferrer.Infer(Ctx(stationFacilities: mid));
        Assert.Equal(StationConditionInferrer.Normal, result);
    }

    [Fact]
    public void Infer_PeacefulFiresEvenOnFrontierStation()
    {
        // Priority: peaceful > bustling > frontier > normal. combat-
        // forbidden wins even over 1-facility frontier.
        var factions = new Dictionary<string, LlmFactionEntry>();
        Assert.Equal(StationConditionInferrer.Peaceful,
                     StationConditionInferrer.Infer(Ctx(
                         factions: factions,
                         forbiddenArchetypes: new[] { "combat", "escort" },
                         stationFacilities: new[] { "Bar" })));
    }

    [Fact]
    public void Infer_HostileButZeroReputationValue_StillCountsAsHostile()
    {
        // Relation is the source of truth, not the numeric rep value.
        // (FactionData.IsEnemy returns true on at-war regardless of rep.)
        var factions = new Dictionary<string, LlmFactionEntry>
        {
            { "Fanatics", F("Meridia's Chosen", "hostile", 0) },
        };
        Assert.Equal(StationConditionInferrer.WarTorn,
                     StationConditionInferrer.Infer(Ctx(factions: factions)));
    }
}
