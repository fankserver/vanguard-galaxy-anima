using System;
using System.Collections.Generic;
using System.Linq;
using VGAnima.Llm;
using VGAnima.MissionJournal;
using VGAnima.Persistence;
using VGMissionJournal.Api;
using VGMissionJournal.Logging;
using Xunit;

namespace VGAnima.Tests.Llm;

public class RegionallyKnownBuilderTests
{
    private const double OneDay  = 86400.0;
    private const double TwoDays = OneDay * 2;

    /// <summary>Fake IMissionJournalQuery honoring GetMissionsInSystem
    /// (the only method RegionallyKnownBuilder invokes). Other methods
    /// throw — if the builder grows a new dependency the tests will
    /// crash loudly instead of returning silent empty data.</summary>
    private sealed class FakeQuery : IMissionJournalQuery
    {
        private readonly List<MissionRecord> _records;
        public FakeQuery(IEnumerable<MissionRecord> records) { _records = records.ToList(); }

        public IReadOnlyList<MissionRecord> GetMissionsInSystem(
            string systemId, double sinceGameSeconds = 0.0, double untilGameSeconds = double.MaxValue)
        {
            return _records.Where(r =>
                r.SourceSystemId == systemId
                && r.AcceptedAtGameSeconds >= sinceGameSeconds
                && r.AcceptedAtGameSeconds <= untilGameSeconds).ToList();
        }

        public int SchemaVersion => 1;
        public int TotalMissionCount => _records.Count;
        public double? OldestAcceptedGameSeconds => null;
        public double? NewestAcceptedGameSeconds => null;
        public MissionRecord? GetMission(string id) => null;
        public IReadOnlyList<MissionRecord> GetActiveMissions() => Array.Empty<MissionRecord>();
        public IReadOnlyList<MissionRecord> GetAllMissions() => _records;
        public IReadOnlyList<MissionRecord> GetMissionsByFaction(string s, double a = 0, double b = double.MaxValue) => throw new NotImplementedException();
        public IReadOnlyList<MissionRecord> GetMissionsByMissionSubclass(string s, double a = 0, double b = double.MaxValue) => throw new NotImplementedException();
        public IReadOnlyList<MissionRecord> GetMissionsByOutcome(Outcome o, double a = 0, double b = double.MaxValue) => throw new NotImplementedException();
        public IReadOnlyList<MissionRecord> GetMissionsWithObjective(string s, double a = 0, double b = double.MaxValue) => throw new NotImplementedException();
        public IReadOnlyList<MissionRecord> GetMissionsForStoryId(string s) => throw new NotImplementedException();
        public IReadOnlyList<MissionRecord> GetRecentMissions(int n) => throw new NotImplementedException();
        public IReadOnlyList<MissionRecord> GetMissionsWithinJumps(string s, int m, Func<string, string, int> f, double a = 0, double b = double.MaxValue) => throw new NotImplementedException();
        public IReadOnlyDictionary<string, int> CountByMissionSubclass(double a = 0, double b = double.MaxValue) => throw new NotImplementedException();
        public IReadOnlyDictionary<Outcome, int> CountByOutcome(double a = 0, double b = double.MaxValue) => throw new NotImplementedException();
        public IReadOnlyDictionary<string, int> CountBySystem(double a = 0, double b = double.MaxValue) => throw new NotImplementedException();
        public IReadOnlyDictionary<string, int> CountByFaction(double a = 0, double b = double.MaxValue) => throw new NotImplementedException();
        public IReadOnlyList<SystemActivity> MostActiveSystemsInRange(string s, Func<string, string, int> f, int m, int n, double a = 0, double b = double.MaxValue) => throw new NotImplementedException();
    }

    /// <summary>Build a resolved MissionRecord whose subclass/objectives
    /// map to <paramref name="archetypeHint"/> via MissionRecordArchetype
    /// — that's what the builder calls to derive recent_activity.
    /// Objective types mirror what vanilla's own missions emit (BountyHunt
    /// → KillEnemies, SalvageWreck → Salvage, MineOre → Mining).</summary>
    private static MissionRecord MakeRec(
        string systemId, string archetypeHint, double terminalAtGameSeconds)
    {
        var subclass   = "Mission";
        var objectives = new List<MissionObjectiveDefinition>();
        switch (archetypeHint)
        {
            case "combat":
                subclass = "BountyMission";
                objectives.Add(new("KillEnemies", null));
                break;
            case "salvage": objectives.Add(new("Salvage", null)); break;
            case "mining":  objectives.Add(new("Mining",  null)); break;
        }
        var steps = objectives.Count == 0
            ? new List<MissionStepDefinition>()
            : new List<MissionStepDefinition>
            {
                new(Description: null, RequireAllObjectives: true, Hidden: false, Objectives: objectives),
            };
        return new MissionRecord(
            StoryId: "", MissionInstanceId: $"inst-{systemId}-{terminalAtGameSeconds}",
            MissionName: "Test", MissionSubclass: subclass, MissionLevel: 10,
            SourceStationId: null, SourceStationName: null,
            SourceSystemId: systemId, SourceSystemName: systemId + "-name",
            SourceSectorId: null, SourceSectorName: null,
            SourceFaction: null,
            TargetStationId: null, TargetStationName: null, TargetSystemId: null,
            PlayerLevel: 0, PlayerShipName: null, PlayerShipLevel: null, PlayerCurrentSystemId: null,
            Steps: steps, Rewards: new List<MissionRewardSnapshot>(),
            Timeline: new List<TimelineEntry>
            {
                new(TimelineState.Accepted, Math.Max(0, terminalAtGameSeconds - 10), "x"),
                new(TimelineState.Completed, terminalAtGameSeconds, "x"),
            });
    }

    private static VgMissionJournalBridge BridgeWith(params MissionRecord[] records) =>
        new VgMissionJournalBridge(new FakeQuery(records));

    // ---- Empty / threshold ----

    [Fact]
    public void Build_EmptyVisited_ReturnsNull()
    {
        var result = RegionallyKnownBuilder.Build(
            new Dictionary<string, VisitedSystem>(),
            BridgeWith(),
            currentGameSeconds: TwoDays);
        Assert.Null(result);
    }

    [Fact]
    public void Build_AllBelowThreshold_ReturnsNull()
    {
        var visited = new Dictionary<string, VisitedSystem>
        {
            ["sys-a"] = new("sys-a", "Alpha", VisitCount: 2,
                FirstVisitGameSeconds: 0, LastVisitGameSeconds: 0),
        };
        var result = RegionallyKnownBuilder.Build(
            visited, BridgeWith(), currentGameSeconds: TwoDays);
        Assert.Null(result);
    }

    [Fact]
    public void Build_MeetsThreshold_ReturnsEntryWithFaceOnlyRecognition()
    {
        var visited = new Dictionary<string, VisitedSystem>
        {
            ["sys-a"] = new("sys-a", "Alpha", VisitCount: 3,
                FirstVisitGameSeconds: 0,
                LastVisitGameSeconds:  OneDay * 2),
        };
        var result = RegionallyKnownBuilder.Build(
            visited, BridgeWith(), currentGameSeconds: OneDay * 5);
        Assert.NotNull(result);
        Assert.Single(result!);
        Assert.Equal("Alpha", result![0].System);
        Assert.Equal(3, result[0].Visits);
        Assert.Equal(3, result[0].LastVisitDaysAgo);
        Assert.Null(result[0].RecentActivity);
    }

    // ---- Recent activity signal ----

    [Fact]
    public void Build_RecentMissionInSystem_PopulatesRecentActivity()
    {
        var visited = new Dictionary<string, VisitedSystem>
        {
            ["sys-a"] = new("sys-a", "Alpha", VisitCount: 5,
                FirstVisitGameSeconds: 0, LastVisitGameSeconds: OneDay * 10),
        };
        var result = RegionallyKnownBuilder.Build(
            visited,
            BridgeWith(MakeRec("sys-a", "salvage", terminalAtGameSeconds: OneDay * 5)),
            currentGameSeconds: OneDay * 10);
        Assert.NotNull(result);
        Assert.NotNull(result![0].RecentActivity);
        Assert.Equal(new[] { "collect_salvage" }, result[0].RecentActivity);
    }

    [Fact]
    public void Build_StaleMission_LeavesRecentActivityNull()
    {
        // Mission resolved 40 days ago — beyond the 30-day staleness
        // window. Bridge's sinceGameSeconds prefilter may already drop
        // it; explicit terminal-age check is redundant armor. Either
        // way recent_activity stays null.
        var visited = new Dictionary<string, VisitedSystem>
        {
            ["sys-a"] = new("sys-a", "Alpha", VisitCount: 5,
                FirstVisitGameSeconds: 0, LastVisitGameSeconds: OneDay * 50),
        };
        var result = RegionallyKnownBuilder.Build(
            visited,
            BridgeWith(MakeRec("sys-a", "salvage", terminalAtGameSeconds: OneDay * 10)),
            currentGameSeconds: OneDay * 50);
        Assert.NotNull(result);
        Assert.Null(result![0].RecentActivity);
    }

    [Fact]
    public void Build_MultipleMissionsInSystem_PicksMostRecentObjectives()
    {
        var visited = new Dictionary<string, VisitedSystem>
        {
            ["sys-a"] = new("sys-a", "Alpha", VisitCount: 5,
                FirstVisitGameSeconds: 0, LastVisitGameSeconds: OneDay * 10),
        };
        var result = RegionallyKnownBuilder.Build(
            visited,
            BridgeWith(
                MakeRec("sys-a", "salvage", terminalAtGameSeconds: OneDay * 3),
                MakeRec("sys-a", "combat",  terminalAtGameSeconds: OneDay * 8)),
            currentGameSeconds: OneDay * 10);
        Assert.NotNull(result![0].RecentActivity);
        Assert.Equal(new[] { "kill_enemies" }, result[0].RecentActivity);
    }

    // ---- Ordering / caps ----

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
            visited, BridgeWith(), currentGameSeconds: OneDay);
        Assert.NotNull(result);
        Assert.Equal(3, result!.Count);
        Assert.Equal("Beta",  result[0].System);
        Assert.Equal("Gamma", result[1].System);
        Assert.Equal("Alpha", result[2].System);
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
            visited, BridgeWith(), currentGameSeconds: OneDay);
        Assert.NotNull(result);
        Assert.Equal(RegionallyKnownBuilder.MaxEntries, result!.Count);
    }

    [Fact]
    public void Build_SkipsMissionsInUnvisitedSystems()
    {
        // Bridge has a mission in sys-b, but only sys-a is a regular.
        // Builder only queries per-visited-system so sys-b never maps in.
        var visited = new Dictionary<string, VisitedSystem>
        {
            ["sys-a"] = new("sys-a", "Alpha", 5, 0, OneDay),
        };
        var result = RegionallyKnownBuilder.Build(
            visited,
            BridgeWith(
                MakeRec("sys-a", "mining", terminalAtGameSeconds: OneDay),
                MakeRec("sys-b", "combat", terminalAtGameSeconds: OneDay)),
            currentGameSeconds: TwoDays);
        Assert.NotNull(result);
        Assert.Single(result!);
        Assert.Equal("Alpha", result![0].System);
    }

    [Fact]
    public void Build_NegativeAge_ClampedToZeroDays()
    {
        var visited = new Dictionary<string, VisitedSystem>
        {
            ["sys-a"] = new("sys-a", "Alpha", 5, 0,
                LastVisitGameSeconds: OneDay * 100),
        };
        var result = RegionallyKnownBuilder.Build(
            visited, BridgeWith(), currentGameSeconds: OneDay);
        Assert.Equal(0, result![0].LastVisitDaysAgo);
    }
}
