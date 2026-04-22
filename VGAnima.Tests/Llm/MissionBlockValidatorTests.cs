using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using VGAnima.Llm;
using Xunit;

namespace VGAnima.Tests.Llm;

public class MissionBlockValidatorTests
{
    /// <summary>Context subset used for cross-checks (enemy_faction hostility).
    /// Plain record, not a seam over IGameStateView — the validator only needs
    /// AtWar + Reputation lookups, both supplied at call time.</summary>
    private static readonly IReadOnlyList<string> Hostiles =
        new[] { "Marauders", "Darkspacers" };

    private static readonly IReadOnlyDictionary<string, int> NeutralAndHostileRep =
        new Dictionary<string, int>
        {
            { "Marauders",       -10 },
            { "Darkspacers",     -50 },
            { "TradingGuild",    100 },
            { "PoliceGuild",      50 },
            { "BountyGuild",      20 },
            { "Stranded",          0 },
            { "MiningGuild",       0 },
        };

    private static MissionBlockValidator Validator() => new();

    // ---------- Rule 1: mission must be a JSON object ----------

    [Fact]
    public void Parse_WhenMissionIsArray_Rejects()
    {
        var mission = JToken.Parse("[]");
        var ex = Assert.Throws<LlmValidationException>(
            () => Validator().Parse(mission, Hostiles, NeutralAndHostileRep));
        Assert.Contains("object", ex.Message);
    }

    [Fact]
    public void Parse_WhenMissionIsString_Rejects()
    {
        var mission = JToken.Parse("\"oops\"");
        Assert.Throws<LlmValidationException>(
            () => Validator().Parse(mission, Hostiles, NeutralAndHostileRep));
    }

    [Fact]
    public void Parse_WhenMissionIsNull_Rejects()
    {
        var mission = JToken.Parse("null");
        Assert.Throws<LlmValidationException>(
            () => Validator().Parse(mission, Hostiles, NeutralAndHostileRep));
    }

    // ---------- Rule 2: strict field set ----------

    [Fact]
    public void Parse_MissingName_Rejects()
    {
        var mission = BuildWithoutField("name");
        var ex = Assert.Throws<LlmValidationException>(
            () => Validator().Parse(mission, Hostiles, NeutralAndHostileRep));
        Assert.Contains("name", ex.Message);
    }

    [Fact]
    public void Parse_MissingDescription_Rejects()
    {
        var mission = BuildWithoutField("description");
        Assert.Throws<LlmValidationException>(
            () => Validator().Parse(mission, Hostiles, NeutralAndHostileRep));
    }

    [Fact]
    public void Parse_MissingCompletionText_Rejects()
    {
        var mission = BuildWithoutField("completion_text");
        Assert.Throws<LlmValidationException>(
            () => Validator().Parse(mission, Hostiles, NeutralAndHostileRep));
    }

    [Fact]
    public void Parse_MissingSourceFaction_Rejects()
    {
        var mission = BuildWithoutField("source_faction");
        Assert.Throws<LlmValidationException>(
            () => Validator().Parse(mission, Hostiles, NeutralAndHostileRep));
    }

    [Fact]
    public void Parse_MissingSteps_Rejects()
    {
        var mission = BuildWithoutField("steps");
        Assert.Throws<LlmValidationException>(
            () => Validator().Parse(mission, Hostiles, NeutralAndHostileRep));
    }

    [Fact]
    public void Parse_MissingRewards_Rejects()
    {
        var mission = BuildWithoutField("rewards");
        Assert.Throws<LlmValidationException>(
            () => Validator().Parse(mission, Hostiles, NeutralAndHostileRep));
    }

    [Fact]
    public void Parse_ExtraTopLevelField_Rejects()
    {
        var mission = BuildGood();
        mission["extra_key"] = "stuff";
        var ex = Assert.Throws<LlmValidationException>(
            () => Validator().Parse(mission, Hostiles, NeutralAndHostileRep));
        Assert.Contains("extra_key", ex.Message);
    }

    // ---------- Rule 3: string caps + ASCII ----------

    [Fact]
    public void Parse_NameTooLong_Rejects()
    {
        var mission = BuildGood();
        mission["name"] = new string('x', 61);
        Assert.Throws<LlmValidationException>(
            () => Validator().Parse(mission, Hostiles, NeutralAndHostileRep));
    }

    [Fact]
    public void Parse_DescriptionTooLong_Rejects()
    {
        var mission = BuildGood();
        mission["description"] = new string('x', 501);
        Assert.Throws<LlmValidationException>(
            () => Validator().Parse(mission, Hostiles, NeutralAndHostileRep));
    }

    [Fact]
    public void Parse_CompletionTextTooLong_Rejects()
    {
        var mission = BuildGood();
        mission["completion_text"] = new string('x', 201);
        Assert.Throws<LlmValidationException>(
            () => Validator().Parse(mission, Hostiles, NeutralAndHostileRep));
    }

    [Fact]
    public void Parse_NameEmpty_Rejects()
    {
        var mission = BuildGood();
        mission["name"] = "   ";
        Assert.Throws<LlmValidationException>(
            () => Validator().Parse(mission, Hostiles, NeutralAndHostileRep));
    }

    [Fact]
    public void Parse_NonAsciiInName_Rejects()
    {
        var mission = BuildGood();
        mission["name"] = "Name \u2014 em-dash";
        var ex = Assert.Throws<LlmValidationException>(
            () => Validator().Parse(mission, Hostiles, NeutralAndHostileRep));
        Assert.Contains("ascii", ex.Message, System.StringComparison.OrdinalIgnoreCase);
    }

    // ---------- Rule 4: source_faction whitelist ----------

    [Fact]
    public void Parse_SourceFactionUnknown_Rejects()
    {
        var mission = BuildGood();
        mission["source_faction"] = "NotAFaction";
        var ex = Assert.Throws<LlmValidationException>(
            () => Validator().Parse(mission, Hostiles, NeutralAndHostileRep));
        Assert.Contains("source_faction", ex.Message);
    }

    [Fact]
    public void Parse_SourceFactionPlayer_Rejects()
    {
        var mission = BuildGood();
        mission["source_faction"] = "Player";
        Assert.Throws<LlmValidationException>(
            () => Validator().Parse(mission, Hostiles, NeutralAndHostileRep));
    }

    // ---------- Rule 5: steps array size ----------

    [Fact]
    public void Parse_StepsEmpty_Rejects()
    {
        var mission = BuildGood();
        mission["steps"] = new JArray();
        Assert.Throws<LlmValidationException>(
            () => Validator().Parse(mission, Hostiles, NeutralAndHostileRep));
    }

    [Fact]
    public void Parse_StepsTooMany_Rejects()
    {
        var mission = BuildGood();
        mission["steps"] = new JArray(BuildStep(), BuildStep(), BuildStep(), BuildStep());
        Assert.Throws<LlmValidationException>(
            () => Validator().Parse(mission, Hostiles, NeutralAndHostileRep));
    }

    [Fact]
    public void Parse_StepNotObject_Rejects()
    {
        var mission = BuildGood();
        mission["steps"] = new JArray("not-a-step");
        Assert.Throws<LlmValidationException>(
            () => Validator().Parse(mission, Hostiles, NeutralAndHostileRep));
    }

    [Fact]
    public void Parse_StepObjectivesEmpty_Rejects()
    {
        var mission = BuildGood();
        var step = new JObject { ["objectives"] = new JArray() };
        mission["steps"] = new JArray(step);
        Assert.Throws<LlmValidationException>(
            () => Validator().Parse(mission, Hostiles, NeutralAndHostileRep));
    }

    [Fact]
    public void Parse_StepObjectivesTooMany_Rejects()
    {
        var mission = BuildGood();
        var step = new JObject
        {
            ["objectives"] = new JArray(
                BuildTriggerObj(), BuildTriggerObj(), BuildTriggerObj()),
        };
        mission["steps"] = new JArray(step);
        Assert.Throws<LlmValidationException>(
            () => Validator().Parse(mission, Hostiles, NeutralAndHostileRep));
    }

    // ---------- Rule 7: per-objective type dispatch ----------

    [Fact]
    public void Parse_ObjectiveUnknownType_Rejects()
    {
        var mission = BuildGood();
        var bad = new JObject { ["type"] = "TradeOffer" };
        mission["steps"] = new JArray(new JObject
        {
            ["objectives"] = new JArray(bad),
        });
        var ex = Assert.Throws<LlmValidationException>(
            () => Validator().Parse(mission, Hostiles, NeutralAndHostileRep));
        Assert.Contains("TradeOffer", ex.Message);
    }

    // ---------- KillEnemies per-type rules ----------

    [Fact]
    public void Parse_KillEnemies_HappyPath()
    {
        var mission = BuildGood();
        var kill = new JObject
        {
            ["type"]            = "KillEnemies",
            ["enemy_faction"]   = "Marauders",
            ["required_amount"] = 3,
            ["description"]     = "Kill three Marauders.",
        };
        mission["steps"] = new JArray(new JObject
        {
            ["objectives"] = new JArray(kill),
        });
        var block = Validator().Parse(mission, Hostiles, NeutralAndHostileRep);
        Assert.Single(block.Steps);
        var obj = Assert.IsType<LlmKillEnemies>(block.Steps[0].Objectives[0]);
        Assert.Equal("Marauders", obj.EnemyFaction);
        Assert.Equal(3, obj.RequiredAmount);
    }

    [Fact]
    public void Parse_KillEnemies_RequiredAmountZero_Rejects()
    {
        var mission = BuildGood();
        var kill = new JObject
        {
            ["type"]            = "KillEnemies",
            ["enemy_faction"]   = "Marauders",
            ["required_amount"] = 0,
            ["description"]     = "Kill none.",
        };
        mission["steps"] = new JArray(new JObject
        {
            ["objectives"] = new JArray(kill),
        });
        Assert.Throws<LlmValidationException>(
            () => Validator().Parse(mission, Hostiles, NeutralAndHostileRep));
    }

    [Fact]
    public void Parse_KillEnemies_RequiredAmountTooBig_Rejects()
    {
        var mission = BuildGood();
        var kill = new JObject
        {
            ["type"]            = "KillEnemies",
            ["enemy_faction"]   = "Marauders",
            ["required_amount"] = 6,
            ["description"]     = "Kill six.",
        };
        mission["steps"] = new JArray(new JObject
        {
            ["objectives"] = new JArray(kill),
        });
        Assert.Throws<LlmValidationException>(
            () => Validator().Parse(mission, Hostiles, NeutralAndHostileRep));
    }

    [Fact]
    public void Parse_KillEnemies_EnemyFactionFriendly_Rejects()
    {
        var mission = BuildGood();
        var kill = new JObject
        {
            ["type"]            = "KillEnemies",
            ["enemy_faction"]   = "TradingGuild",  // +100 rep, not in Hostiles
            ["required_amount"] = 2,
            ["description"]     = "Kill allies.",
        };
        mission["steps"] = new JArray(new JObject
        {
            ["objectives"] = new JArray(kill),
        });
        var ex = Assert.Throws<LlmValidationException>(
            () => Validator().Parse(mission, Hostiles, NeutralAndHostileRep));
        Assert.Contains("enemy_faction", ex.Message);
    }

    [Fact]
    public void Parse_KillEnemies_EnemyFactionNeutralRejected()
    {
        // Vanilla FactionData.IsEnemy: hostile iff at_war OR rep < -500.
        // A rep=0 faction not on the atWar list is NEUTRAL, not hostile —
        // the validator refuses to license a kill mission against them.
        // Same rejection applies to the "don't-like" band (rep in -500..-1).
        var mission = BuildGood();
        var kill = new JObject
        {
            ["type"]            = "KillEnemies",
            ["enemy_faction"]   = "MiningGuild",  // rep 0, not at-war
            ["required_amount"] = 2,
            ["description"]     = "Kill neutrals.",
        };
        mission["steps"] = new JArray(new JObject
        {
            ["objectives"] = new JArray(kill),
        });
        var ex = Assert.Throws<LlmValidationException>(
            () => Validator().Parse(mission, Hostiles, NeutralAndHostileRep));
        Assert.Contains("not hostile", ex.Message);
    }

    [Fact]
    public void Parse_KillEnemies_EnemyFactionShallowNegativeRejected()
    {
        // Marauders in Hostiles gets through via at_war. But a don't-like
        // faction (rep in (-500, 0)) with NO at_war entry is still neutral
        // in vanilla's book. Confirm rejection.
        var mission = BuildGood();
        var kill = new JObject
        {
            ["type"]            = "KillEnemies",
            // Darkspacers is in Hostiles (atWar); swap to something not in it
            // by using a fresh rep dict that puts Fanatics at -300.
            ["enemy_faction"]   = "Fanatics",
            ["required_amount"] = 2,
            ["description"]     = "Premature kill.",
        };
        mission["steps"] = new JArray(new JObject
        {
            ["objectives"] = new JArray(kill),
        });
        var rep = new Dictionary<string, int> { ["Fanatics"] = -300 };
        var ex = Assert.Throws<LlmValidationException>(
            () => Validator().Parse(mission, new List<string>(), rep));
        Assert.Contains("not hostile", ex.Message);
    }

    [Fact]
    public void Parse_KillEnemies_EnemyFactionDeepNegativeAccepted()
    {
        // rep < -500 → hostile per vanilla — allowed even without at_war.
        var mission = BuildGood();
        var kill = new JObject
        {
            ["type"]            = "KillEnemies",
            ["enemy_faction"]   = "Fanatics",
            ["required_amount"] = 2,
            ["description"]     = "Kill zealots.",
        };
        mission["steps"] = new JArray(new JObject
        {
            ["objectives"] = new JArray(kill),
        });
        var rep = new Dictionary<string, int> { ["Fanatics"] = -6000 };
        var block = Validator().Parse(mission, new List<string>(), rep);
        Assert.Single(block.Steps);
    }

    [Fact]
    public void Parse_KillEnemies_EnemyFactionUnknown_Rejects()
    {
        var mission = BuildGood();
        var kill = new JObject
        {
            ["type"]            = "KillEnemies",
            ["enemy_faction"]   = "NotAFaction",
            ["required_amount"] = 2,
            ["description"]     = "Kill.",
        };
        mission["steps"] = new JArray(new JObject
        {
            ["objectives"] = new JArray(kill),
        });
        Assert.Throws<LlmValidationException>(
            () => Validator().Parse(mission, Hostiles, NeutralAndHostileRep));
    }

    [Fact]
    public void Parse_KillEnemies_DescriptionTooLong_Rejects()
    {
        var mission = BuildGood();
        var kill = new JObject
        {
            ["type"]            = "KillEnemies",
            ["enemy_faction"]   = "Marauders",
            ["required_amount"] = 1,
            ["description"]     = new string('x', 121),
        };
        mission["steps"] = new JArray(new JObject
        {
            ["objectives"] = new JArray(kill),
        });
        Assert.Throws<LlmValidationException>(
            () => Validator().Parse(mission, Hostiles, NeutralAndHostileRep));
    }

    // ---------- ProtectUnit per-type rules ----------

    [Fact]
    public void Parse_ProtectUnit_HappyPath()
    {
        var mission = BuildGood();
        var protect = new JObject
        {
            ["type"]         = "ProtectUnit",
            ["protect_text"] = "Keep the convoy alive.",
        };
        // Must pair with a non-ProtectUnit objective to pass the degenerate-rule.
        var kill = BuildKillObj("Marauders", 1);
        mission["steps"] = new JArray(new JObject
        {
            ["objectives"] = new JArray(protect, kill),
        });
        var block = Validator().Parse(mission, Hostiles, NeutralAndHostileRep);
        Assert.IsType<LlmProtectUnit>(block.Steps[0].Objectives[0]);
    }

    [Fact]
    public void Parse_ProtectUnit_MissingProtectText_Rejects()
    {
        var mission = BuildGood();
        var protect = new JObject { ["type"] = "ProtectUnit" };
        mission["steps"] = new JArray(new JObject
        {
            ["objectives"] = new JArray(protect, BuildKillObj("Marauders", 1)),
        });
        Assert.Throws<LlmValidationException>(
            () => Validator().Parse(mission, Hostiles, NeutralAndHostileRep));
    }

    [Fact]
    public void Parse_ProtectUnit_ProtectTextTooLong_Rejects()
    {
        var mission = BuildGood();
        var protect = new JObject
        {
            ["type"]         = "ProtectUnit",
            ["protect_text"] = new string('x', 121),
        };
        mission["steps"] = new JArray(new JObject
        {
            ["objectives"] = new JArray(protect, BuildKillObj("Marauders", 1)),
        });
        Assert.Throws<LlmValidationException>(
            () => Validator().Parse(mission, Hostiles, NeutralAndHostileRep));
    }

    // ---------- TriggerObjective per-type rules ----------

    [Fact]
    public void Parse_TriggerObjective_HappyPath()
    {
        var mission = BuildGood();
        var trig = new JObject
        {
            ["type"]            = "TriggerObjective",
            ["trigger"]         = "DockedWithSpaceStation",
            ["required_amount"] = 2,
            ["description"]     = "Dock twice.",
        };
        mission["steps"] = new JArray(new JObject
        {
            ["objectives"] = new JArray(trig),
        });
        var block = Validator().Parse(mission, Hostiles, NeutralAndHostileRep);
        var obj = Assert.IsType<LlmTriggerObjective>(block.Steps[0].Objectives[0]);
        Assert.Equal("DockedWithSpaceStation", obj.Trigger);
    }

    [Fact]
    public void Parse_TriggerObjective_TriggerNotWhitelisted_Rejects()
    {
        var mission = BuildGood();
        var trig = new JObject
        {
            ["type"]            = "TriggerObjective",
            ["trigger"]         = "BountyTargetKilled",  // real enum value; not whitelisted
            ["required_amount"] = 1,
            ["description"]     = "Kill.",
        };
        mission["steps"] = new JArray(new JObject
        {
            ["objectives"] = new JArray(trig),
        });
        var ex = Assert.Throws<LlmValidationException>(
            () => Validator().Parse(mission, Hostiles, NeutralAndHostileRep));
        Assert.Contains("trigger", ex.Message);
    }

    [Fact]
    public void Parse_TriggerObjective_RequiredAmountTooBig_Rejects()
    {
        var mission = BuildGood();
        var trig = new JObject
        {
            ["type"]            = "TriggerObjective",
            ["trigger"]         = "DockedWithSpaceStation",
            ["required_amount"] = 4,
            ["description"]     = "Dock.",
        };
        mission["steps"] = new JArray(new JObject
        {
            ["objectives"] = new JArray(trig),
        });
        Assert.Throws<LlmValidationException>(
            () => Validator().Parse(mission, Hostiles, NeutralAndHostileRep));
    }

    // ---------- CollectItemTypes per-type rules ----------

    [Fact]
    public void Parse_CollectItemTypes_HappyPath()
    {
        var mission = BuildGood();
        var collect = new JObject
        {
            ["type"]            = "CollectItemTypes",
            ["item_category"]   = "Ore",
            ["required_amount"] = 5,
            ["description"]     = "Collect ore samples.",
        };
        mission["steps"] = new JArray(new JObject
        {
            ["objectives"] = new JArray(collect),
        });
        var block = Validator().Parse(mission, Hostiles, NeutralAndHostileRep);
        var obj = Assert.IsType<LlmCollectItemTypes>(block.Steps[0].Objectives[0]);
        Assert.Equal("Ore", obj.ItemCategory);
        Assert.Equal(5, obj.RequiredAmount);
    }

    [Fact]
    public void Parse_CollectItemTypes_CategoryNotWhitelisted_Rejects()
    {
        var mission = BuildGood();
        var collect = new JObject
        {
            ["type"]            = "CollectItemTypes",
            ["item_category"]   = "Crystal",
            ["required_amount"] = 1,
            ["description"]     = "Collect.",
        };
        mission["steps"] = new JArray(new JObject
        {
            ["objectives"] = new JArray(collect),
        });
        Assert.Throws<LlmValidationException>(
            () => Validator().Parse(mission, Hostiles, NeutralAndHostileRep));
    }

    [Fact]
    public void Parse_CollectItemTypes_RequiredAmountTooBig_Rejects()
    {
        var mission = BuildGood();
        var collect = new JObject
        {
            ["type"]            = "CollectItemTypes",
            ["item_category"]   = "Ore",
            ["required_amount"] = 51,
            ["description"]     = "Collect.",
        };
        mission["steps"] = new JArray(new JObject
        {
            ["objectives"] = new JArray(collect),
        });
        Assert.Throws<LlmValidationException>(
            () => Validator().Parse(mission, Hostiles, NeutralAndHostileRep));
    }

    // ---------- CollectItemTypes guards_faction ----------

    [Fact]
    public void Parse_CollectItemTypes_GuardsFaction_Salvage_HappyPath()
    {
        var mission = BuildGood();
        var collect = new JObject
        {
            ["type"]            = "CollectItemTypes",
            ["item_category"]   = "Salvage",
            ["required_amount"] = 10,
            ["description"]     = "Recover salvage from the defended wreck.",
            ["guards_faction"]  = "Marauders",
        };
        mission["steps"] = new JArray(new JObject
        {
            ["objectives"] = new JArray(collect),
        });
        var block = Validator().Parse(mission, Hostiles, NeutralAndHostileRep);
        var obj = Assert.IsType<LlmCollectItemTypes>(block.Steps[0].Objectives[0]);
        Assert.Equal("Salvage",   obj.ItemCategory);
        Assert.Equal("Marauders", obj.GuardsFaction);
    }

    [Fact]
    public void Parse_CollectItemTypes_GuardsFaction_Ore_HappyPath()
    {
        var mission = BuildGood();
        var collect = new JObject
        {
            ["type"]            = "CollectItemTypes",
            ["item_category"]   = "Ore",
            ["required_amount"] = 5,
            ["description"]     = "Mine the contested field.",
            ["guards_faction"]  = "Darkspacers",
        };
        mission["steps"] = new JArray(new JObject
        {
            ["objectives"] = new JArray(collect),
        });
        var block = Validator().Parse(mission, Hostiles, NeutralAndHostileRep);
        var obj = Assert.IsType<LlmCollectItemTypes>(block.Steps[0].Objectives[0]);
        Assert.Equal("Darkspacers", obj.GuardsFaction);
    }

    [Fact]
    public void Parse_CollectItemTypes_NoGuardsFaction_LeavesFieldNull()
    {
        var mission = BuildGood();
        var collect = new JObject
        {
            ["type"]            = "CollectItemTypes",
            ["item_category"]   = "Salvage",
            ["required_amount"] = 3,
            ["description"]     = "Undefended site.",
        };
        mission["steps"] = new JArray(new JObject
        {
            ["objectives"] = new JArray(collect),
        });
        var block = Validator().Parse(mission, Hostiles, NeutralAndHostileRep);
        var obj = Assert.IsType<LlmCollectItemTypes>(block.Steps[0].Objectives[0]);
        Assert.Null(obj.GuardsFaction);
    }

    [Fact]
    public void Parse_CollectItemTypes_GuardsFaction_OnRefinedProduct_Rejects()
    {
        // RefinedProduct has no POI to attach guards to — acquired via
        // refinery/trader, not by flying to a spawn. Must be rejected.
        var mission = BuildGood();
        var collect = new JObject
        {
            ["type"]            = "CollectItemTypes",
            ["item_category"]   = "RefinedProduct",
            ["required_amount"] = 3,
            ["description"]     = "Bring refined goods.",
            ["guards_faction"]  = "Marauders",
        };
        mission["steps"] = new JArray(new JObject
        {
            ["objectives"] = new JArray(collect),
        });
        Assert.Throws<LlmValidationException>(
            () => Validator().Parse(mission, Hostiles, NeutralAndHostileRep));
    }

    [Fact]
    public void Parse_CollectItemTypes_GuardsFaction_OnTradeGoods_Rejects()
    {
        var mission = BuildGood();
        var collect = new JObject
        {
            ["type"]            = "CollectItemTypes",
            ["item_category"]   = "TradeGoods",
            ["required_amount"] = 3,
            ["description"]     = "Haul cargo.",
            ["guards_faction"]  = "Marauders",
        };
        mission["steps"] = new JArray(new JObject
        {
            ["objectives"] = new JArray(collect),
        });
        Assert.Throws<LlmValidationException>(
            () => Validator().Parse(mission, Hostiles, NeutralAndHostileRep));
    }

    [Fact]
    public void Parse_CollectItemTypes_GuardsFaction_NotWhitelisted_Rejects()
    {
        var mission = BuildGood();
        var collect = new JObject
        {
            ["type"]            = "CollectItemTypes",
            ["item_category"]   = "Salvage",
            ["required_amount"] = 3,
            ["description"]     = "Site.",
            ["guards_faction"]  = "NotARealFaction",
        };
        mission["steps"] = new JArray(new JObject
        {
            ["objectives"] = new JArray(collect),
        });
        Assert.Throws<LlmValidationException>(
            () => Validator().Parse(mission, Hostiles, NeutralAndHostileRep));
    }

    [Fact]
    public void Parse_CollectItemTypes_GuardsFaction_FriendlyFaction_Rejects()
    {
        // Can't have TradingGuild defenders attacking the player — that's
        // either a canon break or an accidental war. Same hostility rule
        // as KillEnemies / ClearPoi.
        var mission = BuildGood();
        var collect = new JObject
        {
            ["type"]            = "CollectItemTypes",
            ["item_category"]   = "Salvage",
            ["required_amount"] = 3,
            ["description"]     = "Site.",
            ["guards_faction"]  = "TradingGuild",
        };
        mission["steps"] = new JArray(new JObject
        {
            ["objectives"] = new JArray(collect),
        });
        Assert.Throws<LlmValidationException>(
            () => Validator().Parse(mission, Hostiles, NeutralAndHostileRep));
    }

    // ---------- Step-level: at most one POI-spawning objective ----------

    [Fact]
    public void Parse_Step_ClearPoiPlusCollectSalvage_Rejects()
    {
        // This is the exact shape that orphaned a POI in live play —
        // ClearPoi + CollectItemTypes(Salvage) in one step competed for
        // the step's single dynamicPointOfInterest slot.
        var mission = BuildGood();
        var clear = new JObject
        {
            ["type"]          = "ClearPoi",
            ["enemy_faction"] = "Marauders",
            ["description"]   = "Clear the combat zone.",
        };
        var collect = new JObject
        {
            ["type"]            = "CollectItemTypes",
            ["item_category"]   = "Salvage",
            ["required_amount"] = 5,
            ["description"]     = "Recover salvage.",
        };
        mission["steps"] = new JArray(new JObject
        {
            ["objectives"] = new JArray(clear, collect),
        });
        Assert.Throws<LlmValidationException>(
            () => Validator().Parse(mission, Hostiles, NeutralAndHostileRep));
    }

    [Fact]
    public void Parse_Step_ClearPoiPlusCollectOre_Rejects()
    {
        var mission = BuildGood();
        var clear = new JObject
        {
            ["type"]          = "ClearPoi",
            ["enemy_faction"] = "Marauders",
            ["description"]   = "Clear the asteroid approach.",
        };
        var collect = new JObject
        {
            ["type"]            = "CollectItemTypes",
            ["item_category"]   = "Ore",
            ["required_amount"] = 5,
            ["description"]     = "Mine the field.",
        };
        mission["steps"] = new JArray(new JObject
        {
            ["objectives"] = new JArray(clear, collect),
        });
        Assert.Throws<LlmValidationException>(
            () => Validator().Parse(mission, Hostiles, NeutralAndHostileRep));
    }

    [Fact]
    public void Parse_Step_ClearPoiPlusCollectTradeGoods_Allowed()
    {
        // TradeGoods doesn't spawn a POI, so the step still only has ONE
        // POI-spawning objective (ClearPoi). Must be accepted.
        var mission = BuildGood();
        var clear = new JObject
        {
            ["type"]          = "ClearPoi",
            ["enemy_faction"] = "Marauders",
            ["description"]   = "Clear the zone.",
        };
        var collect = new JObject
        {
            ["type"]            = "CollectItemTypes",
            ["item_category"]   = "TradeGoods",
            ["required_amount"] = 3,
            ["description"]     = "Haul afterwards.",
        };
        mission["steps"] = new JArray(new JObject
        {
            ["objectives"] = new JArray(clear, collect),
        });
        var block = Validator().Parse(mission, Hostiles, NeutralAndHostileRep);
        Assert.Equal(2, block.Steps[0].Objectives.Count);
    }

    [Fact]
    public void Parse_Steps_ClearPoiThenCollectSalvage_InSeparateSteps_Allowed()
    {
        // The "multi-location" shape: fight here, loot there. Each step
        // carries one POI. This is how vanilla multi-step missions work.
        var mission = BuildGood();
        var clear = new JObject
        {
            ["type"]          = "ClearPoi",
            ["enemy_faction"] = "Marauders",
            ["description"]   = "Clear the escort.",
        };
        var collect = new JObject
        {
            ["type"]            = "CollectItemTypes",
            ["item_category"]   = "Salvage",
            ["required_amount"] = 5,
            ["description"]     = "Then salvage the separate wreck.",
        };
        mission["steps"] = new JArray(
            new JObject { ["objectives"] = new JArray(clear) },
            new JObject { ["objectives"] = new JArray(collect) });
        var block = Validator().Parse(mission, Hostiles, NeutralAndHostileRep);
        Assert.Equal(2, block.Steps.Count);
    }

    // ---------- ClearPoi ----------

    [Fact]
    public void Parse_ClearPoi_HappyPath()
    {
        var mission = BuildGood();
        var clear = new JObject
        {
            ["type"]          = "ClearPoi",
            ["enemy_faction"] = "Marauders",
            ["description"]   = "Clear the field.",
        };
        mission["steps"] = new JArray(new JObject
        {
            ["objectives"] = new JArray(clear),
        });
        var block = Validator().Parse(mission, Hostiles, NeutralAndHostileRep);
        var obj = Assert.IsType<LlmClearPoi>(block.Steps[0].Objectives[0]);
        Assert.Equal("Marauders", obj.EnemyFaction);
        Assert.Equal("Clear the field.", obj.Description);
    }

    [Fact]
    public void Parse_ClearPoi_FriendlyFaction_Rejects()
    {
        var mission = BuildGood();
        var clear = new JObject
        {
            ["type"]          = "ClearPoi",
            ["enemy_faction"] = "TradingGuild",  // rep 100, friendly
            ["description"]   = "Bad target.",
        };
        mission["steps"] = new JArray(new JObject
        {
            ["objectives"] = new JArray(clear),
        });
        var ex = Assert.Throws<LlmValidationException>(
            () => Validator().Parse(mission, Hostiles, NeutralAndHostileRep));
        Assert.Contains("not hostile", ex.Message);
    }

    [Fact]
    public void Parse_ClearPoi_UnknownFaction_Rejects()
    {
        var mission = BuildGood();
        var clear = new JObject
        {
            ["type"]          = "ClearPoi",
            ["enemy_faction"] = "NotAFaction",
            ["description"]   = "Clear.",
        };
        mission["steps"] = new JArray(new JObject
        {
            ["objectives"] = new JArray(clear),
        });
        Assert.Throws<LlmValidationException>(
            () => Validator().Parse(mission, Hostiles, NeutralAndHostileRep));
    }

    [Fact]
    public void Parse_ClearPoi_NoRequiredAmountField()
    {
        // ClearPoi must NOT carry a required_amount — the spawn's totalUnitCount
        // supplies it. An extra key is a strict-schema violation.
        var mission = BuildGood();
        var clear = new JObject
        {
            ["type"]            = "ClearPoi",
            ["enemy_faction"]   = "Marauders",
            ["required_amount"] = 3,
            ["description"]     = "Clear.",
        };
        mission["steps"] = new JArray(new JObject
        {
            ["objectives"] = new JArray(clear),
        });
        Assert.Throws<LlmValidationException>(
            () => Validator().Parse(mission, Hostiles, NeutralAndHostileRep));
    }

    // ---------- Per-step combat-objective cap ----------

    [Fact]
    public void Parse_Step_WithBothClearPoiAndKillEnemies_Rejects()
    {
        // Guardrail: ClearPoi + KillEnemies in the same step is the redundant
        // double-tracking pattern observed in live testing. Reject.
        var mission = BuildGood();
        mission["steps"] = new JArray(new JObject
        {
            ["objectives"] = new JArray(
                new JObject
                {
                    ["type"]          = "ClearPoi",
                    ["enemy_faction"] = "Marauders",
                    ["description"]   = "Clear the zone.",
                },
                new JObject
                {
                    ["type"]            = "KillEnemies",
                    ["enemy_faction"]   = "Marauders",
                    ["required_amount"] = 3,
                    ["description"]     = "Hunt stragglers.",
                }),
        });
        var ex = Assert.Throws<LlmValidationException>(
            () => Validator().Parse(mission, Hostiles, NeutralAndHostileRep));
        Assert.Contains("at most one", ex.Message);
    }

    [Fact]
    public void Parse_Step_ClearPoiPlusNonCombat_Accepted()
    {
        // ClearPoi paired with a non-combat objective is fine — e.g. clear
        // the zone then dock with the station.
        var mission = BuildGood();
        mission["steps"] = new JArray(new JObject
        {
            ["objectives"] = new JArray(
                new JObject
                {
                    ["type"]          = "ClearPoi",
                    ["enemy_faction"] = "Marauders",
                    ["description"]   = "Clear the zone.",
                },
                new JObject
                {
                    ["type"]            = "TriggerObjective",
                    ["trigger"]         = "DockedWithSpaceStation",
                    ["required_amount"] = 1,
                    ["description"]     = "Return and dock.",
                }),
        });
        var block = Validator().Parse(mission, Hostiles, NeutralAndHostileRep);
        Assert.Equal(2, block.Steps[0].Objectives.Count);
    }

    // ---------- Rewards per-type rules ----------

    [Fact]
    public void Parse_CreditsReward_HappyPath()
    {
        var mission = BuildGood();
        mission["rewards"] = new JArray(new JObject
        {
            ["type"]       = "Credits",
            ["base_value"] = 100,
        });
        var block = Validator().Parse(mission, Hostiles, NeutralAndHostileRep);
        var credits = Assert.IsType<LlmCreditsReward>(block.Rewards[0]);
        Assert.Equal(100, credits.BaseValue);
    }

    [Fact]
    public void Parse_CreditsReward_BaseValueTooLow_Rejects()
    {
        var mission = BuildGood();
        mission["rewards"] = new JArray(new JObject
        {
            ["type"]       = "Credits",
            ["base_value"] = 5,
        });
        Assert.Throws<LlmValidationException>(
            () => Validator().Parse(mission, Hostiles, NeutralAndHostileRep));
    }

    [Fact]
    public void Parse_CreditsReward_BaseValueTooHigh_Rejects()
    {
        var mission = BuildGood();
        mission["rewards"] = new JArray(new JObject
        {
            ["type"]       = "Credits",
            ["base_value"] = 201,
        });
        Assert.Throws<LlmValidationException>(
            () => Validator().Parse(mission, Hostiles, NeutralAndHostileRep));
    }

    [Fact]
    public void Parse_ExperienceReward_BaseValueTooHigh_Rejects()
    {
        var mission = BuildGood();
        mission["rewards"] = new JArray(new JObject
        {
            ["type"]       = "Experience",
            ["base_value"] = 151,
        });
        Assert.Throws<LlmValidationException>(
            () => Validator().Parse(mission, Hostiles, NeutralAndHostileRep));
    }

    [Fact]
    public void Parse_ReputationReward_HappyPath_Negative()
    {
        var mission = BuildGood();
        mission["rewards"] = new JArray(new JObject
        {
            ["type"]    = "Reputation",
            ["faction"] = "Marauders",
            ["amount"]  = -200,
        });
        var block = Validator().Parse(mission, Hostiles, NeutralAndHostileRep);
        var rep = Assert.IsType<LlmReputationReward>(block.Rewards[0]);
        Assert.Equal(-200, rep.Amount);
    }

    [Fact]
    public void Parse_ReputationReward_AmountOutOfRange_Rejects()
    {
        var mission = BuildGood();
        mission["rewards"] = new JArray(new JObject
        {
            ["type"]    = "Reputation",
            ["faction"] = "TradingGuild",
            ["amount"]  = 501,
        });
        Assert.Throws<LlmValidationException>(
            () => Validator().Parse(mission, Hostiles, NeutralAndHostileRep));
    }

    [Fact]
    public void Parse_ReputationReward_FactionUnknown_Rejects()
    {
        var mission = BuildGood();
        mission["rewards"] = new JArray(new JObject
        {
            ["type"]    = "Reputation",
            ["faction"] = "NotAFaction",
            ["amount"]  = 50,
        });
        Assert.Throws<LlmValidationException>(
            () => Validator().Parse(mission, Hostiles, NeutralAndHostileRep));
    }

    [Fact]
    public void Parse_RewardsEmpty_Rejects()
    {
        var mission = BuildGood();
        mission["rewards"] = new JArray();
        Assert.Throws<LlmValidationException>(
            () => Validator().Parse(mission, Hostiles, NeutralAndHostileRep));
    }

    [Fact]
    public void Parse_RewardsTooMany_Rejects()
    {
        var mission = BuildGood();
        var six = new JArray();
        for (var i = 0; i < 6; i++)
            six.Add(new JObject { ["type"] = "Credits", ["base_value"] = 50 });
        mission["rewards"] = six;
        Assert.Throws<LlmValidationException>(
            () => Validator().Parse(mission, Hostiles, NeutralAndHostileRep));
    }

    [Fact]
    public void Parse_RewardUnknownType_Rejects()
    {
        var mission = BuildGood();
        mission["rewards"] = new JArray(new JObject
        {
            ["type"]   = "Item",
            ["amount"] = 1,
        });
        Assert.Throws<LlmValidationException>(
            () => Validator().Parse(mission, Hostiles, NeutralAndHostileRep));
    }

    // ---------- Global coherence: degenerate mission ----------

    [Fact]
    public void Parse_OnlyProtectUnitAcrossAllSteps_Rejects()
    {
        var mission = BuildGood();
        var protect1 = new JObject { ["type"] = "ProtectUnit", ["protect_text"] = "a" };
        var protect2 = new JObject { ["type"] = "ProtectUnit", ["protect_text"] = "b" };
        mission["steps"] = new JArray(
            new JObject { ["objectives"] = new JArray(protect1) },
            new JObject { ["objectives"] = new JArray(protect2) });
        var ex = Assert.Throws<LlmValidationException>(
            () => Validator().Parse(mission, Hostiles, NeutralAndHostileRep));
        Assert.Contains("degenerate", ex.Message, System.StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Parse_MixedProtectAndKill_Accepted()
    {
        var mission = BuildGood();
        var protect = new JObject { ["type"] = "ProtectUnit", ["protect_text"] = "guard" };
        var kill    = BuildKillObj("Marauders", 2);
        mission["steps"] = new JArray(
            new JObject { ["objectives"] = new JArray(protect) },
            new JObject { ["objectives"] = new JArray(kill) });
        var block = Validator().Parse(mission, Hostiles, NeutralAndHostileRep);
        Assert.Equal(2, block.Steps.Count);
    }

    // ---------- Helpers ----------

    /// <summary>Builds a minimal valid mission block (one KillEnemies step,
    /// one Credits reward) that every "modify-and-reject" test starts from.</summary>
    private static JObject BuildGood()
    {
        return new JObject
        {
            ["name"]            = "Test Mission",
            ["description"]     = "A test.",
            ["completion_text"] = "Nice work.",
            ["source_faction"]  = "TradingGuild",
            ["steps"]           = new JArray(BuildStep()),
            ["rewards"]         = new JArray(new JObject
            {
                ["type"]       = "Credits",
                ["base_value"] = 50,
            }),
        };
    }

    private static JObject BuildStep()
    {
        return new JObject
        {
            ["objectives"] = new JArray(BuildKillObj("Marauders", 1)),
        };
    }

    private static JObject BuildKillObj(string faction, int amount) => new()
    {
        ["type"]            = "KillEnemies",
        ["enemy_faction"]   = faction,
        ["required_amount"] = amount,
        ["description"]     = "Take them out.",
    };

    private static JObject BuildTriggerObj() => new()
    {
        ["type"]            = "TriggerObjective",
        ["trigger"]         = "DockedWithSpaceStation",
        ["required_amount"] = 1,
        ["description"]     = "Dock.",
    };

    private static JObject BuildWithoutField(string field)
    {
        var m = BuildGood();
        m.Remove(field);
        return m;
    }
}
