using System.Collections.Generic;
using System.Linq;
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

    /// <summary>Magnitude threshold above which an event enters the
    /// <c>notable</c> (galaxy-wide) window. Events at or above this
    /// score are "talked about everywhere." v1 threshold tuned so a
    /// level-12 combat mission barely qualifies; level-5 gather jobs
    /// don't. See <see cref="MagnitudeScorer"/>.</summary>
    public const int NotableMinMagnitude = 7;

    /// <summary>Builds a filtered view of the journal log for the broker
    /// at <paramref name="stationId"/> aligned with
    /// <paramref name="factionIdentifier"/>. Inputs are read-only; the
    /// log is scanned exactly once per call.</summary>
    public static LlmJournalSection Build(
        IReadOnlyList<CompletedMissionRecord> log,
        string stationId,
        string factionIdentifier)
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

        return new LlmJournalSection
        {
            Local     = local,
            Factional = factional,
            Notable   = notable,
        };
    }

    private static LlmJournalEntry ToSnapshot(CompletedMissionRecord r) =>
        new(r.StoryId, r.MissionName, r.Archetype, r.Outcome, r.SourceFaction,
            r.StationName, r.SystemName, r.ResolvedGameSeconds, r.MagnitudeScore);
}
