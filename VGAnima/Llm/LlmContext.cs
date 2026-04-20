using System.Collections.Generic;
using Newtonsoft.Json;

namespace VGAnima.Llm;

/// <summary>Snapshot types matching spec §4's JSON shape. Every collection
/// is <see cref="IReadOnlyList{T}"/> / <see cref="IReadOnlyDictionary{TKey,TValue}"/>
/// for immutability after <see cref="ContextGatherer.Gather"/> returns.
///
/// Serialized via <see cref="System.Text.Json"/> with explicit <c>snake_case</c>
/// property names so the JSON shape survives C# property casing. Field order in
/// the JSON is controlled by property-declaration order (STJ preserves it).</summary>
internal sealed class LlmContext
{
    [JsonProperty("player")]       public LlmPlayerSection   Player       { get; set; } = null!;
    [JsonProperty("fleet")]        public LlmFleetSection    Fleet        { get; set; } = null!;
    [JsonProperty("cargo_contents")] public IReadOnlyList<LlmCargoSnapshot> CargoContents { get; set; } = null!;
    [JsonProperty("location")]     public LlmLocationSection Location     { get; set; } = null!;
    [JsonProperty("reputation")]   public IReadOnlyDictionary<string, int> Reputation { get; set; } = null!;
    [JsonProperty("at_war")]       public IReadOnlyList<string> AtWar     { get; set; } = null!;
    [JsonProperty("missions")]     public LlmMissionsSection Missions     { get; set; } = null!;
    [JsonProperty("story_arcs_active")] public IReadOnlyList<string> StoryArcsActive { get; set; } = null!;
    [JsonProperty("waypoints")]    public IReadOnlyList<LlmWaypointSnapshot> Waypoints { get; set; } = null!;
    [JsonProperty("time")]         public LlmTimeSection     Time         { get; set; } = null!;
    [JsonProperty("broker")]       public LlmBrokerSection   Broker       { get; set; } = null!;
}

internal sealed class LlmPlayerSection
{
    [JsonProperty("level")]                 public int Level { get; set; }
    [JsonProperty("credits")]               public long Credits { get; set; }
    [JsonProperty("specialization")]        public string Specialization { get; set; } = string.Empty;
    [JsonProperty("bounty_rank")]           public int BountyRank { get; set; }
    [JsonProperty("patrol_rank")]           public int PatrolRank { get; set; }
    [JsonProperty("industry_rank")]         public int IndustryRank { get; set; }
    [JsonProperty("max_bounty_level")]      public int MaxBountyLevel { get; set; }
    [JsonProperty("max_patrol_level")]      public int MaxPatrolLevel { get; set; }
    [JsonProperty("max_industry_level")]    public int MaxIndustryLevel { get; set; }
    [JsonProperty("unlocked_titles")]       public IReadOnlyList<string> UnlockedTitles { get; set; } = null!;
    [JsonProperty("active_mission_count")]  public int ActiveMissionCount { get; set; }
    [JsonProperty("active_mission_cap")]    public int ActiveMissionCap { get; set; }
}

internal sealed class LlmFleetSection
{
    [JsonProperty("primary_ship")] public LlmShipSnapshot? PrimaryShip { get; set; }
    [JsonProperty("stored_ships")] public IReadOnlyList<LlmStoredShipSnapshot> StoredShips { get; set; } = null!;
    [JsonProperty("crew")]         public IReadOnlyList<LlmCrewSnapshot> Crew { get; set; } = null!;
}

internal sealed record LlmShipSnapshot(
    [property: JsonProperty("name")]          string Name,
    [property: JsonProperty("faction")]       string Faction,
    [property: JsonProperty("level")]         int Level,
    [property: JsonProperty("hull_pct")]      int HullPct,
    [property: JsonProperty("shield_pct")]    int ShieldPct,
    [property: JsonProperty("cargo_used_pct")] int CargoUsedPct);

internal sealed record LlmStoredShipSnapshot(
    [property: JsonProperty("name")]      string Name,
    [property: JsonProperty("level")]     int Level,
    [property: JsonProperty("faction")]   string Faction,
    [property: JsonProperty("role_hint")] string RoleHint);

internal sealed record LlmCrewSnapshot(
    [property: JsonProperty("name")]      string Name,
    [property: JsonProperty("role_hint")] string RoleHint);

internal sealed record LlmCargoSnapshot(
    [property: JsonProperty("item")]  string Item,
    [property: JsonProperty("count")] int Count);

internal sealed class LlmLocationSection
{
    [JsonProperty("current_station")]     public string CurrentStation { get; set; } = string.Empty;
    [JsonProperty("station_faction")]     public string StationFaction { get; set; } = string.Empty;
    [JsonProperty("station_facilities")]  public IReadOnlyList<string> StationFacilities { get; set; } = null!;
    [JsonProperty("current_system")]      public string CurrentSystem { get; set; } = string.Empty;
    [JsonProperty("current_sector")]      public string CurrentSector { get; set; } = string.Empty;
    [JsonProperty("quadrant")]            public int Quadrant { get; set; }
    [JsonProperty("connected_systems")]   public IReadOnlyList<LlmSystemSnapshot> ConnectedSystems { get; set; } = null!;
}

internal sealed record LlmSystemSnapshot(
    [property: JsonProperty("name")]       string Name,
    [property: JsonProperty("faction")]    string? Faction,
    [property: JsonProperty("jumps_away")] int JumpsAway);

internal sealed class LlmMissionsSection
{
    [JsonProperty("active_story_ids")]       public IReadOnlyList<string> ActiveStoryIds { get; set; } = null!;
    [JsonProperty("archive_recent")]         public IReadOnlyList<string> ArchiveRecent { get; set; } = null!;
    [JsonProperty("current_bounty_level")]   public int? CurrentBountyLevel { get; set; }
    [JsonProperty("current_patrol_level")]   public int? CurrentPatrolLevel { get; set; }
    [JsonProperty("current_industry_level")] public int? CurrentIndustryLevel { get; set; }
}

internal sealed record LlmWaypointSnapshot(
    [property: JsonProperty("poi_name")]   string PoiName,
    [property: JsonProperty("jumps_away")] int JumpsAway);

internal sealed class LlmTimeSection
{
    [JsonProperty("elapsed_seconds")] public double ElapsedSeconds { get; set; }
    [JsonProperty("day_of_year")]     public int DayOfYear { get; set; }
}

internal sealed class LlmBrokerSection
{
    [JsonProperty("name")]            public string Name { get; set; } = string.Empty;
    [JsonProperty("is_male")]         public bool IsMale { get; set; }
    [JsonProperty("seed")]            public string Seed { get; set; } = string.Empty;
    [JsonProperty("station_faction")] public string StationFaction { get; set; } = string.Empty;
}

/// <summary>Broker-specific input to <see cref="ContextGatherer.Gather"/> that
/// doesn't belong on the game-state view (the view describes the player's
/// world, not the broker being voiced).</summary>
internal sealed record BrokerInfo(
    string Name,
    bool IsMale,
    string Seed,
    string StationFaction);
