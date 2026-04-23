using System;
using System.Collections.Generic;
using System.Linq;
using VGAnima.Missions;
using VGAnima.Persistence;

namespace VGAnima.Llm;

/// <summary>Filters the <see cref="PersistedBrokerRegistry"/>'s completed-
/// mission log through an NPC-perspective lens: each broker only sees
/// what they would plausibly know given their station, faction, the
/// event's magnitude, the distance from where it happened, its age,
/// and the player's galactic fame. Four windows compose the view:
/// <list type="bullet">
///   <item><c>local</c> — recent events at THIS station. Bar gossip.
///   Always visible, highest fidelity.</item>
///   <item><c>network</c> — resolved events within reach via the
///   faction's internal network (same <c>source_faction</c> as the
///   broker, close enough for the reach formula to pass).</item>
///   <item><c>rumors</c> — distant hearsay that reached the broker
///   despite being out-of-network — either different faction, or same
///   faction far enough away that only magnitude pushed it through.</item>
///   <item><c>active</c> — in-flight offered/accepted missions the
///   broker plausibly knows about. Duplicate-avoidance signal.</item>
/// </list>
///
/// <para>Reach is governed by <see cref="MagnitudeReachFormula"/>. Each
/// resolved record appears in at most one window; a storyId already
/// surfaced in <c>local</c> is excluded from <c>network</c> and
/// <c>rumors</c>, and a storyId in <c>network</c> is excluded from
/// <c>rumors</c>. The LLM's "three-lens" narrative framing stays honest
/// — no event is told three different ways.</para>
///
/// <para>Windows are returned recent-first (most recent at index 0);
/// size caps shape both context JSON size and the LLM's attention.</para></summary>
internal static class JournalContextBuilder
{
    public const int LocalWindowSize   = 5;
    public const int NetworkWindowSize = 5;
    public const int RumorsWindowSize  = 3;
    /// <summary>Cap on the in-flight window. In-flight entry count is
    /// naturally bounded (brokers are transient), so the cap is mostly
    /// defensive. 8 entries ≈ every possible offer across the player's
    /// current quadrant at busy pace.</summary>
    public const int ActiveWindowSize  = 8;

    /// <summary>Builds a filtered view of the journal for the broker
    /// at <paramref name="stationId"/> aligned with
    /// <paramref name="factionIdentifier"/>.
    ///
    /// <param name="jumpsFromStationToBroker">Given a resolved-station
    /// guid, returns the jump distance from THAT station's system to
    /// the broker's current system. Production implementation looks the
    /// station up via <c>GalaxyMapData.current.GetPointOfInterest</c>
    /// then runs <see cref="Galaxy.GalaxyDistance.JumpsBetween"/>
    /// against a vanilla jumpgate adapter. Returns <c>int.MaxValue</c>
    /// for destroyed / unknown stations so the reach check fails safely
    /// (filtered out as unreachable). <c>null</c> accepted for callers
    /// that don't care about distance — such entries land in rumors
    /// only when magnitude alone clears the unknown-distance band.</param>
    ///
    /// <param name="currentGameSeconds">Used by the age penalty in
    /// <see cref="MagnitudeReachFormula"/>. Zero disables age decay
    /// effectively (treats everything as fresh).</param>
    ///
    /// <param name="playerFame">Max of bounty / patrol / industry ranks.
    /// Feeds <see cref="MagnitudeReachFormula.FameBonus"/>.</param></summary>
    public static LlmJournalSection Build(
        IReadOnlyList<CompletedMissionRecord> log,
        string stationId,
        string factionIdentifier,
        Func<string, int>? jumpsFromStationToBroker = null,
        double currentGameSeconds = 0,
        int playerFame = 0,
        IReadOnlyCollection<PersistedEntry>? inFlight = null)
    {
        // Recent-first iteration — log is stored oldest-first, so reverse
        // it once and slice the windows off that.
        var recentFirst = log.Reverse().ToList();

        // Pre-compute per-record (jumpsAway, ageDays, sameFaction) so
        // each window filter doesn't redo the work. Memoizes the
        // station→jumps lookup implicitly: if the same station resolves
        // multiple missions we only pay the lookup cost once per unique
        // stationId (see distance cache below).
        var distanceCache = new Dictionary<string, int>(StringComparer.Ordinal);
        int JumpsFor(string stationGuid)
        {
            if (jumpsFromStationToBroker == null) return int.MaxValue;
            if (distanceCache.TryGetValue(stationGuid, out var cached)) return cached;
            var computed = jumpsFromStationToBroker(stationGuid);
            // Negative values from GalaxyDistance.JumpsBetween (disconnected)
            // map to MaxValue for the reach formula — unreachable is
            // unreachable regardless of why.
            if (computed < 0) computed = int.MaxValue;
            distanceCache[stationGuid] = computed;
            return computed;
        }

        var local = recentFirst
            .Where(r => r.StationId == stationId)
            .Take(LocalWindowSize)
            .Select(r => ToSnapshot(r, jumpsFromHere: 0))
            .ToList();

        var localStoryIds = new HashSet<string>(local.Select(s => s.StoryId));

        // Network window: same-faction events within reach, excluding
        // local entries. Reach formula combines distance, age, fame,
        // and same-faction bonus.
        var network = new List<LlmJournalEntry>();
        foreach (var r in recentFirst)
        {
            if (network.Count >= NetworkWindowSize) break;
            if (r.SourceFaction != factionIdentifier) continue;
            if (localStoryIds.Contains(r.StoryId))   continue;
            if (r.StationId    == stationId)         continue;

            var jumpsAway = JumpsFor(r.StationId);
            if (jumpsAway == int.MaxValue) continue;   // unreachable
            var ageDays = Math.Max(0, (currentGameSeconds - r.ResolvedGameSeconds) / 86400.0);

            if (MagnitudeReachFormula.Reaches(
                    r.MagnitudeScore, jumpsAway, ageDays,
                    sameFaction: true, fame: playerFame))
            {
                network.Add(ToSnapshot(r, jumpsFromHere: jumpsAway));
            }
        }

        var networkStoryIds = new HashSet<string>(network.Select(s => s.StoryId));

        // Rumors window: everything else in reach — different faction,
        // or same faction that didn't make the network cut for other
        // reasons (unlikely but possible). Cross-faction records get
        // sameFaction=false in the reach check, so they need one
        // magnitude-point more to push through.
        var rumors = new List<LlmJournalEntry>();
        foreach (var r in recentFirst)
        {
            if (rumors.Count >= RumorsWindowSize) break;
            if (localStoryIds.Contains(r.StoryId))   continue;
            if (networkStoryIds.Contains(r.StoryId)) continue;
            if (r.StationId    == stationId)         continue;

            var jumpsAway = JumpsFor(r.StationId);
            if (jumpsAway == int.MaxValue) continue;
            var ageDays = Math.Max(0, (currentGameSeconds - r.ResolvedGameSeconds) / 86400.0);
            var sameFaction = r.SourceFaction == factionIdentifier;

            if (MagnitudeReachFormula.Reaches(
                    r.MagnitudeScore, jumpsAway, ageDays,
                    sameFaction: sameFaction, fame: playerFame))
            {
                rumors.Add(ToSnapshot(r, jumpsFromHere: jumpsAway));
            }
        }

        var active = BuildActiveWindow(inFlight, stationId, factionIdentifier);

        return new LlmJournalSection
        {
            Local   = local,
            Network = network,
            Rumors  = rumors,
            Active  = active,
        };
    }

    /// <summary>Projects in-flight persisted entries (state = offered or
    /// accepted) into the <c>active</c> window, filtered by the same
    /// proximity lens as the other windows. Union of "at this station"
    /// and "same faction elsewhere" — broker awareness, not omniscience.
    /// Magnitude is recomputed on the fly since in-flight entries don't
    /// carry a resolved outcome yet.
    ///
    /// <para>The active window intentionally does NOT use the reach
    /// formula — we want the broker aware of any offered mission in
    /// their orbit regardless of magnitude, so duplicate-avoidance is
    /// robust for trivial hauls too.</para></summary>
    private static IReadOnlyList<LlmJournalEntry> BuildActiveWindow(
        IReadOnlyCollection<PersistedEntry>? inFlight,
        string stationId,
        string factionIdentifier)
    {
        if (inFlight is null || inFlight.Count == 0)
            return System.Array.Empty<LlmJournalEntry>();

        // Score: 2 = same station (highest priority), 1 = same faction
        // elsewhere, 0 = neither (filtered out). Stable order for ties
        // is "newest first" using createdGameSeconds.
        var ranked = new List<(int prio, PersistedEntry entry)>();
        foreach (var e in inFlight)
        {
            int prio;
            if (e.Broker.StationId == stationId)                            prio = 2;
            else if (e.MissionBlock.SourceFaction == factionIdentifier)     prio = 1;
            else                                                             continue;
            ranked.Add((prio, e));
        }
        ranked.Sort((a, b) =>
        {
            if (a.prio != b.prio) return b.prio.CompareTo(a.prio); // high prio first
            return b.entry.Timestamps.CreatedGameSeconds
                    .CompareTo(a.entry.Timestamps.CreatedGameSeconds);
        });

        return ranked
            .Take(ActiveWindowSize)
            .Select(x => InFlightSnapshot(x.entry))
            .ToList();
    }

    private static LlmJournalEntry ToSnapshot(CompletedMissionRecord r, int jumpsFromHere) =>
        new(r.StoryId, r.MissionName, r.Archetype, r.Outcome, r.SourceFaction,
            r.StationName, r.SystemName, r.ResolvedGameSeconds, r.MagnitudeScore,
            JumpsFromHere: jumpsFromHere);

    private static LlmJournalEntry InFlightSnapshot(PersistedEntry e)
    {
        var block     = e.MissionBlock;
        var archetype = ArchetypeInferrer.Infer(block);
        // Use the entry's "created" time as the journal timestamp for
        // in-flight missions — that's when the offer appeared.
        // Magnitude is re-scored with a synthetic "completed" outcome so
        // a comparable value surfaces; LLM uses it only for relative
        // weighting, not for outcome-aware scoring.
        var missionLevel = 10; // unknown at in-flight time; use a neutral default
        var magnitude    = MagnitudeScorer.Score(
            block, archetype, CompletedMissionOutcomes.Completed, missionLevel);
        return new LlmJournalEntry(
            StoryId:             e.StoryId,
            MissionName:         block.Name,
            Archetype:           archetype,
            Outcome:             CompletedMissionOutcomes.InProgress,
            SourceFaction:       block.SourceFaction,
            StationName:         e.Broker.StationNameSnapshot ?? "a station",
            SystemName:          e.Broker.SystemNameSnapshot  ?? string.Empty,
            ResolvedGameSeconds: e.Timestamps.CreatedGameSeconds,
            Magnitude:           magnitude,
            JumpsFromHere:       0);
    }
}
