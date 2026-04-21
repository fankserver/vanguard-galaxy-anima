using System.Collections.Generic;

namespace VGAnima.Persistence;

/// <summary>Drops stale entries at load time. Two rules:
/// <list type="bullet">
///   <item>Accepted entries whose storyId isn't in vanilla's active or
///     archived mission lists — the mission is gone from the player's
///     timeline; keeping the entry is a memory leak. This rule is
///     evaluated globally: vanilla's mission lists aren't station-scoped.</item>
///   <item>Offered entries whose broker seed doesn't match any patron at
///     the entry's <c>stationId</c> — the broker is gone; the cached
///     inference has nowhere to live. This rule is evaluated only for
///     entries whose <see cref="PersistedBroker.StationId"/> matches the
///     <c>currentStationId</c> the purger was called for. Entries for
///     other stations survive — we can't verify their brokers from a
///     different bar's patron list and premature purging would drop live
///     state (spec §8).</item>
/// </list>
/// Returns the storyIds dropped so callers can log them.</summary>
internal static class OrphanPurger
{
    public static IReadOnlyCollection<string> Purge(
        PersistedBrokerRegistry registry,
        IReadOnlyCollection<string> activeStoryIds,
        IReadOnlyCollection<string> archivedStoryIds,
        IReadOnlyCollection<string> knownPatronSeeds,
        string? currentStationId)
    {
        var activeSet   = new HashSet<string>(activeStoryIds);
        var archivedSet = new HashSet<string>(archivedStoryIds);
        var seedSet     = new HashSet<string>(knownPatronSeeds);

        var toDrop = new List<string>();
        foreach (var entry in registry.All())
        {
            bool isOrphan;
            switch (entry.State)
            {
                case PersistedEntryStates.Accepted:
                    // Global rule: mission must be in vanilla's lists somewhere.
                    isOrphan = !activeSet.Contains(entry.StoryId) && !archivedSet.Contains(entry.StoryId);
                    break;
                case PersistedEntryStates.Offered:
                    // Station-scoped rule: only verify at the entry's home station.
                    // Entries at other stations survive this pass; they'll be
                    // re-evaluated when the player visits that station's bar.
                    isOrphan = entry.Broker.StationId == currentStationId
                        && !seedSet.Contains(entry.Broker.Seed);
                    break;
                default:
                    // Unknown state label → drop (safe default; prevents leaks
                    // if a future state is added without purge-rule updates).
                    isOrphan = true;
                    break;
            }
            if (isOrphan) toDrop.Add(entry.StoryId);
        }

        foreach (var storyId in toDrop) registry.Remove(storyId);
        return toDrop;
    }
}
