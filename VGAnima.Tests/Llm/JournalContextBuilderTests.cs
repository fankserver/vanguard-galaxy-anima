using System.Collections.Generic;
using System.Linq;
using VGAnima.Llm;
using VGAnima.Persistence;
using Xunit;

namespace VGAnima.Tests.Llm;

public class JournalContextBuilderTests
{
    private static CompletedMissionRecord Rec(
        string storyId, string stationId, string faction,
        int magnitude, string missionName = "Test Mission",
        string archetype = MissionArchetypes.Combat,
        string outcome   = CompletedMissionOutcomes.Completed) =>
        new(storyId, "A Broker", stationId, "A Station", faction,
            missionName, archetype, outcome,
            MissionLevel: 10, SystemName: "A System",
            MagnitudeScore: magnitude,
            ResolvedGameSeconds: 1000.0,
            ResolvedRealUtc:     "2026-04-22T00:00:00Z");

    [Fact]
    public void Build_EmptyLog_ReturnsEmptyWindows()
    {
        var section = JournalContextBuilder.Build(
            new List<CompletedMissionRecord>(), "station-1", "SalvageGuild");
        Assert.Empty(section.Local);
        Assert.Empty(section.Factional);
        Assert.Empty(section.Notable);
    }

    [Fact]
    public void Build_LocalWindow_FiltersBySameStationId()
    {
        var log = new[]
        {
            Rec("s1", "station-A", "SalvageGuild", 3),
            Rec("s2", "station-B", "SalvageGuild", 3),
            Rec("s3", "station-A", "MiningGuild",  3),
            Rec("s4", "station-C", "SalvageGuild", 3),
        };
        var section = JournalContextBuilder.Build(log, "station-A", "SalvageGuild");
        Assert.Equal(2, section.Local.Count);
        Assert.All(section.Local, e => Assert.Contains(e.StoryId, new[] { "s1", "s3" }));
    }

    [Fact]
    public void Build_FactionalWindow_ExcludesLocalStation()
    {
        // Same faction at OTHER stations only. Local window already covers
        // same-faction-same-station — factional must not duplicate.
        var log = new[]
        {
            Rec("s1", "station-A", "SalvageGuild", 3),   // local, excluded from factional
            Rec("s2", "station-B", "SalvageGuild", 3),   // factional
            Rec("s3", "station-C", "SalvageGuild", 3),   // factional
            Rec("s4", "station-D", "MiningGuild",  3),   // different faction, excluded
        };
        var section = JournalContextBuilder.Build(log, "station-A", "SalvageGuild");
        Assert.Equal(2, section.Factional.Count);
        Assert.All(section.Factional,
            e => Assert.Contains(e.StoryId, new[] { "s2", "s3" }));
    }

    [Fact]
    public void Build_NotableWindow_FiltersByMagnitude()
    {
        // Notable threshold is 7. Only high-magnitude events qualify.
        var log = new[]
        {
            Rec("s1", "station-X", "MiningGuild", magnitude: 3),   // below threshold
            Rec("s2", "station-Y", "MiningGuild", magnitude: 9),   // qualifies
            Rec("s3", "station-Z", "Marauders",   magnitude: 10),  // qualifies
            Rec("s4", "station-W", "TradingGuild", magnitude: 5),  // below threshold
        };
        var section = JournalContextBuilder.Build(log, "station-A", "SalvageGuild");
        Assert.Equal(2, section.Notable.Count);
        Assert.All(section.Notable, e => Assert.True(e.Magnitude >= 7));
    }

    [Fact]
    public void Build_NotableWindow_ExcludesEntriesAlreadyInLocalOrFactional()
    {
        // A single record appears in at most one window.
        var log = new[]
        {
            Rec("big-local",    "station-A", "SalvageGuild", magnitude: 10),
            Rec("big-factional", "station-B", "SalvageGuild", magnitude: 10),
            Rec("big-elsewhere", "station-C", "MiningGuild",   magnitude: 10),
        };
        var section = JournalContextBuilder.Build(log, "station-A", "SalvageGuild");
        Assert.Single(section.Local);
        Assert.Single(section.Factional);
        Assert.Single(section.Notable);
        Assert.Equal("big-elsewhere", section.Notable[0].StoryId);
    }

    [Fact]
    public void Build_AllWindows_RespectSizeCaps()
    {
        // Overfill with 20 high-magnitude entries all at the same station.
        // Local takes its cap (5) from the most-recent slice; Factional is
        // empty because every entry is local-station (same-station excluded
        // from factional); Notable fills from what's left after Local, so
        // it takes its cap (3) from older same-station-but-not-in-local
        // entries. This documents the priority order: Local > Factional
        // > Notable, with each later window excluding earlier matches.
        var log = Enumerable.Range(0, 20)
            .Select(i => Rec($"s{i}", "station-A", "SalvageGuild", magnitude: 10))
            .ToList();
        var section = JournalContextBuilder.Build(log, "station-A", "SalvageGuild");
        Assert.Equal(JournalContextBuilder.LocalWindowSize,   section.Local.Count);
        Assert.Empty(section.Factional);
        Assert.Equal(JournalContextBuilder.NotableWindowSize, section.Notable.Count);
    }

    // ---------- Active window (in-flight entries) ----------

    private static PersistedEntry InFlight(
        string storyId, string stationId, string faction, double createdGameSeconds = 100.0)
    {
        var block = new LlmMissionBlock(
            Name: $"Mission-{storyId}", Description: "d", CompletionText: "c",
            SourceFaction: faction,
            Steps:   new[] { new LlmMissionStep(
                new ClearCombatSiteIntent("Marauders", "x")) },
            Rewards: new System.Collections.Generic.List<LlmReward>());
        var story  = new LlmStory(
            new[] { "pitch" }, new[] { "check" }, new[] { "pay" }, block);
        var broker = new PersistedBroker(
            Seed: "seed-" + storyId, StationId: stationId, Story: story,
            NameSnapshot: "Broker", StationNameSnapshot: "Station",
            SystemNameSnapshot: "System");
        return new PersistedEntry(
            StoryId: storyId, State: PersistedEntryStates.Accepted,
            MissionBlock: block, Broker: broker,
            Timestamps: new PersistedTimestamps(
                CreatedGameSeconds:  createdGameSeconds,
                CreatedRealUtc:      "2026-04-22T00:00:00Z",
                LastSeenGameSeconds: createdGameSeconds,
                LastSeenRealUtc:     "2026-04-22T00:00:00Z"));
    }

    [Fact]
    public void Build_Active_NullInput_ReturnsEmpty()
    {
        var section = JournalContextBuilder.Build(
            new List<CompletedMissionRecord>(), "station-A", "SalvageGuild",
            inFlight: null);
        Assert.Empty(section.Active);
    }

    [Fact]
    public void Build_Active_FiltersToSameStationOrSameFaction()
    {
        // "hostile-station-different-faction" (e2) is excluded — neither
        // same station nor same faction; the broker wouldn't know about it.
        var inFlight = new[]
        {
            InFlight("e1", "station-A", "SalvageGuild"),   // same station
            InFlight("e2", "station-X", "MiningGuild"),    // excluded
            InFlight("e3", "station-B", "SalvageGuild"),   // same faction, diff station
        };
        var section = JournalContextBuilder.Build(
            new List<CompletedMissionRecord>(), "station-A", "SalvageGuild",
            inFlight: inFlight);
        Assert.Equal(2, section.Active.Count);
        Assert.All(section.Active,
            e => Assert.Contains(e.StoryId, new[] { "e1", "e3" }));
    }

    [Fact]
    public void Build_Active_OrdersByStationThenFaction()
    {
        // Same-station entries come first (higher priority), then
        // same-faction-elsewhere. Within a tier, newest-first.
        var inFlight = new[]
        {
            InFlight("e-far",    "station-B", "SalvageGuild", createdGameSeconds: 50),
            InFlight("e-local1", "station-A", "SalvageGuild", createdGameSeconds: 20),
            InFlight("e-local2", "station-A", "SalvageGuild", createdGameSeconds: 30),
        };
        var section = JournalContextBuilder.Build(
            new List<CompletedMissionRecord>(), "station-A", "SalvageGuild",
            inFlight: inFlight);
        Assert.Equal(3, section.Active.Count);
        Assert.Equal("e-local2", section.Active[0].StoryId); // newest local first
        Assert.Equal("e-local1", section.Active[1].StoryId);
        Assert.Equal("e-far",    section.Active[2].StoryId); // factional last
    }

    [Fact]
    public void Build_Active_MarksEntriesAsInProgress()
    {
        var inFlight = new[] { InFlight("e1", "station-A", "SalvageGuild") };
        var section = JournalContextBuilder.Build(
            new List<CompletedMissionRecord>(), "station-A", "SalvageGuild",
            inFlight: inFlight);
        Assert.Single(section.Active);
        Assert.Equal(CompletedMissionOutcomes.InProgress, section.Active[0].Outcome);
    }

    [Fact]
    public void Build_Active_RespectsSizeCap()
    {
        var inFlight = Enumerable.Range(0, 20)
            .Select(i => InFlight($"e{i}", "station-A", "SalvageGuild",
                                  createdGameSeconds: i))
            .ToList();
        var section = JournalContextBuilder.Build(
            new List<CompletedMissionRecord>(), "station-A", "SalvageGuild",
            inFlight: inFlight);
        Assert.Equal(JournalContextBuilder.ActiveWindowSize, section.Active.Count);
    }

    [Fact]
    public void Build_OrdersRecentFirst()
    {
        // Log is stored oldest-first; builder should reverse it so the
        // most recent resolution is index 0 in each window.
        var log = new[]
        {
            Rec("oldest", "station-A", "SalvageGuild", 3),
            Rec("middle", "station-A", "SalvageGuild", 3),
            Rec("newest", "station-A", "SalvageGuild", 3),
        };
        var section = JournalContextBuilder.Build(log, "station-A", "SalvageGuild");
        Assert.Equal("newest", section.Local[0].StoryId);
        Assert.Equal("middle", section.Local[1].StoryId);
        Assert.Equal("oldest", section.Local[2].StoryId);
    }
}
