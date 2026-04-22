using System.Collections.Generic;
using System.Linq;
using Source.Galaxy;
using Source.Galaxy.POI;

namespace VGAnima.Llm;

/// <summary>Builds the list of destinations the LLM may pick for intents
/// that need a target station (<c>deliver_to_station</c> /
/// <c>haul_goods</c>).
///
/// <para>Starts at the broker's station and walks one jumpgate hop
/// outward, collecting every <see cref="SpaceStation"/> it finds
/// (excluding the broker's own station). One hop is the sweet spot —
/// zero hops is too narrow (many systems have only the broker's own
/// station), two+ hops explodes the list and the LLM picks poorly.</para>
///
/// <para>Ranked by:
/// <list type="number">
///   <item>fewest jumps first (prefer in-system);</item>
///   <item>same-faction-as-broker first (within same jumps — narrative
///         nudge: SalvageGuild brokers talk to Steel Vultures stations);</item>
///   <item>station name alphabetical (stable tie-breaker).</item>
/// </list>
/// Result is capped at <see cref="DefaultMaxCount"/> entries and given
/// sequential <c>dest_0</c>..<c>dest_N</c> ids AFTER ranking, so the LLM
/// always sees the top pick as <c>dest_0</c>.</para></summary>
internal static class AccessibleDestinationsBuilder
{
    internal const int DefaultMaxCount = 8;

    /// <summary>Enumerates vanilla state to collect reachable stations and
    /// hands off to <see cref="Rank"/> for ordering and capping. Returns an
    /// empty list when <paramref name="brokerStation"/> is null or has no
    /// attached system (guards against mid-construction broker references).</summary>
    public static IReadOnlyList<AccessibleDestination> Build(
        SpaceStation? brokerStation, int maxCount = DefaultMaxCount)
    {
        if (brokerStation == null || brokerStation.system == null)
            return System.Array.Empty<AccessibleDestination>();

        var brokerFaction = brokerStation.faction?.identifier;
        var candidates    = new List<AccessibleDestination>();

        // 0 jumps away: SpaceStations in the broker's own system, excluding
        // the broker's station itself. Common case: many systems have 1-3
        // stations the broker could plausibly dispatch work to.
        foreach (var poi in brokerStation.system.pointsOfInterest)
        {
            if (poi is SpaceStation st && st.guid != brokerStation.guid)
                candidates.Add(ToDestination(st, jumps: 0, brokerFaction));
        }

        // 1 jump away: every SpaceStation in a system reachable by an open
        // jumpgate in the broker's system. `canUseJumpGate` = gate is
        // accessible AND the gate POI itself isn't hidden (vanilla line 61645).
        foreach (var poi in brokerStation.system.pointsOfInterest)
        {
            if (poi is JumpGate jg && jg.canUseJumpGate && jg.targetSystem != null)
            {
                foreach (var tPoi in jg.targetSystem.pointsOfInterest)
                {
                    if (tPoi is SpaceStation st)
                        candidates.Add(ToDestination(st, jumps: 1, brokerFaction));
                }
            }
        }

        return Rank(candidates, maxCount);
    }

    /// <summary>Pure ranking + cap. Exposed separately from <see cref="Build"/>
    /// so unit tests can exercise ordering without needing live Unity /
    /// vanilla state.</summary>
    public static IReadOnlyList<AccessibleDestination> Rank(
        IEnumerable<AccessibleDestination> candidates, int maxCount)
    {
        var ordered = candidates
            .OrderBy(c => c.JumpsAway)
            .ThenByDescending(c => c.SameFactionAsBroker)
            .ThenBy(c => c.StationName, System.StringComparer.Ordinal)
            .Take(maxCount)
            .ToList();

        // Re-label sequentially after ranking so `dest_0` is always the top
        // pick. We avoid leaking the pre-rank order via the ids — the LLM
        // sees stable ordering regardless of input order.
        var result = new List<AccessibleDestination>(ordered.Count);
        for (var i = 0; i < ordered.Count; i++)
            result.Add(ordered[i] with { ShortId = $"dest_{i}" });
        return result;
    }

    private static AccessibleDestination ToDestination(
        SpaceStation station, int jumps, string? brokerFaction)
    {
        var factionId          = station.faction?.identifier ?? "Unknown";
        var factionDisplayName = station.faction?.name ?? "Unknown";
        return new AccessibleDestination(
            ShortId:              string.Empty,    // assigned after ranking
            StationName:          station.name ?? "Unknown Station",
            SystemName:           station.system?.name ?? "Unknown System",
            FactionIdentifier:    factionId,
            FactionDisplayName:   factionDisplayName,
            JumpsAway:            jumps,
            SameFactionAsBroker:  brokerFaction != null && factionId == brokerFaction,
            Guid:                 station.guid);
    }
}
