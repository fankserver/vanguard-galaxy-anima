using System.Collections.Generic;
using VGAnima.Llm;
using VGAnima.Persistence;
using Xunit;

namespace VGAnima.Tests.Llm;

public class RegionallyKnownBuilderTests
{
    private const double OneDay  = 86400.0;
    private const double TwoDays = OneDay * 2;

    [Fact]
    public void Build_EmptyVisited_ReturnsNull()
    {
        var result = RegionallyKnownBuilder.Build(
            new Dictionary<string, VisitedSystem>(),
            new List<CompletedMissionRecord>(),
            currentGameSeconds: TwoDays);

        Assert.Null(result);
    }

    [Fact]
    public void Build_AllBelowThreshold_ReturnsNull()
    {
        // Two visits is "passing through"; must not qualify.
        var visited = new Dictionary<string, VisitedSystem>
        {
            ["sys-a"] = new("sys-a", "Alpha", VisitCount: 2,
                FirstVisitGameSeconds: 0, LastVisitGameSeconds: 0),
        };

        var result = RegionallyKnownBuilder.Build(
            visited, new List<CompletedMissionRecord>(),
            currentGameSeconds: TwoDays);

        Assert.Null(result);
    }

    [Fact]
    public void Build_MeetsThreshold_ReturnsEntryWithFaceOnlyRecognition()
    {
        // Three visits is the minimum to register. No mission log →
        // recent_activity stays null (face-only recognition).
        var visited = new Dictionary<string, VisitedSystem>
        {
            ["sys-a"] = new("sys-a", "Alpha", VisitCount: 3,
                FirstVisitGameSeconds: 0,
                LastVisitGameSeconds:  OneDay * 2),
        };

        var result = RegionallyKnownBuilder.Build(
            visited, new List<CompletedMissionRecord>(),
            currentGameSeconds: OneDay * 5);

        Assert.NotNull(result);
        Assert.Single(result!);
        Assert.Equal("Alpha", result![0].System);
        Assert.Equal(3, result[0].Visits);
        Assert.Equal(3, result[0].LastVisitDaysAgo);   // day 5 - day 2 = 3
        Assert.Null(result[0].RecentActivity);
    }

    [Fact]
    public void Build_RecentMissionInSystem_PopulatesRecentActivity()
    {
        // Mission resolved 5 days ago — well within staleness window;
        // recent_activity reflects its archetype.
        var visited = new Dictionary<string, VisitedSystem>
        {
            ["sys-a"] = new("sys-a", "Alpha", VisitCount: 5,
                FirstVisitGameSeconds: 0,
                LastVisitGameSeconds:  OneDay * 10),
        };
        var log = new List<CompletedMissionRecord>
        {
            MakeRecord("mission-1", "Alpha", archetype: "salvage",
                resolvedGameSeconds: OneDay * 5),
        };

        var result = RegionallyKnownBuilder.Build(
            visited, log, currentGameSeconds: OneDay * 10);

        Assert.NotNull(result);
        Assert.Equal("salvage", result![0].RecentActivity);
    }

    [Fact]
    public void Build_StaleMission_LeavesRecentActivityNull()
    {
        // Mission resolved 40 days ago — beyond the 30-day staleness
        // window; must NOT surface as current activity. The broker
        // should recognize the player but NOT assume ongoing salvage
        // behavior from months-old evidence.
        var visited = new Dictionary<string, VisitedSystem>
        {
            ["sys-a"] = new("sys-a", "Alpha", VisitCount: 5,
                FirstVisitGameSeconds: 0,
                LastVisitGameSeconds:  OneDay * 50),
        };
        var log = new List<CompletedMissionRecord>
        {
            MakeRecord("mission-old", "Alpha", archetype: "salvage",
                resolvedGameSeconds: OneDay * 10),
        };

        var result = RegionallyKnownBuilder.Build(
            visited, log, currentGameSeconds: OneDay * 50);

        Assert.NotNull(result);
        Assert.Null(result![0].RecentActivity);
    }

    [Fact]
    public void Build_MultipleMissionsInSystem_PicksMostRecentArchetype()
    {
        // Two missions in the same system, both recent. The more-recent
        // one wins so the broker's framing reflects current behavior.
        var visited = new Dictionary<string, VisitedSystem>
        {
            ["sys-a"] = new("sys-a", "Alpha", VisitCount: 5,
                FirstVisitGameSeconds: 0,
                LastVisitGameSeconds:  OneDay * 10),
        };
        var log = new List<CompletedMissionRecord>
        {
            MakeRecord("mission-old", "Alpha", "salvage", OneDay * 3),
            MakeRecord("mission-new", "Alpha", "combat",  OneDay * 8),
        };

        var result = RegionallyKnownBuilder.Build(
            visited, log, currentGameSeconds: OneDay * 10);

        Assert.Equal("combat", result![0].RecentActivity);
    }

    [Fact]
    public void Build_SortsByVisitCountDesc()
    {
        var visited = new Dictionary<string, VisitedSystem>
        {
            ["sys-a"] = new("sys-a", "Alpha", 3, 0, 0),
            ["sys-b"] = new("sys-b", "Beta",  12, 0, 0),
            ["sys-c"] = new("sys-c", "Gamma", 7,  0, 0),
        };

        var result = RegionallyKnownBuilder.Build(
            visited, new List<CompletedMissionRecord>(),
            currentGameSeconds: OneDay);

        Assert.NotNull(result);
        Assert.Equal(3, result!.Count);
        Assert.Equal("Beta",  result[0].System);  // 12 visits
        Assert.Equal("Gamma", result[1].System);  // 7 visits
        Assert.Equal("Alpha", result[2].System);  // 3 visits
    }

    [Fact]
    public void Build_CapsAtMaxEntries()
    {
        var visited = new Dictionary<string, VisitedSystem>();
        for (int i = 0; i < RegionallyKnownBuilder.MaxEntries + 5; i++)
        {
            visited[$"sys-{i}"] = new($"sys-{i}", $"Sys{i}",
                VisitCount: RegionallyKnownBuilder.MinVisitsThreshold + i,
                FirstVisitGameSeconds: 0, LastVisitGameSeconds: 0);
        }

        var result = RegionallyKnownBuilder.Build(
            visited, new List<CompletedMissionRecord>(),
            currentGameSeconds: OneDay);

        Assert.NotNull(result);
        Assert.Equal(RegionallyKnownBuilder.MaxEntries, result!.Count);
    }

    [Fact]
    public void Build_SkipsMissionsInUnvisitedSystems()
    {
        // The completed-log may contain missions in systems the player
        // no longer has visit records for (data drift, sidecar
        // hand-editing). Those missions must not leak into
        // regionally_known — only visited systems map through.
        var visited = new Dictionary<string, VisitedSystem>
        {
            ["sys-a"] = new("sys-a", "Alpha", 5, 0, OneDay),
        };
        var log = new List<CompletedMissionRecord>
        {
            MakeRecord("m1", "Alpha", "gather", OneDay),
            MakeRecord("m2", "Beta",  "combat", OneDay),  // Beta not visited
        };

        var result = RegionallyKnownBuilder.Build(
            visited, log, currentGameSeconds: TwoDays);

        Assert.NotNull(result);
        Assert.Single(result!);
        Assert.Equal("Alpha", result![0].System);
    }

    [Fact]
    public void Build_NegativeAge_ClampedToZeroDays()
    {
        // Defensive: a save-load roundtrip with clock drift could yield
        // resolvedGameSeconds slightly ahead of currentGameSeconds.
        // Treat as "today," not negative-days.
        var visited = new Dictionary<string, VisitedSystem>
        {
            ["sys-a"] = new("sys-a", "Alpha", 5, 0,
                LastVisitGameSeconds: OneDay * 100),  // ahead of clock
        };

        var result = RegionallyKnownBuilder.Build(
            visited, new List<CompletedMissionRecord>(),
            currentGameSeconds: OneDay);

        Assert.Equal(0, result![0].LastVisitDaysAgo);
    }

    private static CompletedMissionRecord MakeRecord(
        string storyId, string systemName, string archetype, double resolvedGameSeconds) =>
        new(StoryId:             storyId,
            BrokerName:          "Test Broker",
            StationId:           "station-x",
            StationName:         "Station X",
            SourceFaction:       "TradingGuild",
            MissionName:         "Test Mission",
            Archetype:           archetype,
            Outcome:             "completed",
            MissionLevel:        5,
            SystemName:          systemName,
            MagnitudeScore:      3,
            ResolvedGameSeconds: resolvedGameSeconds,
            ResolvedRealUtc:     "2026-04-23T00:00:00Z");
}
