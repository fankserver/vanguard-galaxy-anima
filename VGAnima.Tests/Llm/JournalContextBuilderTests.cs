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
