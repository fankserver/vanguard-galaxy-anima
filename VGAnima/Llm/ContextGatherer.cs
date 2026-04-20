using System.Collections.Generic;
using System.Linq;

namespace VGAnima.Llm;

/// <summary>Flattens an <see cref="IGameStateView"/> into an <see cref="LlmContext"/>
/// ready for JSON serialization. Enforces spec §4 truncation caps:
/// <list type="bullet">
///   <item><c>stored_ships</c> → first 10</item>
///   <item><c>crew</c> → first 10</item>
///   <item><c>connected_systems</c> → first 8 (2-jump radius)</item>
///   <item><c>archive_recent</c> → last 10 (most recent = end of list)</item>
///   <item><c>waypoints</c> → first 5</item>
/// </list>
/// <see cref="IGameStateView"/> already yields the "natural" lists; the
/// gatherer applies these additional caps on top before building the context.
/// Deterministic, side-effect-free, safe to call from the Unity main thread.</summary>
internal sealed class ContextGatherer
{
    public LlmContext Gather(IGameStateView view, BrokerInfo broker)
    {
        return new LlmContext
        {
            Player = new LlmPlayerSection
            {
                Level             = view.PlayerLevel,
                Credits           = view.PlayerCredits,
                Specialization    = view.PlayerSpecialization,
                BountyRank        = view.BountyRank,
                PatrolRank        = view.PatrolRank,
                IndustryRank      = view.IndustryRank,
                MaxBountyLevel    = view.MaxBountyLevel,
                MaxPatrolLevel    = view.MaxPatrolLevel,
                MaxIndustryLevel  = view.MaxIndustryLevel,
                UnlockedTitles    = view.UnlockedTitles,
                ActiveMissionCount = view.ActiveMissionCount,
                ActiveMissionCap   = view.ActiveMissionCap,
            },
            Fleet = new LlmFleetSection
            {
                PrimaryShip = view.PrimaryShip,
                StoredShips = view.StoredShips.Take(10).ToList(),
                Crew        = view.Crew.Take(10).ToList(),
            },
            CargoContents = view.CargoContents,
            Location = new LlmLocationSection
            {
                CurrentStation    = view.CurrentStationName,
                StationFaction    = view.StationFaction,
                StationFacilities = view.StationFacilities,
                CurrentSystem     = view.CurrentSystemName,
                CurrentSector     = view.CurrentSectorName,
                Quadrant          = view.Quadrant,
                ConnectedSystems  = view.ConnectedSystems.Take(8).ToList(),
            },
            Reputation = view.Reputation,
            AtWar      = view.AtWar,
            Missions = new LlmMissionsSection
            {
                ActiveStoryIds       = view.ActiveStoryIds,
                // archive tail — most recent 10 entries.
                ArchiveRecent        = TakeLast(view.ArchiveRecent, 10),
                CurrentBountyLevel   = view.CurrentBountyLevel,
                CurrentPatrolLevel   = view.CurrentPatrolLevel,
                CurrentIndustryLevel = view.CurrentIndustryLevel,
            },
            StoryArcsActive = view.StoryArcsActive,
            Waypoints       = view.Waypoints.Take(5).ToList(),
            Time = new LlmTimeSection
            {
                ElapsedSeconds = view.ElapsedSeconds,
                // Game days ≈ 86400 elapsed-seconds per day (matches vanilla bar refresh cadence).
                DayOfYear      = ((int)(view.ElapsedSeconds / 86400.0) % 365) + 1,
            },
            Broker = new LlmBrokerSection
            {
                Name           = broker.Name,
                IsMale         = broker.IsMale,
                Seed           = broker.Seed,
                StationFaction = broker.StationFaction,
            },
        };
    }

    private static IReadOnlyList<T> TakeLast<T>(IReadOnlyList<T> source, int n)
    {
        // netstandard2.1 lacks LINQ's TakeLast on IReadOnlyList<T> generically,
        // and Enumerable.TakeLast requires IEnumerable<T>. Open-code to avoid
        // allocating twice.
        if (source.Count <= n) return source;
        var list = new List<T>(n);
        for (var i = source.Count - n; i < source.Count; i++) list.Add(source[i]);
        return list;
    }
}
