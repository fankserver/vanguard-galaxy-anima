using System.Collections.Generic;
using Source.Galaxy;
using Source.Galaxy.POI;

namespace VGAnima.Galaxy;

/// <summary>Thin wrapper over vanilla galaxy state that produces the
/// closures <see cref="GalaxyDistance.JumpsBetween"/> and
/// <see cref="VGAnima.Llm.JournalContextBuilder.Build"/> expect. Keeps
/// the vanilla-API coupling localized to one file so the rest of the
/// journal stack stays vanilla-agnostic and unit-testable.
///
/// <para><b>Filter contract:</b> neighbor iteration mirrors vanilla's
/// <c>TravelManager.GenerateShortestRoute</c> (decomp line 106934) —
/// iterate <c>system.pointsOfInterest</c>, keep only
/// <c>JumpGate</c>s that are <c>canUseJumpGate</c>. This respects
/// locked gates. <see cref="SystemMapData.GetAdjacentSystems"/> does
/// NOT filter locked gates (decomp line 60974), so we can't just
/// forward to it — that would over-report connectivity.</para></summary>
internal static class VanillaSystemGraph
{
    /// <summary>Returns the guids of systems reachable in one jump from
    /// the given system guid, or an empty sequence if the system isn't
    /// found or has no usable gates. Safe to call when
    /// <c>GalaxyMapData.current</c> is null (returns empty).</summary>
    public static IEnumerable<string> GetAdjacent(string systemGuid)
    {
        var galaxy = GalaxyMapData.current;
        if (galaxy == null) yield break;
        var system = galaxy.GetSystem(systemGuid);
        if (system == null) yield break;
        foreach (var poi in system.pointsOfInterest)
        {
            if (poi is JumpGate gate && gate.canUseJumpGate && gate.targetSystem != null)
            {
                yield return gate.targetSystem.guid;
            }
        }
    }

    /// <summary>Resolves a station (POI) guid to the guid of its host
    /// system, or null when the station no longer exists. Needed by
    /// <c>JournalContextBuilder</c>'s reach formula — completed-mission
    /// records store <c>StationId</c>, but the distance math works on
    /// <b>system</b> guids. Destroyed-station guids (Conquest can wipe
    /// stations mid-save) return null; callers treat that as
    /// unreachable.</summary>
    public static string? StationSystemGuid(string stationGuid)
    {
        var galaxy = GalaxyMapData.current;
        if (galaxy == null) return null;
        var poi = galaxy.GetPointOfInterest(stationGuid);
        return poi?.system?.guid;
    }

    /// <summary>Pre-bound closure for
    /// <c>JournalContextBuilder.Build(jumpsFromStationToBroker:)</c>.
    /// Given the broker's current system guid, returns a function that
    /// computes "jumps from ANY station's system to the broker's."
    /// <c>int.MaxValue</c> for stations whose system is gone or
    /// disconnected.</summary>
    public static System.Func<string, int> JumpsToBrokerFrom(string brokerSystemGuid) =>
        stationGuid =>
        {
            var fromSystemGuid = StationSystemGuid(stationGuid);
            if (string.IsNullOrEmpty(fromSystemGuid)) return int.MaxValue;
            var jumps = GalaxyDistance.JumpsBetween(
                fromSystemGuid!, brokerSystemGuid, GetAdjacent);
            // GalaxyDistance returns -1 for disconnected / unknown →
            // map to MaxValue so the reach formula uniformly treats
            // "out of reach" as infinite distance.
            return jumps < 0 ? int.MaxValue : jumps;
        };
}
