using System.Collections.Generic;

namespace VGAnima.Galaxy;

/// <summary>Jump-count distance between any two systems. Independent of
/// the player's current location — required by
/// <c>JournalContextBuilder</c>'s reach formula where we need to measure
/// "how far is a previously-resolved station from the broker I'm voicing
/// right now?"
///
/// <para>Vanilla's <c>TravelManager.JumpDistanceToPoi</c> implicitly
/// starts from <c>GamePlayer.current.currentSystem</c> (not
/// overridable), which only answers "how far from me," so we replicate
/// the BFS pattern from <c>TravelManager.GenerateShortestRoute</c>
/// (decomp line 106934) independently.</para>
///
/// <para>Design choice: delegate-based graph accessor rather than a
/// dedicated interface — lets tests pass a dict-backed closure without
/// instantiating vanilla types, and keeps production wiring a one-line
/// lambda over <c>SystemMapData.GetAdjacentSystems()</c>.</para>
///
/// <para>Callers are expected to memoize for hot paths. At current scale
/// (≤50 completed missions × 1 broker per bar refresh) the BFS runs in
/// sub-millisecond per call and doesn't need an internal cache, but
/// <c>JournalContextBuilder</c> should cache per-context-build so 50
/// lookups collapse to a handful of computed values.</para></summary>
internal static class GalaxyDistance
{
    /// <summary>Returns the minimum number of jumpgate hops between
    /// <paramref name="fromGuid"/> and <paramref name="toGuid"/>, or
    /// <c>-1</c> if no path exists (disconnected pocket system or
    /// invalid guid).
    ///
    /// <para>Symmetric: <c>JumpsBetween(A, B) == JumpsBetween(B, A)</c>
    /// when both systems are connected. The BFS walks forward from
    /// <paramref name="fromGuid"/>, so the "unreachable" case only fires
    /// when the source has no path to the target — any
    /// <paramref name="fromGuid"/> unknown to <paramref name="getAdjacent"/>
    /// simply yields an empty neighbor set and then <c>-1</c>.</para>
    ///
    /// <param name="getAdjacent">Closure returning the neighbor guids
    /// reachable from a given system via a usable jumpgate. Production
    /// implementation must filter on <c>JumpGate.canUseJumpGate</c> so
    /// locked gates don't fake connectivity. Returning an empty sequence
    /// for an unknown guid is the expected failure mode.</param></summary>
    public static int JumpsBetween(
        string fromGuid,
        string toGuid,
        System.Func<string, IEnumerable<string>> getAdjacent)
    {
        if (string.IsNullOrEmpty(fromGuid) || string.IsNullOrEmpty(toGuid)) return -1;
        if (fromGuid == toGuid) return 0;

        var visited = new HashSet<string>(System.StringComparer.Ordinal) { fromGuid };
        var queue   = new Queue<(string Guid, int Depth)>();
        queue.Enqueue((fromGuid, 0));

        while (queue.Count > 0)
        {
            var (current, depth) = queue.Dequeue();
            foreach (var neighbor in getAdjacent(current))
            {
                if (neighbor == null || !visited.Add(neighbor)) continue;
                if (neighbor == toGuid) return depth + 1;
                queue.Enqueue((neighbor, depth + 1));
            }
        }
        return -1;
    }
}
