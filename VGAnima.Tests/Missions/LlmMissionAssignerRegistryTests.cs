using System;
using VGAnima.Llm;
using VGAnima.Missions;
using VGAnima.Persistence;
using Xunit;

namespace VGAnima.Tests.Missions;

public class LlmMissionAssignerRegistryTests
{
    // LlmMissionAssigner.Assign calls MissionFactoryFromJson.Build, which
    // calls Faction.Get(...) on its first line. `Source.Galaxy.Faction..cctor()`
    // NREs in the xUnit appdomain (its static init touches Unity-dependent
    // subclass ctors), so every path that hits Build blows up before any
    // assertion runs. Tests stay in the file for documentation / future-proofing
    // but must be Skipped under the current stub. Matches the pattern used by
    // LlmMissionAssignerTests.
    private const string FactionCctorSkip =
        "Faction..cctor NREs without Unity runtime — deferred to E2E";

    [Fact(Skip = FactionCctorSkip)]
    public void Assign_PushesEntryToRegistryInOfferedState()
    {
        var reg = new PersistedBrokerRegistry();
        var clock = new FakeClock(
            gameSeconds: 18420.5,
            utcNow: new DateTime(2026, 04, 21, 10, 15, 30, DateTimeKind.Utc));
        var assigner = new LlmMissionAssigner(
            register: _ => { },
            registry: reg,
            clock: clock);

        var story = Story();
        var storyId = assigner.Assign(
            block: Block(),
            missionLevel: 1,
            brokerStation: null,
            brokerSeed: "vganima-broker-abc-0",
            brokerStory: story);

        var entry = reg.Get(storyId);
        Assert.NotNull(entry);
        Assert.Equal(PersistedEntryStates.Offered, entry!.State);
        Assert.Equal("vganima-broker-abc-0", entry.Broker.Seed);
        Assert.Same(story, entry.Broker.Story);
        Assert.Equal(18420.5, entry.Timestamps.CreatedGameSeconds);
        Assert.Equal("2026-04-21T10:15:30Z", entry.Timestamps.CreatedRealUtc);
        Assert.Equal(18420.5, entry.Timestamps.LastSeenGameSeconds);
    }

    [Fact(Skip = FactionCctorSkip)]
    public void Assign_WithoutRegistry_StillReturnsStoryId()
    {
        // Back-compat: the simpler ctor (no registry) still works for
        // tests and pre-registry integrations.
        var assigner = new LlmMissionAssigner(_ => { });
        var storyId = assigner.Assign(Block(), 1, null, "seed", Story());
        Assert.StartsWith("vganima_llm_", storyId);
    }

    private sealed class FakeClock : IClock
    {
        public FakeClock(double gameSeconds, DateTime utcNow)
        {
            GameSeconds = gameSeconds;
            UtcNow = utcNow;
        }
        public double GameSeconds { get; }
        public DateTime UtcNow { get; }
    }

    // Mirrors the private `Block()` helper in
    // VGAnima.Tests/Missions/LlmMissionAssignerTests.cs.
    private static LlmMissionBlock Block() => new(
        Name: "Test", Description: "d", CompletionText: "c", SourceFaction: "TradingGuild",
        Steps: new[]
        {
            new LlmMissionStep(new LlmObjective[]
            {
                new LlmTriggerObjective("DockedWithSpaceStation", 1, "Dock."),
            }),
        },
        Rewards: new LlmReward[] { new LlmCreditsReward(50) });

    // Mirrors the private `Story()` helper in
    // VGAnima.Tests/Pitch/LlmPitchProviderTests.cs.
    private static LlmStory Story() => new(
        Pitch:   new[] { "pitch-1", "pitch-2", "pitch-3" },
        CheckIn: new[] { "checkin-1" },
        Payout:  new[] { "payout-1", "payout-2", "payout-3" });
}
