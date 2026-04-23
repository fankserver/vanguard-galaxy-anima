using System.Collections.Generic;
using System.Linq;
using Source.Galaxy;
using Source.Galaxy.POI;
using Source.Player;
using Source.Util;

namespace VGAnima.Llm;

/// <summary>Production <see cref="IGameStateView"/> impl. Reads from
/// <c>GamePlayer.current</c> + Unity singletons on the Unity main thread.
///
/// Every accessor is null-safe: if <c>GamePlayer.current</c> hasn't initialised
/// yet (e.g. first broker injection after pressing "new game" before the
/// player object lands), we return empty/zero defaults rather than throwing.
/// Callers (the orchestrator in Task 8) shouldn't even be gathering context in
/// that window — config gate checks come first — but safety-net defaults are
/// cheap insurance.
///
/// Faction names are serialised as the <c>Faction.identifier</c> game value
/// (e.g. "TradingGuild", "MiningGuild") — see decomp
/// <c>Source.Galaxy.Faction.identifier</c>.
///
/// Storyteller names come from <c>Storyteller.identifier</c> which is
/// <c>GetType().Name</c> (e.g. "Sandbox", "Conquest", "Economy", "Tutorial",
/// "Default", "Puppeteers").</summary>
internal sealed class GameStateView : IGameStateView
{
    // Cache the full list of known factions for reputation iteration. Faction.all
    // rebuilds a dictionary view per call — grab it once per gather via a property.
    private static IEnumerable<Source.Galaxy.Faction> AllFactions => Source.Galaxy.Faction.all;

    public int PlayerLevel =>
        GamePlayer.current?.level ?? 0;

    // PlayerCredits removed — see IGameStateView rationale.

    public string PlayerSpecialization
    {
        get
        {
            var p = GamePlayer.current;
            if (p == null) return string.Empty;
            // GamePlayer.starterSpecialization is stored as int, cast to the
            // enum for the string identifier.
            var spec = (Source.Crew.CommanderSpecialization)p.starterSpecialization;
            return spec.ToString();
        }
    }

    public int BountyRank   => GamePlayer.current?.bountyRank   ?? 0;
    public int PatrolRank   => GamePlayer.current?.patrolRank   ?? 0;
    public int IndustryRank => GamePlayer.current?.industryRank ?? 0;

    public int MaxBountyLevel   => GamePlayer.current?.maxBountyLevel   ?? 0;
    public int MaxPatrolLevel   => GamePlayer.current?.maxPatrolLevel   ?? 0;
    public int MaxIndustryLevel => GamePlayer.current?.maxIndustryLevel ?? 0;

    public IReadOnlyList<string> UnlockedTitles =>
        GamePlayer.current?.unlockedTitles.ToList() ?? new List<string>();

    public int ActiveMissionCount =>
        GamePlayer.current == null
            ? 0
            : GamePlayer.current.missions.Count
              + (GamePlayer.current.currentBounty   != null ? 1 : 0)
              + (GamePlayer.current.currentPatrol   != null ? 1 : 0)
              + (GamePlayer.current.currentIndustry != null ? 1 : 0);

    // GamePlayer.MissionLimit constant (20 as of 2026-04-20, decomp line 42).
    public int ActiveMissionCap => GamePlayer.MissionLimit;

    public LlmShipSnapshot? PrimaryShip
    {
        get
        {
            var ship = GamePlayer.current?.currentSpaceShip;
            if (ship == null) return null;
            // hull/shield/cargo intentionally NOT read — see LlmShipSnapshot.
            return new LlmShipSnapshot(
                Name:              ship.name ?? string.Empty,
                Faction:           ship.faction?.identifier ?? string.Empty,
                Level:             ship.level,
                HasCombatLoadout:  ship.HasLoadout(GameplayType.Combat),
                HasMiningLoadout:  ship.HasLoadout(GameplayType.Mining),
                HasSalvageLoadout: ship.HasLoadout(GameplayType.Salvage));
        }
    }

    public IReadOnlyList<LlmStoredShipSnapshot> StoredShips
    {
        get
        {
            var p = GamePlayer.current;
            if (p == null) return new List<LlmStoredShipSnapshot>();
            var list = new List<LlmStoredShipSnapshot>();
            foreach (var s in p.spaceShips)
            {
                if (s == p.currentSpaceShip) continue;  // skip primary — already in PrimaryShip
                list.Add(new LlmStoredShipSnapshot(
                    Name:     s.name ?? string.Empty,
                    Level:    s.level,
                    Faction:  s.faction?.identifier ?? string.Empty,
                    RoleHint: string.Empty));  // role inference deferred; empty is valid
            }
            return list;
        }
    }

    public IReadOnlyList<LlmCrewSnapshot> Crew
    {
        get
        {
            var p = GamePlayer.current;
            if (p == null) return new List<LlmCrewSnapshot>();
            var list = new List<LlmCrewSnapshot>();
            foreach (var c in p.crewMembers)
            {
                var display = BuildCrewDisplayName(c);
                list.Add(new LlmCrewSnapshot(
                    Name:     display,
                    RoleHint: c.profession.ToString()));
            }
            return list;
        }
    }

    private static string BuildCrewDisplayName(Source.Crew.CrewMemberData c)
    {
        // Name format matches spec §4 example: "Charlie 'Sniper' Churchill".
        var callsign = string.IsNullOrEmpty(c.callsign) ? null : $"'{c.callsign}'";
        var parts = new List<string>();
        if (!string.IsNullOrEmpty(c.firstName)) parts.Add(c.firstName);
        if (callsign != null) parts.Add(callsign);
        if (!string.IsNullOrEmpty(c.lastName))  parts.Add(c.lastName);
        return string.Join(" ", parts);
    }

    public string CurrentStationName
    {
        get
        {
            var station = SpaceStation.current;
            return station?.name ?? string.Empty;
        }
    }

    public string StationFaction
    {
        get
        {
            var station = SpaceStation.current;
            return station?.faction?.identifier ?? string.Empty;
        }
    }

    public IReadOnlyList<string> StationFacilities
    {
        get
        {
            var station = SpaceStation.current;
            if (station == null) return new List<string>();
            var list = new List<string>();
            if (station.bar               != null) list.Add("Bar");
            if (station.shipyard          != null) list.Add("Shipyard");
            if (station.refinery          != null) list.Add("Refinery");
            if (station.forge             != null) list.Add("Forge");
            if (station.missionBoard      != null) list.Add("MissionBoard");
            if (station.airlock           != null) list.Add("Airlock");
            if (station.personalHangar    != null) list.Add("PersonalHangar");
            if (station.salvageWorkshop   != null) list.Add("SalvageWorkshop");
            if (station.recruitmentCenter != null) list.Add("RecruitmentCenter");
            return list;
        }
    }

    public string CurrentSystemName => SystemMapData.current?.name ?? string.Empty;

    public string CurrentSectorName => SectorMapData.current?.name ?? string.Empty;

    public int Quadrant => SectorMapData.current?.quadrant ?? 0;

    public IReadOnlyList<LlmSystemSnapshot> ConnectedSystems
    {
        get
        {
            var sys = SystemMapData.current;
            if (sys == null) return new List<LlmSystemSnapshot>();
            var list = new List<LlmSystemSnapshot>();
            foreach (var adjacent in sys.GetAdjacentSystems())
            {
                if (adjacent == null) continue;
                // Derive a simple faction hint from whichever faction owns the
                // most POIs in the adjacent system, or null if none do.
                Source.Galaxy.Faction? dominant = null;
                var maxCount = 0;
                var counts = new Dictionary<Source.Galaxy.Faction, int>();
                foreach (var poi in adjacent.pointsOfInterest)
                {
                    if (poi.faction == null) continue;
                    counts.TryGetValue(poi.faction, out var existing);
                    counts[poi.faction] = existing + 1;
                }
                foreach (var kv in counts)
                {
                    if (kv.Value > maxCount)
                    {
                        maxCount = kv.Value;
                        dominant = kv.Key;
                    }
                }
                list.Add(new LlmSystemSnapshot(
                    Name:      adjacent.name ?? string.Empty,
                    Faction:   dominant?.identifier,
                    JumpsAway: 1));  // GetAdjacentSystems only returns direct jumpgate targets
            }
            return list;
        }
    }

    public IReadOnlyDictionary<string, int> Reputation
    {
        get
        {
            var p = GamePlayer.current;
            if (p == null) return new Dictionary<string, int>();
            var dict = new Dictionary<string, int>();
            foreach (var f in AllFactions)
            {
                if (f == Source.Galaxy.Faction.player) continue;
                // GetReputation(player, f) can't return its "unknown pair" sentinel
                // here — player and f are both non-null — so we record whatever it
                // gives us. Factions the player has never met still appear at their
                // default reputation, which is fine: the LLM gets the full landscape
                // and can see which factions matter and which don't.
                dict[f.identifier] = p.factionData.GetReputation(Source.Galaxy.Faction.player, f);
            }
            return dict;
        }
    }

    public IReadOnlyList<string> AtWar
    {
        get
        {
            var p = GamePlayer.current;
            if (p == null) return new List<string>();
            var list = new List<string>();
            foreach (var f in p.atWar) list.Add(f.identifier);
            return list;
        }
    }

    public IReadOnlyList<string> ActiveStoryIds
    {
        get
        {
            var p = GamePlayer.current;
            if (p == null) return new List<string>();
            var list = new List<string>();
            foreach (var m in p.missions)
                if (!string.IsNullOrEmpty(m.storyId)) list.Add(m.storyId);
            return list;
        }
    }

    public IReadOnlyList<string> ArchiveRecent
    {
        get
        {
            var p = GamePlayer.current;
            if (p == null) return new List<string>();
            // Snapshot-copy instead of aliasing: ContextGatherer's TakeLast may
            // short-circuit to return its input when it's already short enough,
            // which would leave the snapshot holding a live reference to
            // GamePlayer.missionsArchive. A mission archiving mid-serialization
            // would then mutate the "snapshot" underneath the JSON writer.
            return new List<string>(p.missionsArchive);
        }
    }

    public int? CurrentBountyLevel
    {
        get
        {
            var p = GamePlayer.current;
            return p?.currentBounty != null ? (int?)p.bountyRank : null;
        }
    }

    public int? CurrentPatrolLevel
    {
        get
        {
            var p = GamePlayer.current;
            return p?.currentPatrol != null ? (int?)p.patrolRank : null;
        }
    }

    public int? CurrentIndustryLevel
    {
        get
        {
            var p = GamePlayer.current;
            return p?.currentIndustry != null ? (int?)p.industryRank : null;
        }
    }

    public IReadOnlyList<string> StoryArcsActive
    {
        get
        {
            var p = GamePlayer.current;
            if (p == null) return new List<string>();
            var list = new List<string>();
            foreach (var st in p.storytellers) list.Add(st.identifier);
            return list;
        }
    }

    public IReadOnlyList<LlmWaypointSnapshot> Waypoints
    {
        get
        {
            var p = GamePlayer.current;
            if (p == null) return new List<LlmWaypointSnapshot>();
            var list = new List<LlmWaypointSnapshot>();
            var current = p.currentSystem;
            var i = 0;
            foreach (var wp in p.waypoints)
            {
                if (wp == null) { i++; continue; }
                var name = wp.name ?? string.Empty;
                var jumps = wp.system == current ? 0 : 1 + i;
                list.Add(new LlmWaypointSnapshot(PoiName: name, JumpsAway: jumps));
                i++;
            }
            return list;
        }
    }

    public double ElapsedSeconds => GamePlayer.current?.elapsedTime ?? 0.0;
}
