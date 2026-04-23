using System.Collections.Generic;

namespace VGAnima.Llm;

/// <summary>Seam over <c>Source.Player.GamePlayer.current</c> + related
/// singletons for <see cref="ContextGatherer"/>. Every property is read-only:
/// gatherer reads the view, never writes back.
///
/// Production impl: <see cref="GameStateView"/>. Tests pass a hand-rolled fake
/// (see <c>ContextGathererTests.FakeGameStateView</c>).
///
/// Collection-typed properties already apply the "natural" cap of the game
/// world (e.g. the player's actual stored_ships list) — <see cref="ContextGatherer"/>
/// applies the <em>additional</em> spec §4 truncation caps (≤10 / ≤10 / ≤8 /
/// ≤10 / ≤5) on top, so both layers are well-defined.</summary>
internal interface IGameStateView
{
    // Player section
    int PlayerLevel { get; }
    // PlayerCredits intentionally omitted — bank balance is private-
    // knowledge; exposing it would break the same rule that removed
    // cargo-item classification and cargo_used_pct.
    string PlayerSpecialization { get; }
    int BountyRank { get; }
    int PatrolRank { get; }
    int IndustryRank { get; }
    int MaxBountyLevel { get; }
    int MaxPatrolLevel { get; }
    int MaxIndustryLevel { get; }
    IReadOnlyList<string> UnlockedTitles { get; }
    int ActiveMissionCount { get; }
    int ActiveMissionCap { get; }

    // Fleet section
    LlmShipSnapshot? PrimaryShip { get; }
    IReadOnlyList<LlmStoredShipSnapshot> StoredShips { get; }
    IReadOnlyList<LlmCrewSnapshot> Crew { get; }

    // Location section
    string CurrentStationName { get; }
    string StationFaction { get; }
    IReadOnlyList<string> StationFacilities { get; }
    string CurrentSystemName { get; }
    string CurrentSectorName { get; }
    int Quadrant { get; }
    IReadOnlyList<LlmSystemSnapshot> ConnectedSystems { get; }

    // Reputation + war
    IReadOnlyDictionary<string, int> Reputation { get; }
    IReadOnlyList<string> AtWar { get; }

    // Missions
    IReadOnlyList<string> ActiveStoryIds { get; }
    IReadOnlyList<string> ArchiveRecent { get; }
    int? CurrentBountyLevel { get; }
    int? CurrentPatrolLevel { get; }
    int? CurrentIndustryLevel { get; }

    // Story + waypoints + time
    IReadOnlyList<string> StoryArcsActive { get; }
    IReadOnlyList<LlmWaypointSnapshot> Waypoints { get; }
    double ElapsedSeconds { get; }
}
