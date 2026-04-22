using System.Collections.Generic;
using System.Linq;
using VGAnima.Persistence;
using Xunit;

namespace VGAnima.Tests.Persistence;

/// <summary>Journal-specific behavior of the registry — cap enforcement +
/// load round-trip. The broader registry tests live in a sibling file.</summary>
public class PersistedBrokerRegistryJournalTests
{
    private static CompletedMissionRecord Rec(string storyId, int magnitude = 3) =>
        new(storyId, "Broker", "station-A", "Station A", "SalvageGuild",
            "Mission", MissionArchetypes.Combat,
            CompletedMissionOutcomes.Completed,
            MissionLevel: 10, SystemName: "Sys",
            MagnitudeScore: magnitude,
            ResolvedGameSeconds: 1.0,
            ResolvedRealUtc: "2026-04-22T00:00:00Z");

    [Fact]
    public void RecordCompletion_AppendsToLog()
    {
        var r = new PersistedBrokerRegistry();
        r.RecordCompletion(Rec("s1"));
        r.RecordCompletion(Rec("s2"));
        Assert.Equal(2, r.CompletedMissions.Count);
        Assert.Equal("s1", r.CompletedMissions[0].StoryId);
        Assert.Equal("s2", r.CompletedMissions[1].StoryId);
    }

    [Fact]
    public void RecordCompletion_TrimsToCapFifo()
    {
        var r = new PersistedBrokerRegistry();
        // Cap is 50; push 53 to force 3 drops from the oldest end.
        for (var i = 0; i < PersistedBrokerRegistry.MaxCompletedMissions + 3; i++)
            r.RecordCompletion(Rec($"s{i}"));
        Assert.Equal(PersistedBrokerRegistry.MaxCompletedMissions, r.CompletedMissions.Count);
        // First surviving entry should be s3 (s0, s1, s2 dropped).
        Assert.Equal("s3", r.CompletedMissions[0].StoryId);
        // Last should be the most recent push.
        Assert.Equal($"s{PersistedBrokerRegistry.MaxCompletedMissions + 2}",
                     r.CompletedMissions[^1].StoryId);
    }

    [Fact]
    public void LoadCompletedMissions_ReplacesExistingLog()
    {
        var r = new PersistedBrokerRegistry();
        r.RecordCompletion(Rec("old"));
        r.LoadCompletedMissions(new[] { Rec("new1"), Rec("new2") });
        Assert.Equal(2, r.CompletedMissions.Count);
        Assert.Equal("new1", r.CompletedMissions[0].StoryId);
        Assert.Equal("new2", r.CompletedMissions[1].StoryId);
    }

    [Fact]
    public void LoadCompletedMissions_Null_ClearsLog()
    {
        // Pre-journal sidecars omit the field entirely; deserializer
        // supplies null. Load must treat it as "empty log" without NREs.
        var r = new PersistedBrokerRegistry();
        r.RecordCompletion(Rec("old"));
        r.LoadCompletedMissions(null);
        Assert.Empty(r.CompletedMissions);
    }

    [Fact]
    public void LoadCompletedMissions_InputOverCap_TrimsOldestFirst()
    {
        // A hand-edited sidecar could hold more than the cap. Load must
        // honor the cap by keeping the MOST RECENT entries.
        var records = Enumerable.Range(0, PersistedBrokerRegistry.MaxCompletedMissions + 10)
            .Select(i => Rec($"s{i}"))
            .ToArray();
        var r = new PersistedBrokerRegistry();
        r.LoadCompletedMissions(records);
        Assert.Equal(PersistedBrokerRegistry.MaxCompletedMissions, r.CompletedMissions.Count);
        Assert.Equal("s10", r.CompletedMissions[0].StoryId);  // s0..s9 dropped
    }

    [Fact]
    public void Clear_ResetsCompletedLog()
    {
        var r = new PersistedBrokerRegistry();
        r.RecordCompletion(Rec("s1"));
        r.Clear();
        Assert.Empty(r.CompletedMissions);
    }
}
