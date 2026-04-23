using System;
using System.Collections.Generic;
using System.Linq;
using VGAnima.Llm;
using VGAnima.Persistence;
using Xunit;

namespace VGAnima.Tests.Llm;

public class JournalContextBuilderTests
{
    private const double OneDay = 86400.0;

    private static CompletedMissionRecord Rec(
        string storyId, string stationId, string faction,
        int magnitude, string missionName = "Test Mission",
        string archetype = MissionArchetypes.Combat,
        string outcome   = CompletedMissionOutcomes.Completed,
        double resolvedGameSeconds = 1000.0) =>
        new(storyId, "A Broker", stationId, "A Station", faction,
            missionName, archetype, outcome,
            MissionLevel: 10, SystemName: "A System",
            MagnitudeScore: magnitude,
            ResolvedGameSeconds: resolvedGameSeconds,
            ResolvedRealUtc:     "2026-04-22T00:00:00Z");

    // Default graph closure: "always adjacent" (1 jump). Lands in the
    // base-0 distance band, so any magnitude ≥ 0 reaches.
    private static Func<string, int> AlwaysAdjacent() => _ => 1;

    // "Far but reachable" (8 jumps). Base requirement becomes 4.
    private static Func<string, int> AlwaysFar() => _ => 8;

    // "Unreachable" (returns MaxValue). Every record fails the reach
    // check — tests exclude-when-unreachable paths.
    private static Func<string, int> AlwaysUnreachable() => _ => int.MaxValue;

    [Fact]
    public void Build_EmptyLog_ReturnsEmptyWindows()
    {
        var section = JournalContextBuilder.Build(
            new List<CompletedMissionRecord>(), "station-1", "SalvageGuild",
            AlwaysAdjacent());
        Assert.Empty(section.Local);
        Assert.Empty(section.Network);
        Assert.Empty(section.Rumors);
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
        var section = JournalContextBuilder.Build(
            log, "station-A", "SalvageGuild", AlwaysAdjacent());
        Assert.Equal(2, section.Local.Count);
        Assert.All(section.Local, e => Assert.Contains(e.StoryId, new[] { "s1", "s3" }));
        // Local entries always get JumpsFromHere = 0 — we were there.
        Assert.All(section.Local, e => Assert.Equal(0, e.JumpsFromHere));
    }

    [Fact]
    public void Build_NetworkWindow_SameFactionWithinReach_PopulatesJumpsFromHere()
    {
        // Same-faction neighbor: base-0 distance band → every mag reaches.
        // JumpsFromHere reflects the distance the adapter reported.
        var log = new[]
        {
            Rec("s1", "station-A", "SalvageGuild", 3),   // local, excluded from network
            Rec("s2", "station-B", "SalvageGuild", 1),   // network (mag-1 still reaches at 1 jump)
            Rec("s3", "station-C", "SalvageGuild", 3),   // network
            Rec("s4", "station-D", "MiningGuild",  3),   // different faction → rumors, not network
        };
        var section = JournalContextBuilder.Build(
            log, "station-A", "SalvageGuild", AlwaysAdjacent());
        Assert.Equal(2, section.Network.Count);
        Assert.All(section.Network,
            e => Assert.Contains(e.StoryId, new[] { "s2", "s3" }));
        Assert.All(section.Network, e => Assert.Equal(1, e.JumpsFromHere));
    }

    [Fact]
    public void Build_GossipAlwaysTravelsToNeighbors_EvenAtMagnitudeOne()
    {
        // Product decision locked in 2026-04-23: "in reallife gossip
        // travels always faster" — mag-1 events must reach 1-2 jumps
        // out unconditionally (base-0 distance band).
        var log = new[]
        {
            Rec("trivial", "station-B", "SalvageGuild", magnitude: 1,
                outcome: CompletedMissionOutcomes.Abandoned),
        };
        var section = JournalContextBuilder.Build(
            log, "station-A", "SalvageGuild", AlwaysAdjacent());
        Assert.Single(section.Network);
        Assert.Equal("trivial", section.Network[0].StoryId);
    }

    [Fact]
    public void Build_FarEvent_LowMagnitude_DoesNotReach()
    {
        // 12 jumps (band-4 base = 6), same-faction bonus (-1) → required 5.
        // Mag-3 fails (stays out); mag-6 passes (lands in Network).
        var log = new[]
        {
            Rec("far-trivial", "station-far", "SalvageGuild", magnitude: 3),
            Rec("far-notable", "station-far", "SalvageGuild", magnitude: 6),
        };
        var section = JournalContextBuilder.Build(
            log, "station-A", "SalvageGuild", _ => 12, currentGameSeconds: 0);
        Assert.Single(section.Network);
        Assert.Equal("far-notable", section.Network[0].StoryId);
        // Rumors would only catch it if it had a non-same-faction reason
        // to reach, which it doesn't at this distance without fame.
        Assert.DoesNotContain(section.Rumors, e => e.StoryId == "far-trivial");
    }

    [Fact]
    public void Build_UnreachableStation_ExcludedFromAllWindows()
    {
        // Station the adapter can't resolve (destroyed / unknown guid)
        // fails the reach check on MaxValue distance. Must be filtered
        // out of network AND rumors so the LLM never sees it as
        // hearsay.
        var log = new[]
        {
            Rec("ghost", "station-gone", "SalvageGuild", magnitude: 10),
        };
        var section = JournalContextBuilder.Build(
            log, "station-A", "SalvageGuild", AlwaysUnreachable());
        Assert.Empty(section.Network);
        Assert.Empty(section.Rumors);
    }

    [Fact]
    public void Build_RumorsWindow_CrossFactionStorylines()
    {
        // Cross-faction events that meet the cross-faction reach gate
        // (no same-faction bonus) land in rumors, not network.
        var log = new[]
        {
            Rec("own-faction", "station-B", "SalvageGuild", magnitude: 3),  // network
            Rec("marauders",   "station-C", "Marauders",    magnitude: 3),  // rumors
        };
        var section = JournalContextBuilder.Build(
            log, "station-A", "SalvageGuild", AlwaysAdjacent());
        Assert.Single(section.Network);
        Assert.Equal("own-faction", section.Network[0].StoryId);
        Assert.Single(section.Rumors);
        Assert.Equal("marauders", section.Rumors[0].StoryId);
    }

    [Fact]
    public void Build_AgedEvent_FadesFromReach()
    {
        // 10 jumps away (base=4), mag 4, 40 days old → age penalty +3 →
        // required 7 → mag-4 fails. Drop it.
        var ancient = Rec("ancient", "station-far", "SalvageGuild",
            magnitude: 4, resolvedGameSeconds: 0);
        var log = new[] { ancient };

        var section = JournalContextBuilder.Build(
            log, "station-A", "SalvageGuild",
            _ => 10,
            currentGameSeconds: 40 * OneDay);

        Assert.Empty(section.Network);
        Assert.Empty(section.Rumors);
    }

    [Fact]
    public void Build_FreshEvent_SameMagnitudeAndDistance_PassesReach()
    {
        // Same distance + magnitude as the Build_AgedEvent case, but
        // fresh — required stays at 4, mag-4 passes.
        var fresh = Rec("fresh", "station-far", "SalvageGuild",
            magnitude: 4, resolvedGameSeconds: 39 * OneDay);
        var log = new[] { fresh };

        var section = JournalContextBuilder.Build(
            log, "station-A", "SalvageGuild",
            _ => 10,
            currentGameSeconds: 40 * OneDay);

        Assert.Single(section.Network);
    }

    [Fact]
    public void Build_PlayerFame_PushesOtherwiseUnreachableEventsThrough()
    {
        // 12 jumps (base=6), same-faction bonus (-1) → required 5 at
        // fame=0. Mag-3 fails. With fame=15 (bonus -3), required drops
        // to 2, mag-3 passes. Models the "CEO is known everywhere" rule.
        var log = new[]
        {
            Rec("noteworthy", "station-far", "SalvageGuild", magnitude: 3),
        };

        var withoutFame = JournalContextBuilder.Build(
            log, "station-A", "SalvageGuild", _ => 12,
            currentGameSeconds: 0, playerFame: 0);
        var withFame = JournalContextBuilder.Build(
            log, "station-A", "SalvageGuild", _ => 12,
            currentGameSeconds: 0, playerFame: 15);

        Assert.Empty(withoutFame.Network);
        Assert.Empty(withoutFame.Rumors);
        Assert.Single(withFame.Network);
    }

    [Fact]
    public void Build_StoryIds_AppearInAtMostOneWindow()
    {
        // A record surfaced in Local must not also appear in Network or
        // Rumors. Keeps the LLM's three-lens framing honest — an event
        // is only told one way.
        var log = new[]
        {
            Rec("big-local",  "station-A", "SalvageGuild", magnitude: 10),
            Rec("big-net",    "station-B", "SalvageGuild", magnitude: 10),
            Rec("big-rumor",  "station-C", "MiningGuild",  magnitude: 10),
        };
        var section = JournalContextBuilder.Build(
            log, "station-A", "SalvageGuild", AlwaysAdjacent());

        var allStoryIds = section.Local.Select(e => e.StoryId)
            .Concat(section.Network.Select(e => e.StoryId))
            .Concat(section.Rumors.Select(e => e.StoryId))
            .ToList();
        Assert.Equal(allStoryIds.Count, allStoryIds.Distinct().Count());
    }

    [Fact]
    public void Build_WindowsRespectSizeCaps()
    {
        // Overfill each window type — all at same station (→ local
        // cap), all same-faction-elsewhere at 1 jump (→ network cap),
        // all cross-faction at 1 jump magnitude-high (→ rumors cap).
        var log = Enumerable.Range(0, 10).Select(i =>
            Rec($"loc{i}", "station-A", "SalvageGuild", 5))
            .Concat(Enumerable.Range(0, 10).Select(i =>
                Rec($"net{i}", $"station-B{i}", "SalvageGuild", 5)))
            .Concat(Enumerable.Range(0, 10).Select(i =>
                Rec($"rum{i}", $"station-C{i}", "Marauders", 5)))
            .ToList();

        var section = JournalContextBuilder.Build(
            log, "station-A", "SalvageGuild", AlwaysAdjacent());

        Assert.Equal(JournalContextBuilder.LocalWindowSize,   section.Local.Count);
        Assert.Equal(JournalContextBuilder.NetworkWindowSize, section.Network.Count);
        Assert.Equal(JournalContextBuilder.RumorsWindowSize,  section.Rumors.Count);
    }

    [Fact]
    public void Build_OrdersRecentFirst()
    {
        var log = new[]
        {
            Rec("oldest", "station-A", "SalvageGuild", 3, resolvedGameSeconds: 100),
            Rec("middle", "station-A", "SalvageGuild", 3, resolvedGameSeconds: 200),
            Rec("newest", "station-A", "SalvageGuild", 3, resolvedGameSeconds: 300),
        };
        var section = JournalContextBuilder.Build(
            log, "station-A", "SalvageGuild", AlwaysAdjacent());
        Assert.Equal("newest", section.Local[0].StoryId);
        Assert.Equal("middle", section.Local[1].StoryId);
        Assert.Equal("oldest", section.Local[2].StoryId);
    }

    [Fact]
    public void Build_DistanceLookup_CachedPerUniqueStationGuid()
    {
        // Same station guid across multiple records should only trigger
        // one jump-distance lookup — the cache matters for perf at
        // scale (50 completed missions × multiple duplicates).
        var callCount = 0;
        Func<string, int> instrumented = _ => { callCount++; return 1; };

        var log = new[]
        {
            Rec("s1", "station-B", "SalvageGuild", 3),
            Rec("s2", "station-B", "SalvageGuild", 3),
            Rec("s3", "station-B", "SalvageGuild", 3),
        };
        JournalContextBuilder.Build(
            log, "station-A", "SalvageGuild", instrumented);

        // Two windows (network + rumors) both do the same station lookup,
        // but the per-build cache collapses them to one call per unique
        // station guid. With only station-B represented, we expect 1.
        Assert.Equal(1, callCount);
    }

    // ---------- Active window (in-flight entries, unchanged behavior) ----------

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
            new List<CompletedMissionRecord>(), "station-A", "SalvageGuild",
            AlwaysAdjacent(), inFlight: null);
        Assert.Empty(section.Active);
    }

    [Fact]
    public void Build_Active_FiltersToSameStationOrSameFaction()
    {
        var inFlight = new[]
        {
            InFlight("e1", "station-A", "SalvageGuild"),
            InFlight("e2", "station-X", "MiningGuild"),
            InFlight("e3", "station-B", "SalvageGuild"),
        };
        var section = JournalContextBuilder.Build(
            new List<CompletedMissionRecord>(), "station-A", "SalvageGuild",
            AlwaysAdjacent(), inFlight: inFlight);
        Assert.Equal(2, section.Active.Count);
        Assert.All(section.Active,
            e => Assert.Contains(e.StoryId, new[] { "e1", "e3" }));
    }

    [Fact]
    public void Build_Active_OrdersByStationThenFaction()
    {
        var inFlight = new[]
        {
            InFlight("e-far",    "station-B", "SalvageGuild", createdGameSeconds: 50),
            InFlight("e-local1", "station-A", "SalvageGuild", createdGameSeconds: 20),
            InFlight("e-local2", "station-A", "SalvageGuild", createdGameSeconds: 30),
        };
        var section = JournalContextBuilder.Build(
            new List<CompletedMissionRecord>(), "station-A", "SalvageGuild",
            AlwaysAdjacent(), inFlight: inFlight);
        Assert.Equal(3, section.Active.Count);
        Assert.Equal("e-local2", section.Active[0].StoryId);
        Assert.Equal("e-local1", section.Active[1].StoryId);
        Assert.Equal("e-far",    section.Active[2].StoryId);
    }

    [Fact]
    public void Build_Active_MarksEntriesAsInProgress()
    {
        var inFlight = new[] { InFlight("e1", "station-A", "SalvageGuild") };
        var section = JournalContextBuilder.Build(
            new List<CompletedMissionRecord>(), "station-A", "SalvageGuild",
            AlwaysAdjacent(), inFlight: inFlight);
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
            AlwaysAdjacent(), inFlight: inFlight);
        Assert.Equal(JournalContextBuilder.ActiveWindowSize, section.Active.Count);
    }
}
