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
    [JsonProperty("location")]     public LlmLocationSection Location     { get; set; } = null!;
    /// <summary>Unified per-faction snapshot: identifier → (display_name,
    /// relation, reputation). Replaces the earlier trio of <c>reputation</c> /
    /// <c>at_war</c> / <c>hostile_factions</c> fields. Keys are the stable
    /// identifiers (<see cref="Source.Galaxy.Faction"/>.identifier); use
    /// identifiers in mission block fields, the entry's display_name in
    /// dialogue text. <c>relation</c> matches vanilla
    /// <c>FactionData.IsEnemy</c>: hostile if <c>at_war</c> or rep &lt; -500,
    /// neutral for rep in [-500, 0], friendly for rep &gt; 0.</summary>
    [JsonProperty("factions")] public IReadOnlyDictionary<string, LlmFactionEntry> Factions { get; set; } = null!;
    /// <summary>Numeric clamps the LLM must respect for reward base_values.
    /// Mirrored in the system prompt as an IMPORTANT rule — repetition lowers
    /// the out-of-range rate compared to stating them only in the schema block.</summary>
    [JsonProperty("reward_clamps")] public LlmRewardClampsSection RewardClamps { get; set; } = null!;
    /// <summary>Pre-computed archetype recommendation for the mission. We do
    /// the signal aggregation (cargo inspection, specialization, titles, active
    /// missions, faction state, etc.) and hand the LLM a ranked weights dict
    /// plus a rationale. The LLM's job is to author dialogue that fits — not
    /// to synthesize the decision from scattered raw signals. See
    /// <see cref="MissionGuidanceBuilder"/> for the weighting rules.</summary>
    [JsonProperty("mission_guidance")] public LlmMissionGuidance MissionGuidance { get; set; } = null!;
    [JsonProperty("missions")]     public LlmMissionsSection Missions     { get; set; } = null!;
    /// <summary>Per-broker filtered view of the VGAnima journal — what THIS
    /// broker plausibly knows about the player's past broker work. Omitted
    /// entirely (null) when <c>Style.IncludePlayerJournal = false</c> or
    /// when the registry is empty. See <see cref="JournalContextBuilder"/>.</summary>
    [JsonProperty("journal", NullValueHandling = NullValueHandling.Ignore)]
    public LlmJournalSection? Journal { get; set; }
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
    [property: JsonProperty("cargo_used_pct")] int CargoUsedPct,
    // Hardpoint-derived loadout flags — the game's own HasLoadout check
    // (reads turrets + drone bay + torpedo bay, classified by the
    // GameplayType the mounted items declare). Authoritative "is this ship
    // equipped to do X right now" signal; honest even when the player has
    // mining tools mounted on a nominally-combat hull or vice versa.
    [property: JsonProperty("has_combat_loadout")]  bool HasCombatLoadout,
    [property: JsonProperty("has_mining_loadout")]  bool HasMiningLoadout,
    [property: JsonProperty("has_salvage_loadout")] bool HasSalvageLoadout);

internal sealed record LlmStoredShipSnapshot(
    [property: JsonProperty("name")]      string Name,
    [property: JsonProperty("level")]     int Level,
    [property: JsonProperty("faction")]   string Faction,
    [property: JsonProperty("role_hint")] string RoleHint);

internal sealed record LlmCrewSnapshot(
    [property: JsonProperty("name")]      string Name,
    [property: JsonProperty("role_hint")] string RoleHint);

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

internal sealed record LlmFactionEntry(
    [property: JsonProperty("display_name")] string DisplayName,
    [property: JsonProperty("relation")]     string Relation,
    [property: JsonProperty("reputation")]   int    Reputation);

internal sealed class LlmMissionGuidance
{
    /// <summary>Normalized weights per archetype; entries sum to 1.0 (or close,
    /// modulo rounding). Higher = stronger recommendation. Keys are the five
    /// archetype strings: "combat", "gather", "salvage", "deliver", "escort".
    /// Serialization preserves insertion order — ranked high-to-low.</summary>
    [JsonProperty("archetype_weights")] public IReadOnlyDictionary<string, double> ArchetypeWeights { get; set; } = null!;

    /// <summary>Archetypes the LLM MUST NOT pick — impossible given context.
    /// E.g. <c>combat</c> is forbidden when no faction is hostile.</summary>
    [JsonProperty("forbidden_archetypes")] public IReadOnlyList<string> ForbiddenArchetypes { get; set; } = null!;

    /// <summary>Short phrases explaining which signals drove the weighting.
    /// Not used by the LLM for decisions — but useful in logs + makes debugging
    /// miscalibrations easy ("why did it pick combat?").</summary>
    [JsonProperty("rationale")] public IReadOnlyList<string> Rationale { get; set; } = null!;
}

internal sealed class LlmRewardClampsSection
{
    [JsonProperty("credits_base_value_min")]    public int CreditsBaseValueMin    { get; set; }
    [JsonProperty("credits_base_value_max")]    public int CreditsBaseValueMax    { get; set; }
    [JsonProperty("experience_base_value_min")] public int ExperienceBaseValueMin { get; set; }
    [JsonProperty("experience_base_value_max")] public int ExperienceBaseValueMax { get; set; }
    [JsonProperty("reputation_amount_min")]     public int ReputationAmountMin    { get; set; }
    [JsonProperty("reputation_amount_max")]     public int ReputationAmountMax    { get; set; }
}

/// <summary>Broker-specific input to <see cref="ContextGatherer.Gather"/> that
/// doesn't belong on the game-state view (the view describes the player's
/// world, not the broker being voiced).</summary>
internal sealed record BrokerInfo(
    string Name,
    bool IsMale,
    string Seed,
    string StationFaction);

/// <summary>Per-broker journal view. Four windows built by
/// <see cref="JournalContextBuilder"/>: three for RESOLVED missions
/// (local / factional / notable) and one for IN-FLIGHT missions
/// (active). Each entry is a compact snapshot the LLM reads as narrative
/// anchors — "you've been salvaging here lately" / "I heard you're
/// already running a Corsair job, let me pitch something different."
///
/// <para>Why active is a peer window, not a substate: an offered or
/// accepted mission that hasn't resolved yet is load-bearing for
/// duplicate avoidance. Without it, two brokers at the same bar can
/// offer near-identical jobs because neither sees the other's
/// in-flight entry.</para></summary>
internal sealed class LlmJournalSection
{
    /// <summary>Resolved events at THIS station. Bar-gossip level of detail.</summary>
    [JsonProperty("local")]     public IReadOnlyList<LlmJournalEntry> Local     { get; set; } = null!;
    /// <summary>Resolved events involving the SAME faction elsewhere. Intel-
    /// network level; the broker heard through channels.</summary>
    [JsonProperty("factional")] public IReadOnlyList<LlmJournalEntry> Factional { get; set; } = null!;
    /// <summary>Resolved high-magnitude events regardless of location.
    /// Famous deeds travel everywhere.</summary>
    [JsonProperty("notable")]   public IReadOnlyList<LlmJournalEntry> Notable   { get; set; } = null!;
    /// <summary>IN-FLIGHT missions the broker can plausibly know about
    /// (same station OR same faction elsewhere). Outcome is always
    /// <c>"in_progress"</c>. Used by the prompt's non-duplication rule.</summary>
    [JsonProperty("active")]    public IReadOnlyList<LlmJournalEntry> Active    { get; set; } = null!;
}

internal sealed record LlmJournalEntry(
    [property: JsonProperty("storyId")]              string StoryId,
    [property: JsonProperty("mission_name")]         string MissionName,
    [property: JsonProperty("archetype")]            string Archetype,
    [property: JsonProperty("outcome")]              string Outcome,
    [property: JsonProperty("source_faction")]       string SourceFaction,
    [property: JsonProperty("station_name")]         string StationName,
    [property: JsonProperty("system_name")]          string SystemName,
    [property: JsonProperty("resolved_game_seconds")] double ResolvedGameSeconds,
    [property: JsonProperty("magnitude")]            int    Magnitude);
