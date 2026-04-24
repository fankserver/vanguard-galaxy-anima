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

public class JournalContextBuilderTests
{
    private const double OneDay = 86400.0;

    private const string BrokerStation = "station-A";
    private const string BrokerSystem  = "system-A";

    // ---- Fake bridge source ----

    /// <summary>Fake IMissionJournalQuery. Only the methods
    /// JournalContextBuilder actually calls are implemented — the rest
    /// throw (compile-time safety: if the builder starts calling
    /// something new, tests blow up immediately).</summary>
    private sealed class FakeQuery : IMissionJournalQuery
    {
        private readonly List<MissionRecord> _records;
        public FakeQuery(IEnumerable<MissionRecord> records) { _records = records.ToList(); }

        public IReadOnlyList<MissionRecord> GetMissionsWithinJumps(
            string pivotSystemId, int maxJumps,
            Func<string, string, int> jumpDistance,
            double sinceGameSeconds = 0.0,
            double untilGameSeconds = double.MaxValue)
        {
            return _records.Where(r =>
            {
                if (string.IsNullOrEmpty(r.SourceSystemId)) return false;
                var terminal = r.TerminalAtGameSeconds ?? r.AcceptedAtGameSeconds;
                if (terminal < sinceGameSeconds || terminal > untilGameSeconds) return false;
                var d = jumpDistance(r.SourceSystemId!, pivotSystemId);
                return d >= 0 && d <= maxJumps;
            }).ToList();
        }

        public int SchemaVersion => 1;
        public int TotalMissionCount => _records.Count;
        public double? OldestAcceptedGameSeconds => null;
        public double? NewestAcceptedGameSeconds => null;
        public MissionRecord? GetMission(string id) => null;
        public IReadOnlyList<MissionRecord> GetActiveMissions() => Array.Empty<MissionRecord>();
        public IReadOnlyList<MissionRecord> GetAllMissions() => _records;
        public IReadOnlyList<MissionRecord> GetMissionsInSystem(string s, double a = 0, double b = double.MaxValue) => throw new NotImplementedException();
        public IReadOnlyList<MissionRecord> GetMissionsByFaction(string s, double a = 0, double b = double.MaxValue) => throw new NotImplementedException();
        public IReadOnlyList<MissionRecord> GetMissionsByMissionSubclass(string s, double a = 0, double b = double.MaxValue) => throw new NotImplementedException();
        public IReadOnlyList<MissionRecord> GetMissionsByOutcome(Outcome o, double a = 0, double b = double.MaxValue) => throw new NotImplementedException();
        public IReadOnlyList<MissionRecord> GetMissionsWithObjective(string s, double a = 0, double b = double.MaxValue) => throw new NotImplementedException();
        public IReadOnlyList<MissionRecord> GetMissionsForStoryId(string s) => throw new NotImplementedException();
        public IReadOnlyList<MissionRecord> GetRecentMissions(int n) => throw new NotImplementedException();
        public IReadOnlyDictionary<string, int> CountByMissionSubclass(double a = 0, double b = double.MaxValue) => throw new NotImplementedException();
        public IReadOnlyDictionary<Outcome, int> CountByOutcome(double a = 0, double b = double.MaxValue) => throw new NotImplementedException();
        public IReadOnlyDictionary<string, int> CountBySystem(double a = 0, double b = double.MaxValue) => throw new NotImplementedException();
        public IReadOnlyDictionary<string, int> CountByFaction(double a = 0, double b = double.MaxValue) => throw new NotImplementedException();
        public IReadOnlyList<SystemActivity> MostActiveSystemsInRange(string s, Func<string, string, int> f, int m, int n, double a = 0, double b = double.MaxValue) => throw new NotImplementedException();
    }

    /// <summary>Builds a resolved MissionRecord fixture. Terminal state
    /// + timeline wired up so <see cref="MissionRecord.Outcome"/> returns
    /// a real value (MagnitudeDerivation checks Outcome for decay).</summary>
    private static MissionRecord Rec(
        string storyIdOrInstance,
        string sourceStationId,
        string sourceSystemId,
        string sourceFaction,
        int    missionLevel = 10,
        string subclass     = "Mission",
        TimelineState terminal = TimelineState.Completed,
        double acceptedAtGameSeconds = 100.0,
        double terminalAtGameSeconds = 200.0)
    {
        var timeline = new List<TimelineEntry>
        {
            new(TimelineState.Accepted, acceptedAtGameSeconds, "2026-04-22T00:00:00Z"),
            new(terminal, terminalAtGameSeconds, "2026-04-22T00:01:00Z"),
        };

        return new MissionRecord(
            StoryId: storyIdOrInstance,
            MissionInstanceId: storyIdOrInstance,
            MissionName: "Test Mission",
            MissionSubclass: subclass,
            MissionLevel: missionLevel,
            SourceStationId: sourceStationId,
            SourceStationName: sourceStationId + "-name",
            SourceSystemId: sourceSystemId,
            SourceSystemName: sourceSystemId + "-name",
            SourceSectorId: null, SourceSectorName: null,
            SourceFaction: sourceFaction,
            TargetStationId: null, TargetStationName: null, TargetSystemId: null,
            PlayerLevel: 0,
            PlayerShipName: null, PlayerShipLevel: null, PlayerCurrentSystemId: null,
            Steps: new List<MissionStepDefinition>(),
            Rewards: new List<MissionRewardSnapshot>(),
            Timeline: timeline);
    }

    private static VgMissionJournalBridge BridgeWith(params MissionRecord[] records) =>
        new VgMissionJournalBridge(new FakeQuery(records));

    // Default jump closure: "always adjacent" (1 jump).
    private static Func<string, string, int> AlwaysAdjacent() => (_, _) => 1;

    // "Far but reachable" — 12 jumps (band 4, base=6).
    private static Func<string, string, int> AlwaysFar(int jumps = 12) => (_, _) => jumps;

    // ---- Empty / absent bridge ----

    [Fact]
    public void Build_NoRecords_ReturnsEmptyWindows()
    {
        var section = JournalContextBuilder.Build(
            BridgeWith(),
            BrokerStation, BrokerSystem, "SalvageGuild",
            AlwaysAdjacent());
        Assert.Empty(section.Local);
        Assert.Empty(section.Network);
        Assert.Empty(section.Rumors);
    }

    [Fact]
    public void Build_EmptyBrokerSystem_SkipsBridgeQuery_ReturnsEmpty()
    {
        // Empty system guid = broker location unknown → reach math would
        // be meaningless. Builder should short-circuit to empty resolved
        // windows rather than let the bridge throw.
        var section = JournalContextBuilder.Build(
            BridgeWith(Rec("s1", BrokerStation, BrokerSystem, "SalvageGuild", missionLevel: 10)),
            BrokerStation, brokerSystemId: "", factionIdentifier: "SalvageGuild",
            AlwaysAdjacent());
        Assert.Empty(section.Local);
        Assert.Empty(section.Network);
        Assert.Empty(section.Rumors);
    }

    // ---- Local window ----

    [Fact]
    public void Build_LocalWindow_FiltersByBrokerStationId()
    {
        var section = JournalContextBuilder.Build(
            BridgeWith(
                Rec("s1", BrokerStation, BrokerSystem,    "SalvageGuild"),
                Rec("s2", "station-B",   "system-B",      "SalvageGuild"),
                Rec("s3", BrokerStation, BrokerSystem,    "MiningGuild"),
                Rec("s4", "station-C",   "system-C",      "SalvageGuild")),
            BrokerStation, BrokerSystem, "SalvageGuild", AlwaysAdjacent());
        Assert.Equal(2, section.Local.Count);
        Assert.All(section.Local, e => Assert.Contains(e.StoryId, new[] { "s1", "s3" }));
        Assert.All(section.Local, e => Assert.Equal(0, e.JumpsFromHere));
    }

    // ---- Network window ----

    [Fact]
    public void Build_NetworkWindow_SameFactionWithinReach_PopulatesJumpsFromHere()
    {
        var section = JournalContextBuilder.Build(
            BridgeWith(
                Rec("s1", BrokerStation, BrokerSystem, "SalvageGuild"),     // local
                Rec("s2", "station-B",   "system-B",   "SalvageGuild"),     // network
                Rec("s3", "station-C",   "system-C",   "SalvageGuild"),     // network
                Rec("s4", "station-D",   "system-D",   "MiningGuild")),     // rumors
            BrokerStation, BrokerSystem, "SalvageGuild", AlwaysAdjacent());
        Assert.Equal(2, section.Network.Count);
        Assert.All(section.Network,
            e => Assert.Contains(e.StoryId, new[] { "s2", "s3" }));
        Assert.All(section.Network, e => Assert.Equal(1, e.JumpsFromHere));
    }

    [Fact]
    public void Build_GossipAlwaysTravelsToNeighbors_EvenAtMagnitudeOne()
    {
        // Product decision 2026-04-23: "gossip always travels" — base-0
        // distance band accepts any magnitude. Level 2 abandoned = mag
        // clamped to 1 (2/2 - 2 = -1 → clamp 1).
        var section = JournalContextBuilder.Build(
            BridgeWith(Rec("trivial", "station-B", "system-B", "SalvageGuild",
                missionLevel: 2, terminal: TimelineState.Abandoned)),
            BrokerStation, BrokerSystem, "SalvageGuild", AlwaysAdjacent());
        Assert.Single(section.Network);
        Assert.Equal("trivial", section.Network[0].StoryId);
    }

    [Fact]
    public void Build_FarEvent_LowMagnitude_DoesNotReach()
    {
        // 12 jumps = band 4 base=6, same-faction bonus -1 → required 5.
        // Level 4 = mag 2 (no combat, no multi-step) → OUT.
        // Level 12 BountyMission = 6 + 1 combat = 7 → IN.
        var section = JournalContextBuilder.Build(
            BridgeWith(
                Rec("far-trivial", "station-far", "system-far", "SalvageGuild", missionLevel: 4),
                Rec("far-notable", "station-far", "system-far", "SalvageGuild", missionLevel: 12, subclass: "BountyMission")),
            BrokerStation, BrokerSystem, "SalvageGuild", AlwaysFar(),
            currentGameSeconds: 0);
        Assert.Single(section.Network);
        Assert.Equal("far-notable", section.Network[0].StoryId);
        Assert.DoesNotContain(section.Rumors, e => e.StoryId == "far-trivial");
    }

    [Fact]
    public void Build_RecordOutsideMaxReachJumps_ExcludedFromAllWindows()
    {
        // 20 jumps > MaxReachJumps (15) → bridge prefilter drops it.
        var section = JournalContextBuilder.Build(
            BridgeWith(Rec("too-far", "station-gone", "system-gone", "SalvageGuild", missionLevel: 20)),
            BrokerStation, BrokerSystem, "SalvageGuild", AlwaysFar(jumps: 20));
        Assert.Empty(section.Network);
        Assert.Empty(section.Rumors);
    }

    // ---- Rumors window ----

    [Fact]
    public void Build_RumorsWindow_CrossFactionStorylines()
    {
        var section = JournalContextBuilder.Build(
            BridgeWith(
                Rec("own-faction", "station-B", "system-B", "SalvageGuild"),
                Rec("marauders",   "station-C", "system-C", "Marauders")),
            BrokerStation, BrokerSystem, "SalvageGuild", AlwaysAdjacent());
        Assert.Single(section.Network);
        Assert.Equal("own-faction", section.Network[0].StoryId);
        Assert.Single(section.Rumors);
        Assert.Equal("marauders", section.Rumors[0].StoryId);
    }

    // ---- Reach modifiers ----

    [Fact]
    public void Build_AgedEvent_FadesFromReach()
    {
        // 10 jumps = band 4 base=4; age 40 days → +3 penalty; same-fac -1 → required 6.
        // Level 10 Mission = mag 5 → OUT.
        var ancient = Rec("ancient", "station-far", "system-far", "SalvageGuild",
            missionLevel: 10, terminalAtGameSeconds: 0);
        var section = JournalContextBuilder.Build(
            BridgeWith(ancient),
            BrokerStation, BrokerSystem, "SalvageGuild",
            (_, _) => 10,
            currentGameSeconds: 40 * OneDay);
        Assert.Empty(section.Network);
        Assert.Empty(section.Rumors);
    }

    [Fact]
    public void Build_PlayerFame_PushesOtherwiseUnreachableEventsThrough()
    {
        // 12 jumps = base 6, same-fac -1 → required 5 at fame 0.
        // Level 10 Mission = mag 5 — boundary IN. Use level 8 (mag 4) to
        // keep it OUT at fame 0, then flip with fame 15 (bonus -3 → req 2).
        var record = Rec("noteworthy", "station-far", "system-far", "SalvageGuild",
            missionLevel: 8);
        var withoutFame = JournalContextBuilder.Build(
            BridgeWith(record),
            BrokerStation, BrokerSystem, "SalvageGuild",
            AlwaysFar(), currentGameSeconds: 0, playerFame: 0);
        var withFame = JournalContextBuilder.Build(
            BridgeWith(record),
            BrokerStation, BrokerSystem, "SalvageGuild",
            AlwaysFar(), currentGameSeconds: 0, playerFame: 15);
        Assert.Empty(withoutFame.Network);
        Assert.Empty(withoutFame.Rumors);
        Assert.Single(withFame.Network);
    }

    // ---- Dedup across windows ----

    [Fact]
    public void Build_RecordsAppearInAtMostOneWindow()
    {
        var section = JournalContextBuilder.Build(
            BridgeWith(
                Rec("big-local", BrokerStation, BrokerSystem, "SalvageGuild", missionLevel: 20, subclass: "BountyMission"),
                Rec("big-net",   "station-B",   "system-B",   "SalvageGuild", missionLevel: 20, subclass: "BountyMission"),
                Rec("big-rumor", "station-C",   "system-C",   "MiningGuild",  missionLevel: 20, subclass: "BountyMission")),
            BrokerStation, BrokerSystem, "SalvageGuild", AlwaysAdjacent());
        var allIds = section.Local.Select(e => e.StoryId)
            .Concat(section.Network.Select(e => e.StoryId))
            .Concat(section.Rumors.Select(e => e.StoryId))
            .ToList();
        Assert.Equal(allIds.Count, allIds.Distinct().Count());
    }

    [Fact]
    public void Build_EmptyStoryId_UsesInstanceIdForDedup()
    {
        // Generator missions have empty StoryId — two records with empty
        // storyIds must NOT collapse in the dedup set. Record A resolves
        // local, record B has a distinct InstanceId and a station-B
        // source → should land in network independently.
        var a = new MissionRecord(
            StoryId: "", MissionInstanceId: "inst-A",
            MissionName: "Mission A", MissionSubclass: "Mission", MissionLevel: 10,
            SourceStationId: BrokerStation, SourceStationName: "A",
            SourceSystemId: BrokerSystem, SourceSystemName: "A",
            SourceSectorId: null, SourceSectorName: null,
            SourceFaction: "SalvageGuild",
            TargetStationId: null, TargetStationName: null, TargetSystemId: null,
            PlayerLevel: 0, PlayerShipName: null, PlayerShipLevel: null, PlayerCurrentSystemId: null,
            Steps: new List<MissionStepDefinition>(),
            Rewards: new List<MissionRewardSnapshot>(),
            Timeline: new List<TimelineEntry>
            {
                new(TimelineState.Accepted, 100, "x"),
                new(TimelineState.Completed, 200, "x"),
            });
        var b = a with { MissionInstanceId = "inst-B",
            SourceStationId = "station-B", SourceSystemId = "system-B" };

        var section = JournalContextBuilder.Build(
            BridgeWith(a, b),
            BrokerStation, BrokerSystem, "SalvageGuild", AlwaysAdjacent());

        Assert.Single(section.Local);
        Assert.Equal("inst-A", section.Local[0].StoryId);
        Assert.Single(section.Network);
        Assert.Equal("inst-B", section.Network[0].StoryId);
    }

    // ---- Window caps ----

    [Fact]
    public void Build_WindowsRespectSizeCaps()
    {
        var records = Enumerable.Range(0, 10).Select(i =>
            Rec($"loc{i}", BrokerStation, BrokerSystem, "SalvageGuild", missionLevel: 10,
                terminalAtGameSeconds: 1000 + i))
            .Concat(Enumerable.Range(0, 10).Select(i =>
                Rec($"net{i}", $"station-B{i}", $"system-B{i}", "SalvageGuild", missionLevel: 10,
                    terminalAtGameSeconds: 1000 + i)))
            .Concat(Enumerable.Range(0, 10).Select(i =>
                Rec($"rum{i}", $"station-C{i}", $"system-C{i}", "Marauders", missionLevel: 10,
                    terminalAtGameSeconds: 1000 + i)))
            .ToArray();

        var section = JournalContextBuilder.Build(
            BridgeWith(records),
            BrokerStation, BrokerSystem, "SalvageGuild", AlwaysAdjacent());

        Assert.Equal(JournalContextBuilder.LocalWindowSize,   section.Local.Count);
        Assert.Equal(JournalContextBuilder.NetworkWindowSize, section.Network.Count);
        Assert.Equal(JournalContextBuilder.RumorsWindowSize,  section.Rumors.Count);
    }

    [Fact]
    public void Build_OrdersRecentFirst()
    {
        var section = JournalContextBuilder.Build(
            BridgeWith(
                Rec("oldest", BrokerStation, BrokerSystem, "SalvageGuild", terminalAtGameSeconds: 100),
                Rec("middle", BrokerStation, BrokerSystem, "SalvageGuild", terminalAtGameSeconds: 200),
                Rec("newest", BrokerStation, BrokerSystem, "SalvageGuild", terminalAtGameSeconds: 300)),
            BrokerStation, BrokerSystem, "SalvageGuild", AlwaysAdjacent());
        Assert.Equal("newest", section.Local[0].StoryId);
        Assert.Equal("middle", section.Local[1].StoryId);
        Assert.Equal("oldest", section.Local[2].StoryId);
    }

    [Fact]
    public void Build_DistanceLookup_CachedPerUniqueSourceSystem()
    {
        var callCount = 0;
        Func<string, string, int> instrumented = (_, _) => { callCount++; return 1; };

        JournalContextBuilder.Build(
            BridgeWith(
                Rec("s1", "station-B1", "system-B", "SalvageGuild"),
                Rec("s2", "station-B2", "system-B", "SalvageGuild"),
                Rec("s3", "station-B3", "system-B", "SalvageGuild")),
            BrokerStation, BrokerSystem, "SalvageGuild", instrumented);

        // Bridge calls jumpDistance once per record during the
        // GetMissionsWithinJumps prefilter (3 calls). Then the builder's
        // distanceCache collapses per-window lookups to one per unique
        // source system (system-B → 1 call). Total: 3 (prefilter) + 1
        // (builder cache) = 4. Asserting ≤ 4 proves the builder's cache
        // eliminates redundant per-window lookups.
        Assert.True(callCount <= 4,
            $"Expected ≤4 jumpDistance calls (3 prefilter + 1 cached); got {callCount}");
    }

    [Fact]
    public void Build_ExcludesActiveRecordsFromResolvedWindows()
    {
        // In-flight record (no terminal entry) must not appear in
        // local/network/rumors — those are for resolved history only.
        var active = new MissionRecord(
            StoryId: "active-1", MissionInstanceId: "active-1",
            MissionName: "Still Running", MissionSubclass: "Mission", MissionLevel: 10,
            SourceStationId: BrokerStation, SourceStationName: "A",
            SourceSystemId: BrokerSystem, SourceSystemName: "A",
            SourceSectorId: null, SourceSectorName: null,
            SourceFaction: "SalvageGuild",
            TargetStationId: null, TargetStationName: null, TargetSystemId: null,
            PlayerLevel: 0, PlayerShipName: null, PlayerShipLevel: null, PlayerCurrentSystemId: null,
            Steps: new List<MissionStepDefinition>(),
            Rewards: new List<MissionRewardSnapshot>(),
            Timeline: new List<TimelineEntry> { new(TimelineState.Accepted, 100, "x") });

        var section = JournalContextBuilder.Build(
            BridgeWith(active),
            BrokerStation, BrokerSystem, "SalvageGuild", AlwaysAdjacent());
        Assert.Empty(section.Local);
        Assert.Empty(section.Network);
        Assert.Empty(section.Rumors);
    }

    // ---- Active window (in-flight PersistedEntry source, unchanged shape) ----

    private static PersistedEntry InFlight(
        string storyId, string stationId, string faction, double createdGameSeconds = 100.0)
    {
        var block = new LlmMissionBlock(
            Name: $"Mission-{storyId}", Description: "d", CompletionText: "c",
            SourceFaction: faction,
            Steps:   new[] { new LlmMissionStep(
                new ClearCombatSiteIntent("Marauders", "x")) },
            Rewards: new List<LlmReward>());
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
            BridgeWith(), BrokerStation, BrokerSystem, "SalvageGuild",
            AlwaysAdjacent(), inFlight: null);
        Assert.Empty(section.Active);
    }

    [Fact]
    public void Build_Active_FiltersToSameStationOrSameFaction()
    {
        var section = JournalContextBuilder.Build(
            BridgeWith(), BrokerStation, BrokerSystem, "SalvageGuild",
            AlwaysAdjacent(),
            inFlight: new[]
            {
                InFlight("e1", BrokerStation, "SalvageGuild"),
                InFlight("e2", "station-X",   "MiningGuild"),
                InFlight("e3", "station-B",   "SalvageGuild"),
            });
        Assert.Equal(2, section.Active.Count);
        Assert.All(section.Active,
            e => Assert.Contains(e.StoryId, new[] { "e1", "e3" }));
    }

    [Fact]
    public void Build_Active_OrdersByStationThenFaction()
    {
        var section = JournalContextBuilder.Build(
            BridgeWith(), BrokerStation, BrokerSystem, "SalvageGuild",
            AlwaysAdjacent(),
            inFlight: new[]
            {
                InFlight("e-far",    "station-B",    "SalvageGuild", createdGameSeconds: 50),
                InFlight("e-local1", BrokerStation,  "SalvageGuild", createdGameSeconds: 20),
                InFlight("e-local2", BrokerStation,  "SalvageGuild", createdGameSeconds: 30),
            });
        Assert.Equal(3, section.Active.Count);
        Assert.Equal("e-local2", section.Active[0].StoryId);
        Assert.Equal("e-local1", section.Active[1].StoryId);
        Assert.Equal("e-far",    section.Active[2].StoryId);
    }

    [Fact]
    public void Build_Active_MarksEntriesAsInProgress()
    {
        var section = JournalContextBuilder.Build(
            BridgeWith(), BrokerStation, BrokerSystem, "SalvageGuild",
            AlwaysAdjacent(),
            inFlight: new[] { InFlight("e1", BrokerStation, "SalvageGuild") });
        Assert.Single(section.Active);
        Assert.Equal(CompletedMissionOutcomes.InProgress, section.Active[0].Outcome);
    }

    [Fact]
    public void Build_Active_RespectsSizeCap()
    {
        var section = JournalContextBuilder.Build(
            BridgeWith(), BrokerStation, BrokerSystem, "SalvageGuild",
            AlwaysAdjacent(),
            inFlight: Enumerable.Range(0, 20)
                .Select(i => InFlight($"e{i}", BrokerStation, "SalvageGuild",
                                      createdGameSeconds: i))
                .ToList());
        Assert.Equal(JournalContextBuilder.ActiveWindowSize, section.Active.Count);
    }
}
