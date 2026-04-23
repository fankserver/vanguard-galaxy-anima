using System.Collections.Generic;
using Source.MissionSystem;
using VGAnima.Llm;
using VGAnima.Missions;
using Xunit;

namespace VGAnima.Tests.Missions;

public class LlmMissionAssignerTests
{
    // LlmMissionAssigner.Assign calls MissionFactoryFromJson.Build, which
    // calls Faction.Get(...) on its first line. `Source.Galaxy.Faction..cctor()`
    // NREs in the xUnit appdomain (its static init touches Unity-dependent
    // subclass ctors), so every path below blows up before any assertion
    // runs. Tests stay in the file for documentation / future-proofing but
    // must be Skipped under the current stub. Matches the pattern used by
    // MissionFactoryFromJsonTests.
    private const string FactionCctorSkip =
        "Faction..cctor NREs without Unity runtime — deferred to E2E";

    private static LlmMissionBlock Block() => new(
        Name: "Test", Description: "d", CompletionText: "c", SourceFaction: "TradingGuild",
        Steps: new[]
        {
            new LlmMissionStep(new GatherOreIntent(10, "Mine.")),
        },
        Rewards: new LlmReward[] { new LlmCreditsReward(50) });

    [Fact(Skip = FactionCctorSkip)]
    public void Assign_ReturnsStoryIdWithPluginPrefix()
    {
        var registrations = new Dictionary<string, StoryMission>();
        var assigner = new LlmMissionAssigner(
            register: sm => registrations[sm.identifier] = sm);

        var storyId = assigner.Assign(
            Block(), missionLevel: 5, brokerStation: null, brokerSeed: "broker-abc");

        Assert.StartsWith("vganima_llm_", storyId);
        Assert.Contains("broker-abc", storyId);
    }

    [Fact(Skip = FactionCctorSkip)]
    public void Assign_RegistersFactoryUnderTheReturnedStoryId()
    {
        var registrations = new Dictionary<string, StoryMission>();
        var assigner = new LlmMissionAssigner(
            register: sm => registrations[sm.identifier] = sm);

        var storyId = assigner.Assign(Block(), 5, null, "broker-abc");

        Assert.True(registrations.ContainsKey(storyId),
            $"Expected {storyId} registered, got [{string.Join(",", registrations.Keys)}]");
    }

    [Fact(Skip = FactionCctorSkip)]
    public void Assign_TwoCallsSameSeed_DifferentStoryIds()
    {
        var registrations = new Dictionary<string, StoryMission>();
        var assigner = new LlmMissionAssigner(
            register: sm => registrations[sm.identifier] = sm);

        var a = assigner.Assign(Block(), 5, null, "broker-abc");
        var b = assigner.Assign(Block(), 5, null, "broker-abc");

        Assert.NotEqual(a, b);
        Assert.Equal(2, registrations.Count);
    }
}
