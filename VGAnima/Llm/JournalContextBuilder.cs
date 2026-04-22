using System.Collections.Generic;
using System.Linq;
using VGAnima.Missions;
using VGAnima.Persistence;

namespace VGAnima.Llm;

/// <summary>Filters the <see cref="PersistedBrokerRegistry"/>'s completed-
/// mission log through an NPC-perspective lens: each broker only sees
/// what they would plausibly know given their station, faction, and the
/// event's magnitude. Three windows compose the broker's view:
/// <list type="bullet">
///   <item><c>local</c> — recent events at THIS station. Bar gossip.
///   Always visible, highest fidelity.</item>
///   <item><c>factional</c> — recent events involving the SAME faction
///   at OTHER stations. Internal-network chatter.</item>
///   <item><c>notable</c> — high-magnitude events across the galaxy.
///   Famous deeds travel regardless of affinity.</item>
/// </list>
/// The three lists may overlap in principle — the caller is expected to
/// just concatenate-with-dedup when rendering into context.
///
/// <para>All windows are returned recent-first (most recent at index 0),
/// since "last event first" is how a broker would recall. Size caps are
/// constants on this class; they shape both the context JSON size and
/// the LLM's attention.</para></summary>
internal static class JournalContextBuilder
{
    public const int LocalWindowSize     = 5;
    public const int FactionalWindowSize = 5;
    public const int NotableWindowSize   = 3;
    /// <summary>Cap on the in-flight window. In-flight entry count is
    /// naturally bounded (brokers are transient), so the cap is mostly
    /// defensive. 8 entries ≈ every possible offer across the player's
    /// current quadrant at busy pace.</summary>
    public const int ActiveWindowSize    = 8;

    /// <summary>Magnitude threshold above which an event enters the
    /// <c>notable</c> (galaxy-wide) window. Events at or above this
    /// score are "talked about everywhere." v1 threshold tuned so a
    /// level-12 combat mission barely qualifies; level-5 gather jobs
    /// don't. See <see cref="MagnitudeScorer"/>.</summary>
    public const int NotableMinMagnitude = 7;

    /// <summary>Builds a filtered view of the journal for the broker
    /// at <paramref name="stationId"/> aligned with
    /// <paramref name="factionIdentifier"/>. Inputs are read-only; each
    /// collection is scanned exactly once per call.
    ///
    /// <para><paramref name="inFlight"/> holds the registry's currently
    /// offered + accepted entries. When null, the <c>active</c> window
    /// is empty — callers that don't want duplicate-avoidance guidance
    /// can pass null explicitly.</para></summary>
    public static LlmJournalSection Build(
        IReadOnlyList<CompletedMissionRecord> log,
        string stationId,
        string factionIdentifier,
        IReadOnlyCollection<PersistedEntry>? inFlight = null)
    {
        // Recent-first iteration — log is stored oldest-first, so reverse
        // it once and slice the three windows off that.
        var recentFirst = log.Reverse().ToList();

        var local = recentFirst
            .Where(r => r.StationId == stationId)
            .Take(LocalWindowSize)
            .Select(ToSnapshot)
            .ToList();

        // Factional excludes local entries so the two windows don't
        // duplicate. Same-faction events elsewhere only.
        var factional = recentFirst
            .Where(r => r.SourceFaction == factionIdentifier
                        && r.StationId != stationId)
            .Take(FactionalWindowSize)
            .Select(ToSnapshot)
            .ToList();

        // Notable excludes anything already in local OR factional — a
        // given record appears in at most one window. This keeps the
        // context compact and makes the "three lens" framing honest to
        // the LLM (no event is told three different ways).
        var localStoryIds     = new HashSet<string>(local.Select(s => s.StoryId));
        var factionalStoryIds = new HashSet<string>(factional.Select(s => s.StoryId));
        var notable = recentFirst
            .Where(r => r.MagnitudeScore >= NotableMinMagnitude
                        && !localStoryIds.Contains(r.StoryId)
                        && !factionalStoryIds.Contains(r.StoryId))
            .Take(NotableWindowSize)
            .Select(ToSnapshot)
            .ToList();

        var active = BuildActiveWindow(inFlight, stationId, factionIdentifier);

        return new LlmJournalSection
        {
            Local     = local,
            Factional = factional,
            Notable   = notable,
            Active    = active,
        };
    }

    /// <summary>Projects in-flight persisted entries (state = offered or
    /// accepted) into the <c>active</c> window, filtered by the same
    /// proximity lens as the other windows. Union of "at this station"
    /// and "same faction elsewhere" — broker awareness, not omniscience.
    /// Magnitude is recomputed on the fly since in-flight entries don't
    /// carry a resolved outcome yet.</summary>
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

    private static LlmJournalEntry ToSnapshot(CompletedMissionRecord r) =>
        new(r.StoryId, r.MissionName, r.Archetype, r.Outcome, r.SourceFaction,
            r.StationName, r.SystemName, r.ResolvedGameSeconds, r.MagnitudeScore);

    private static LlmJournalEntry InFlightSnapshot(PersistedEntry e)
    {
        var block     = e.MissionBlock;
        var archetype = ArchetypeInferrer.Infer(block);
        // Use the entry's "created" time as the journal timestamp for
        // in-flight missions — that's when the offer appeared.
        // Magnitude is re-scored with a synthetic "completed" outcome so
        // a NotableMinMagnitude-comparable value surfaces; LLM uses it
        // only for relative weighting, not for outcome-aware scoring.
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
            Magnitude:           magnitude);
    }
}
