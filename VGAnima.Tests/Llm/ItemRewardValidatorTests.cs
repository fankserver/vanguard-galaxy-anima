using Newtonsoft.Json.Linq;
using VGAnima.Llm;
using Xunit;

namespace VGAnima.Tests.Llm;

/// <summary>Validator coverage for the v1 item-reward schema extension
/// (<c>{ "type": "Item", "kind": ... }</c>). Sits alongside the broader
/// <c>MissionBlockValidatorTests</c>; factored out to keep the item-
/// reward surface auditable as a unit.</summary>
public class ItemRewardValidatorTests
{
    private static JObject BuildMissionWithReward(JObject reward)
    {
        return new JObject
        {
            ["name"]            = "Mission",
            ["description"]     = "desc",
            ["completion_text"] = "done",
            ["source_faction"]  = "SalvageGuild",
            ["steps"] = new JArray(new JObject
            {
                ["intent"]          = "gather_ore",
                ["required_amount"] = 5,
                ["description"]     = "Mine ore.",
            }),
            ["rewards"] = new JArray(reward),
        };
    }

    [Fact]
    public void ItemReward_MiningClaim_Accepts()
    {
        var reward = new JObject { ["type"] = "Item", ["kind"] = "MiningClaim" };
        var mission = BuildMissionWithReward(reward);
        var block = new MissionBlockValidator().Parse(
            mission,
            atWar: System.Array.Empty<string>(),
            reputation: new System.Collections.Generic.Dictionary<string, int>());
        var r = Assert.IsType<LlmItemReward>(block.Rewards[0]);
        Assert.Equal("MiningClaim", r.Kind);
    }

    [Fact]
    public void ItemReward_SalvageClaim_Accepts()
    {
        var reward = new JObject { ["type"] = "Item", ["kind"] = "SalvageClaim" };
        var block = new MissionBlockValidator().Parse(
            BuildMissionWithReward(reward),
            System.Array.Empty<string>(),
            new System.Collections.Generic.Dictionary<string, int>());
        Assert.Equal("SalvageClaim", ((LlmItemReward)block.Rewards[0]).Kind);
    }

    [Fact]
    public void ItemReward_MaterialMiningClaim_Accepts()
    {
        var reward = new JObject { ["type"] = "Item", ["kind"] = "MaterialMiningClaim" };
        var block = new MissionBlockValidator().Parse(
            BuildMissionWithReward(reward),
            System.Array.Empty<string>(),
            new System.Collections.Generic.Dictionary<string, int>());
        Assert.Equal("MaterialMiningClaim", ((LlmItemReward)block.Rewards[0]).Kind);
    }

    [Fact]
    public void ItemReward_UnknownKind_Rejects()
    {
        // Only the three whitelisted kinds are accepted — anything else
        // (e.g. "Blueprint" that isn't wired up yet) is refused at parse
        // time, not silently tolerated.
        var reward = new JObject { ["type"] = "Item", ["kind"] = "Blueprint" };
        Assert.Throws<LlmValidationException>(() =>
            new MissionBlockValidator().Parse(
                BuildMissionWithReward(reward),
                System.Array.Empty<string>(),
                new System.Collections.Generic.Dictionary<string, int>()));
    }

    [Fact]
    public void ItemReward_MissingKind_Rejects()
    {
        var reward = new JObject { ["type"] = "Item" };
        Assert.Throws<LlmValidationException>(() =>
            new MissionBlockValidator().Parse(
                BuildMissionWithReward(reward),
                System.Array.Empty<string>(),
                new System.Collections.Generic.Dictionary<string, int>()));
    }

    [Fact]
    public void ItemReward_ExtraField_Rejects()
    {
        // Strict-keys enforcement — an unexpected field on an Item reward
        // is rejected, same as any other reward type.
        var reward = new JObject
        {
            ["type"] = "Item",
            ["kind"] = "MiningClaim",
            ["hack"] = "extra",
        };
        Assert.Throws<LlmValidationException>(() =>
            new MissionBlockValidator().Parse(
                BuildMissionWithReward(reward),
                System.Array.Empty<string>(),
                new System.Collections.Generic.Dictionary<string, int>()));
    }
}
