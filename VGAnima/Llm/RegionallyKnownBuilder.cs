using System.Collections.Generic;
using System.Linq;
using VGAnima.MissionJournal;
using VGAnima.Persistence;
using VGMissionJournal.Logging;

namespace VGAnima.Llm;

/// <summary>Builds the <c>regionally_known</c> context section — the
/// list of systems where the player is enough of a regular that a
/// broker there can plausibly recognize their face.
///
/// <para>Two distinct signals compose each entry:
/// <list type="bullet">
///   <item><b>Face recognition</b> — driven by <see cref="VisitedSystem.VisitCount"/>.
///         Threshold (<see cref="MinVisitsThreshold"/>) filters tourist-
///         grade traffic; anything ≥ 3 counts.</item>
///   <item><b>Recent activity</b> — archetype of the most recent
///         terminated mission in that system, only when resolved less
///         than <see cref="StaleActivityDaysThreshold"/> game-days ago.
///         Filled → broker can say "you've been salvaging here, yeah?";
///         null → broker falls back to face-only ("seen you around").</item>
/// </list>
/// Mission history is sourced from VGMissionJournal via
/// <see cref="VgMissionJournalBridge"/>; previously this read VGAnima's
/// own completed-mission log, which was retired in MJ-T4 when
/// VGMissionJournal became the single source of truth for resolved
/// missions. Vanilla missions now contribute to recent_activity too —
/// the broker's memory of "what the player's been up to" isn't limited
/// to LLM-authored jobs.</para></summary>
internal static class RegionallyKnownBuilder
{
    /// <summary>Minimum visits before a system enters the list. Two
    /// transits are "passing through" — three is a pattern.</summary>
    public const int MinVisitsThreshold = 3;

    /// <summary>Maximum entries emitted — caps prompt growth at a
    /// predictable size regardless of how many systems the player has
    /// wandered through. Sorted by visit count desc before truncation.</summary>
    public const int MaxEntries = 10;

    /// <summary>Recency cap on <c>recent_activity</c>. Missions older
    /// than this in game-time no longer represent current behavior;
    /// better to flag face-only than mislead the broker's pitch.</summary>
    public const double StaleActivityDaysThreshold = 30.0;

    /// <summary>Builds the list. Returns null when no system clears the
    /// visit threshold — lets <c>ContextGatherer</c>'s null-omit
    /// serialization keep the JSON key out of the prompt entirely for
    /// fresh saves.</summary>
    public static IReadOnlyList<LlmRegionallyKnownEntry>? Build(
        IReadOnlyDictionary<string, VisitedSystem> visitedSystems,
        VgMissionJournalBridge bridge,
        double currentGameSeconds)
    {
        if (visitedSystems.Count == 0) return null;

        var sinceGameSeconds = currentGameSeconds > StaleActivityDaysThreshold * 86400.0
            ? currentGameSeconds - StaleActivityDaysThreshold * 86400.0
            : 0.0;

        var entries = visitedSystems.Values
            .Where(v => v.VisitCount >= MinVisitsThreshold)
            .OrderByDescending(v => v.VisitCount)
            .ThenBy(v => v.Name, System.StringComparer.Ordinal)
            .Take(MaxEntries)
            .Select(v => BuildEntry(v, bridge, sinceGameSeconds, currentGameSeconds))
            .ToList();

        return entries.Count == 0 ? null : entries;
    }

    private static LlmRegionallyKnownEntry BuildEntry(
        VisitedSystem visited,
        VgMissionJournalBridge bridge,
        double sinceGameSeconds,
        double currentGameSeconds)
    {
        var lastVisitDaysAgo = SecondsToDays(currentGameSeconds - visited.LastVisitGameSeconds);

        // Per-system query scoped to the freshness window. The
        // GetMissionsInSystem prefilter uses AcceptedAtGameSeconds, which
        // can under-filter (a mission accepted inside the window but
        // resolved later would pass prefilter but fail the explicit
        // terminal-age check below). Both guards matter.
        string? recentActivity = null;
        var records = bridge.GetMissionsInSystem(visited.Guid, sinceGameSeconds);
        MissionRecord? mostRecent = null;
        foreach (var r in records)
        {
            if (r.IsActive) continue;                    // only terminated count
            var terminalAt = r.TerminalAtGameSeconds ?? 0.0;
            if (currentGameSeconds - terminalAt > StaleActivityDaysThreshold * 86400.0)
                continue;                                // still too old
            if (mostRecent is null
                || terminalAt > (mostRecent.TerminalAtGameSeconds ?? 0.0))
                mostRecent = r;
        }
        if (mostRecent is not null)
            recentActivity = MissionRecordArchetype.Infer(mostRecent);

        return new LlmRegionallyKnownEntry(
            System:           visited.Name,
            Visits:           visited.VisitCount,
            LastVisitDaysAgo: (int)lastVisitDaysAgo,
            RecentActivity:   recentActivity);
    }

    private static double SecondsToDays(double seconds)
    {
        var days = seconds / 86400.0;
        return days < 0 ? 0 : days;
    }
}
