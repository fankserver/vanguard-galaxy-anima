using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using VGAnima.Llm;
using Xunit;

namespace VGAnima.Tests.Llm;

/// <summary>Validator coverage for the v2 intent-based mission schema.
/// Each step carries exactly one intent from <see cref="IntentWhitelist"/>;
/// the validator checks intent-specific field sets, hostility rules,
/// destination id membership, and archetype forbidden-list enforcement.</summary>
public class MissionBlockValidatorTests
{
    private static readonly IReadOnlyList<string> Hostiles =
        new[] { "Marauders", "Darkspacers" };

    private static readonly IReadOnlyDictionary<string, int> Rep =
        new Dictionary<string, int>
        {
            { "Marauders",    -10 },
            { "Darkspacers",  -50 },
            { "TradingGuild", 100 },
            { "PoliceGuild",   50 },
            { "Stranded",       0 },
        };

    private static readonly IReadOnlyList<AccessibleDestination> Destinations =
        new[]
        {
            new AccessibleDestination(
                ShortId: "dest_0", StationName: "Sarus Prime", SystemName: "Sarus",
                FactionIdentifier: "Gold", FactionDisplayName: "Luminate",
                JumpsAway: 1, SameFactionAsBroker: false, Guid: "guid_sarus"),
            new AccessibleDestination(
                ShortId: "dest_1", StationName: "Kelar Outpost", SystemName: "Kelar",
                FactionIdentifier: "Red", FactionDisplayName: "Kolyatov",
                JumpsAway: 1, SameFactionAsBroker: false, Guid: "guid_kelar"),
        };

    private static MissionBlockValidator Validator() => new();

    private static JObject ValidMission(JArray? steps = null, JArray? rewards = null)
    {
        steps ??= new JArray(
            new JObject
            {
                ["intent"]          = "gather_salvage",
                ["required_amount"] = 15,
                ["description"]     = "Scavenge the derelict.",
            });
        rewards ??= new JArray(
            new JObject { ["type"] = "Credits",    ["base_value"] = 30 },
            new JObject { ["type"] = "Experience", ["base_value"] = 50 });
        return new JObject
        {
            ["name"]            = "Test",
            ["description"]     = "x",
            ["completion_text"] = "y",
            ["source_faction"]  = "SalvageGuild",
            ["steps"]           = steps,
            ["rewards"]         = rewards,
        };
    }

    // -------- Envelope --------

    [Fact]
    public void Parse_MissionMustBeObject()
    {
        var ex = Assert.Throws<LlmValidationException>(
            () => Validator().Parse(JToken.Parse("[]"), Hostiles, Rep));
        Assert.Contains("object", ex.Message);
    }

    [Fact]
    public void Parse_RejectsUnknownTopLevelField()
    {
        var m = ValidMission();
        m["extra_field"] = "oops";
        var ex = Assert.Throws<LlmValidationException>(
            () => Validator().Parse(m, Hostiles, Rep));
        Assert.Contains("extra_field", ex.Message);
    }

    [Fact]
    public void Parse_RejectsMissingRequiredField()
    {
        var m = ValidMission();
        m.Remove("name");
        Assert.Throws<LlmValidationException>(
            () => Validator().Parse(m, Hostiles, Rep));
    }

    [Fact]
    public void Parse_NonAsciiInName_Rejects()
    {
        var m = ValidMission();
        m["name"] = "Too — fancy";   // em dash
        var ex = Assert.Throws<LlmValidationException>(
            () => Validator().Parse(m, Hostiles, Rep));
        Assert.Contains("non-ascii", ex.Message);
    }

    [Fact]
    public void Parse_TooLongDescription_Rejects()
    {
        var m = ValidMission();
        m["description"] = new string('x', MissionBlockValidator.DescriptionMaxLen + 1);
        Assert.Throws<LlmValidationException>(
            () => Validator().Parse(m, Hostiles, Rep));
    }

    [Fact]
    public void Parse_EmptyStringAfterTrim_Rejects()
    {
        var m = ValidMission();
        m["name"] = "   ";
        Assert.Throws<LlmValidationException>(
            () => Validator().Parse(m, Hostiles, Rep));
    }

    // -------- source_faction --------

    [Fact]
    public void Parse_UnknownSourceFaction_Rejects()
    {
        var m = ValidMission();
        m["source_faction"] = "Fakers";
        Assert.Throws<LlmValidationException>(
            () => Validator().Parse(m, Hostiles, Rep));
    }

    // -------- Steps envelope --------

    [Fact]
    public void Parse_EmptySteps_Rejects()
    {
        var m = ValidMission(steps: new JArray());
        Assert.Throws<LlmValidationException>(
            () => Validator().Parse(m, Hostiles, Rep));
    }

    [Fact]
    public void Parse_FourSteps_Rejects()
    {
        var s = new JObject
        {
            ["intent"] = "gather_salvage",
            ["required_amount"] = 5,
            ["description"] = "d",
        };
        var m = ValidMission(steps: new JArray(s, s.DeepClone(), s.DeepClone(), s.DeepClone()));
        Assert.Throws<LlmValidationException>(
            () => Validator().Parse(m, Hostiles, Rep));
    }

    [Fact]
    public void Parse_StepMissingIntent_Rejects()
    {
        var m = ValidMission(steps: new JArray(new JObject { ["description"] = "d" }));
        var ex = Assert.Throws<LlmValidationException>(
            () => Validator().Parse(m, Hostiles, Rep));
        Assert.Contains("intent", ex.Message);
    }

    [Fact]
    public void Parse_UnknownIntent_Rejects()
    {
        var m = ValidMission(steps: new JArray(new JObject
        {
            ["intent"]      = "build_deathstar",
            ["description"] = "d",
        }));
        var ex = Assert.Throws<LlmValidationException>(
            () => Validator().Parse(m, Hostiles, Rep));
        Assert.Contains("build_deathstar", ex.Message);
    }

    // -------- clear_combat_site --------

    [Fact]
    public void Parse_ClearCombatSite_Accepts()
    {
        var m = ValidMission(steps: new JArray(new JObject
        {
            ["intent"]        = "clear_combat_site",
            ["enemy_faction"] = "Marauders",
            ["description"]   = "Clear the blockade.",
        }));
        var block = Validator().Parse(m, Hostiles, Rep);
        var intent = Assert.IsType<ClearCombatSiteIntent>(block.Steps[0].Intent);
        Assert.Equal("Marauders", intent.EnemyFaction);
    }

    [Fact]
    public void Parse_ClearCombatSite_FriendlyFaction_Rejects()
    {
        var m = ValidMission(steps: new JArray(new JObject
        {
            ["intent"]        = "clear_combat_site",
            ["enemy_faction"] = "TradingGuild",      // friendly rep=100
            ["description"]   = "d",
        }));
        Assert.Throws<LlmValidationException>(
            () => Validator().Parse(m, Hostiles, Rep));
    }

    [Fact]
    public void Parse_ClearCombatSite_ExtraField_Rejects()
    {
        var m = ValidMission(steps: new JArray(new JObject
        {
            ["intent"]        = "clear_combat_site",
            ["enemy_faction"] = "Marauders",
            ["description"]   = "d",
            ["required_amount"] = 3,                 // not valid on this intent
        }));
        Assert.Throws<LlmValidationException>(
            () => Validator().Parse(m, Hostiles, Rep));
    }

    [Theory]
    [InlineData("scouting")]
    [InlineData("outpost")]
    [InlineData("lair")]
    [InlineData("raid")]
    [InlineData("cornered_remnants")]
    [InlineData("swarm")]
    public void Parse_ClearCombatSite_Flavor_Accepts(string flavor)
    {
        var m = ValidMission(steps: new JArray(new JObject
        {
            ["intent"]        = "clear_combat_site",
            ["enemy_faction"] = "Marauders",
            ["flavor"]        = flavor,
            ["description"]   = "Clear them.",
        }));
        var block  = Validator().Parse(m, Hostiles, Rep);
        var intent = Assert.IsType<ClearCombatSiteIntent>(block.Steps[0].Intent);
        Assert.Equal(flavor, intent.Flavor);
    }

    [Fact]
    public void Parse_ClearCombatSite_UnknownFlavor_Rejects()
    {
        var m = ValidMission(steps: new JArray(new JObject
        {
            ["intent"]        = "clear_combat_site",
            ["enemy_faction"] = "Marauders",
            ["flavor"]        = "kamikaze",          // not whitelisted
            ["description"]   = "d",
        }));
        var ex = Assert.Throws<LlmValidationException>(
            () => Validator().Parse(m, Hostiles, Rep));
        Assert.Contains("flavor", ex.Message);
    }

    [Fact]
    public void Parse_ClearCombatSite_NoFlavor_NullOnRecord()
    {
        // Omitting flavor is valid — factory falls back to the balanced
        // composition. Confirmed via Flavor == null on the record.
        var m = ValidMission(steps: new JArray(new JObject
        {
            ["intent"]        = "clear_combat_site",
            ["enemy_faction"] = "Marauders",
            ["description"]   = "d",
        }));
        var block  = Validator().Parse(m, Hostiles, Rep);
        var intent = Assert.IsType<ClearCombatSiteIntent>(block.Steps[0].Intent);
        Assert.Null(intent.Flavor);
    }

    // -------- gather_ore / gather_salvage --------

    [Fact]
    public void Parse_GatherOre_Accepts()
    {
        var m = ValidMission(steps: new JArray(new JObject
        {
            ["intent"]          = "gather_ore",
            ["required_amount"] = 10,
            ["description"]     = "Mine the field.",
        }));
        var block  = Validator().Parse(m, Hostiles, Rep);
        var intent = Assert.IsType<GatherOreIntent>(block.Steps[0].Intent);
        Assert.Equal(10, intent.RequiredAmount);
    }

    [Fact]
    public void Parse_GatherSalvage_Accepts()
    {
        var m = ValidMission(steps: new JArray(new JObject
        {
            ["intent"]          = "gather_salvage",
            ["required_amount"] = 12,
            ["description"]     = "Strip the hulks.",
        }));
        var block = Validator().Parse(m, Hostiles, Rep);
        Assert.IsType<GatherSalvageIntent>(block.Steps[0].Intent);
    }

    [Fact]
    public void Parse_GatherOre_AmountOutOfRange_Rejects()
    {
        var m = ValidMission(steps: new JArray(new JObject
        {
            ["intent"]          = "gather_ore",
            ["required_amount"] = 51,                // > max 50
            ["description"]     = "d",
        }));
        Assert.Throws<LlmValidationException>(
            () => Validator().Parse(m, Hostiles, Rep));
    }

    // -------- defended_gather_* --------

    [Fact]
    public void Parse_DefendedGatherSalvage_Accepts()
    {
        var m = ValidMission(steps: new JArray(new JObject
        {
            ["intent"]          = "defended_gather_salvage",
            ["required_amount"] = 15,
            ["guards_faction"]  = "Marauders",
            ["description"]     = "Fight and loot.",
        }));
        var block  = Validator().Parse(m, Hostiles, Rep);
        var intent = Assert.IsType<DefendedGatherSalvageIntent>(block.Steps[0].Intent);
        Assert.Equal("Marauders", intent.GuardsFaction);
    }

    [Fact]
    public void Parse_DefendedGatherOre_FriendlyGuards_Rejects()
    {
        var m = ValidMission(steps: new JArray(new JObject
        {
            ["intent"]          = "defended_gather_ore",
            ["required_amount"] = 10,
            ["guards_faction"]  = "PoliceGuild",     // friendly rep=50
            ["description"]     = "d",
        }));
        Assert.Throws<LlmValidationException>(
            () => Validator().Parse(m, Hostiles, Rep));
    }

    // -------- deliver_to_station --------

    [Fact]
    public void Parse_DeliverToStation_Accepts()
    {
        var m = ValidMission(steps: new JArray(new JObject
        {
            ["intent"]         = "deliver_to_station",
            ["destination_id"] = "dest_0",
            ["description"]    = "Courier run.",
        }));
        var block  = Validator().Parse(m, Hostiles, Rep,
            accessibleDestinations: Destinations);
        var intent = Assert.IsType<DeliverToStationIntent>(block.Steps[0].Intent);
        Assert.Equal("dest_0", intent.DestinationShortId);
    }

    [Fact]
    public void Parse_DeliverToStation_UnknownDestination_Rejects()
    {
        var m = ValidMission(steps: new JArray(new JObject
        {
            ["intent"]         = "deliver_to_station",
            ["destination_id"] = "dest_99",          // not in list
            ["description"]    = "d",
        }));
        Assert.Throws<LlmValidationException>(
            () => Validator().Parse(m, Hostiles, Rep,
                accessibleDestinations: Destinations));
    }

    [Fact]
    public void Parse_DeliverToStation_EmptyDestinationList_Rejects()
    {
        var m = ValidMission(steps: new JArray(new JObject
        {
            ["intent"]         = "deliver_to_station",
            ["destination_id"] = "dest_0",
            ["description"]    = "d",
        }));
        // No accessible_destinations passed — degenerate pocket-system case.
        Assert.Throws<LlmValidationException>(
            () => Validator().Parse(m, Hostiles, Rep));
    }

    // -------- haul_goods --------

    [Fact]
    public void Parse_HaulGoods_Accepts()
    {
        var m = ValidMission(steps: new JArray(new JObject
        {
            ["intent"]          = "haul_goods",
            ["required_amount"] = 10,
            ["destination_id"]  = "dest_1",
            ["description"]     = "Haul the cargo.",
        }));
        var block  = Validator().Parse(m, Hostiles, Rep,
            accessibleDestinations: Destinations);
        var intent = Assert.IsType<HaulGoodsIntent>(block.Steps[0].Intent);
        Assert.Equal(10, intent.RequiredAmount);
        Assert.Equal("dest_1", intent.DestinationShortId);
    }

    [Fact]
    public void Parse_HaulGoods_OverRange_Rejects()
    {
        var m = ValidMission(steps: new JArray(new JObject
        {
            ["intent"]          = "haul_goods",
            ["required_amount"] = 21,                // > haul cap 20
            ["destination_id"]  = "dest_0",
            ["description"]     = "d",
        }));
        Assert.Throws<LlmValidationException>(
            () => Validator().Parse(m, Hostiles, Rep,
                accessibleDestinations: Destinations));
    }

    // -------- forbidden_archetypes --------

    [Fact]
    public void Parse_CombatForbidden_BlocksClearCombatSite()
    {
        var m = ValidMission(steps: new JArray(new JObject
        {
            ["intent"]        = "clear_combat_site",
            ["enemy_faction"] = "Marauders",
            ["description"]   = "d",
        }));
        var ex = Assert.Throws<LlmValidationException>(
            () => Validator().Parse(m, Hostiles, Rep,
                forbiddenArchetypes: new[] { "combat" }));
        Assert.Contains("combat", ex.Message);
    }

    [Fact]
    public void Parse_CombatForbidden_BlocksDefendedGather()
    {
        // defended_* is composite (combat+gather); combat in forbidden
        // list must still block it.
        var m = ValidMission(steps: new JArray(new JObject
        {
            ["intent"]          = "defended_gather_salvage",
            ["required_amount"] = 10,
            ["guards_faction"]  = "Marauders",
            ["description"]     = "d",
        }));
        Assert.Throws<LlmValidationException>(
            () => Validator().Parse(m, Hostiles, Rep,
                forbiddenArchetypes: new[] { "combat" }));
    }

    [Fact]
    public void Parse_GatherForbidden_AllowsHaulGoods()
    {
        // haul_goods is a courier pitch — trade goods the broker hands
        // over for delivery, NOT ore mining. Mapped to `deliver` only
        // (2026-04-23 narrative-honesty refactor), so a `gather`
        // forbidden entry must NOT block it. Previously the mapping
        // was composite (gather+deliver) which gave haul_goods an
        // unearned coupling to the mining archetype.
        var m = ValidMission(steps: new JArray(new JObject
        {
            ["intent"]          = "haul_goods",
            ["required_amount"] = 10,
            ["destination_id"]  = "dest_0",
            ["description"]     = "d",
        }));
        // Should parse without throwing.
        Validator().Parse(m, Hostiles, Rep,
            forbiddenArchetypes: new[] { "gather" },
            accessibleDestinations: Destinations);
    }

    [Fact]
    public void Parse_DeliverForbidden_BlocksHaulGoods()
    {
        // haul_goods now maps to `deliver` only, so deliver-forbidden
        // is the ONE forbidden-archetype gate that can still block it.
        var m = ValidMission(steps: new JArray(new JObject
        {
            ["intent"]          = "haul_goods",
            ["required_amount"] = 10,
            ["destination_id"]  = "dest_0",
            ["description"]     = "d",
        }));
        Assert.Throws<LlmValidationException>(
            () => Validator().Parse(m, Hostiles, Rep,
                forbiddenArchetypes: new[] { "deliver" },
                accessibleDestinations: Destinations));
    }

    [Fact]
    public void Parse_CombatForbidden_AllowsPeacefulGather()
    {
        var m = ValidMission(steps: new JArray(new JObject
        {
            ["intent"]          = "gather_ore",
            ["required_amount"] = 10,
            ["description"]     = "d",
        }));
        var block = Validator().Parse(m, Hostiles, Rep,
            forbiddenArchetypes: new[] { "combat" });
        Assert.IsType<GatherOreIntent>(block.Steps[0].Intent);
    }

    // -------- multi-step --------

    [Fact]
    public void Parse_MultiStep_CombatThenSalvage_Accepts()
    {
        var m = ValidMission(steps: new JArray(
            new JObject
            {
                ["intent"]        = "clear_combat_site",
                ["enemy_faction"] = "Marauders",
                ["description"]   = "Clear.",
            },
            new JObject
            {
                ["intent"]          = "gather_salvage",
                ["required_amount"] = 10,
                ["description"]     = "Loot.",
            }));
        var block = Validator().Parse(m, Hostiles, Rep);
        Assert.Equal(2, block.Steps.Count);
        Assert.IsType<ClearCombatSiteIntent>(block.Steps[0].Intent);
        Assert.IsType<GatherSalvageIntent>(block.Steps[1].Intent);
    }

    // -------- rewards --------

    [Fact]
    public void Parse_NoRewards_Rejects()
    {
        var m = ValidMission(rewards: new JArray());
        Assert.Throws<LlmValidationException>(
            () => Validator().Parse(m, Hostiles, Rep));
    }

    [Fact]
    public void Parse_SixRewards_Rejects()
    {
        var r = new JObject { ["type"] = "Credits", ["base_value"] = 20 };
        var m = ValidMission(rewards: new JArray(
            r, r.DeepClone(), r.DeepClone(), r.DeepClone(), r.DeepClone(), r.DeepClone()));
        Assert.Throws<LlmValidationException>(
            () => Validator().Parse(m, Hostiles, Rep));
    }

    [Fact]
    public void Parse_CreditsRewardOutOfRange_Rejects()
    {
        var m = ValidMission(rewards: new JArray(new JObject
        {
            ["type"] = "Credits", ["base_value"] = 101,
        }));
        Assert.Throws<LlmValidationException>(
            () => Validator().Parse(m, Hostiles, Rep));
    }

    [Fact]
    public void Parse_ReputationReward_UnknownFaction_Rejects()
    {
        var m = ValidMission(rewards: new JArray(new JObject
        {
            ["type"] = "Reputation", ["faction"] = "Fakers", ["amount"] = 200,
        }));
        Assert.Throws<LlmValidationException>(
            () => Validator().Parse(m, Hostiles, Rep));
    }

    [Fact]
    public void Parse_ItemReward_MiningClaim_Accepts()
    {
        var m = ValidMission(rewards: new JArray(new JObject
        {
            ["type"] = "Item", ["kind"] = "MiningClaim",
        }));
        var block = Validator().Parse(m, Hostiles, Rep);
        var item  = Assert.IsType<LlmItemReward>(block.Rewards[0]);
        Assert.Equal("MiningClaim", item.Kind);
    }

    [Fact]
    public void Parse_ItemReward_UnknownKind_Rejects()
    {
        var m = ValidMission(rewards: new JArray(new JObject
        {
            ["type"] = "Item", ["kind"] = "DeathStar",
        }));
        Assert.Throws<LlmValidationException>(
            () => Validator().Parse(m, Hostiles, Rep));
    }
}
