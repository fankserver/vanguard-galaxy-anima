using System.Collections.Generic;
using System.Linq;
using VGAnima.Persistence;

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
///         completed mission in that system, <i>only</i> when resolved
///         less than <see cref="StaleActivityDaysThreshold"/> game-days
///         ago. Filled → broker can say "you've been salvaging here,
///         yeah?"; null → broker falls back to face-only ("seen you
///         around").</item>
/// </list>
/// The freshness gate is deliberate: a three-month-old gather mission
/// should not hint that the player is STILL gathering — behavior
/// changes, and stale activity misinforms the broker's framing.</para>
///
/// <para>Clock is passed explicitly rather than pulled from
/// <c>Plugin.Clock</c> so the builder is unit-testable without Unity
/// runtime. Production caller (BarRefreshPatches) threads the canonical
/// clock in.</para></summary>
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
    /// better to flag face-only than mislead the broker's pitch.
    /// Derived from 30 × 86400 game-seconds-per-day.</summary>
    public const double StaleActivityDaysThreshold = 30.0;

    /// <summary>Builds the list. Returns null when no system clears the
    /// visit threshold — lets <c>ContextGatherer</c>'s null-omit
    /// serialization keep the JSON key out of the prompt entirely for
    /// fresh saves.</summary>
    public static IReadOnlyList<LlmRegionallyKnownEntry>? Build(
        IReadOnlyDictionary<string, VisitedSystem> visitedSystems,
        IReadOnlyList<CompletedMissionRecord> completedLog,
        double currentGameSeconds)
    {
        if (visitedSystems.Count == 0) return null;

        // Index completed-log by system name so the archetype lookup is
        // O(1) per system rather than N×M. We pick "most recent" via
        // ResolvedGameSeconds, so build a reverse-chronological iteration.
        // System names are the join key here (matches
        // CompletedMissionRecord.SystemName, which is a snapshot of
        // VisitedSystem.Name at resolution time). Using name not guid
        // because the completed-log stores the display name — both should
        // agree when the system hasn't been renamed.
        var mostRecentBySystem = new Dictionary<string, CompletedMissionRecord>(
            System.StringComparer.Ordinal);
        foreach (var record in completedLog)
        {
            if (!mostRecentBySystem.TryGetValue(record.SystemName, out var existing)
                || record.ResolvedGameSeconds > existing.ResolvedGameSeconds)
            {
                mostRecentBySystem[record.SystemName] = record;
            }
        }

        var entries = visitedSystems.Values
            .Where(v => v.VisitCount >= MinVisitsThreshold)
            .OrderByDescending(v => v.VisitCount)
            .ThenBy(v => v.Name, System.StringComparer.Ordinal)  // stable ordering
            .Take(MaxEntries)
            .Select(v => BuildEntry(v, mostRecentBySystem, currentGameSeconds))
            .ToList();

        return entries.Count == 0 ? null : entries;
    }

    private static LlmRegionallyKnownEntry BuildEntry(
        VisitedSystem visited,
        IReadOnlyDictionary<string, CompletedMissionRecord> mostRecentBySystem,
        double currentGameSeconds)
    {
        var lastVisitDaysAgo = SecondsToDays(currentGameSeconds - visited.LastVisitGameSeconds);

        string? recentActivity = null;
        if (mostRecentBySystem.TryGetValue(visited.Name, out var record))
        {
            var ageDays = SecondsToDays(currentGameSeconds - record.ResolvedGameSeconds);
            if (ageDays <= StaleActivityDaysThreshold)
                recentActivity = record.Archetype;
            // else: activity too stale — leave null so the prompt rule
            // falls back to face-only recognition.
        }

        return new LlmRegionallyKnownEntry(
            System:           visited.Name,
            Visits:           visited.VisitCount,
            LastVisitDaysAgo: (int)lastVisitDaysAgo,
            RecentActivity:   recentActivity);
    }

    private static double SecondsToDays(double seconds)
    {
        // Clamp negative to 0 — a slightly-future resolvedGameSeconds
        // (clock drift on save-load roundtrip) should read as "today,"
        // not as a negative age.
        var days = seconds / 86400.0;
        return days < 0 ? 0 : days;
    }
}
