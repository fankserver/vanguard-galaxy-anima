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
    /// <summary>Live snapshot of other patrons at this bar — vanilla
    /// salesmen (Prospector, Salvage Scout, Equipment Rep, etc.) and crew
    /// recruiters. VGAnima-injected brokers are filtered out. Lets the
    /// broker reference the rest of the room organically ("see that
    /// Prospector over there?"). Omitted when empty. See
    /// <see cref="BarEcosystemBuilder"/>.</summary>
    [JsonProperty("bar_ecosystem", NullValueHandling = NullValueHandling.Ignore)]
    public LlmBarEcosystemSection? BarEcosystem { get; set; }
    /// <summary>Lifetime tallies of the player's bar-salesman purchases,
    /// written by <see cref="VGAnima.Patches.BarPurchasePatches"/> and
    /// read by <see cref="PurchaseProfileBuilder"/>. Omitted when the
    /// player has never bought anything. Feeds the "offer rewards that
    /// match player taste" loop.</summary>
    [JsonProperty("purchase_profile", NullValueHandling = NullValueHandling.Ignore)]
    public LlmPurchaseProfileSection? PurchaseProfile { get; set; }
    /// <summary>Stations the LLM may pick for intents that need a target
    /// (<c>deliver_to_station</c> / <c>haul_goods</c>). Built by
    /// <see cref="AccessibleDestinationsBuilder"/>: 0-1 jumpgate hops from
    /// the broker's system, capped at 8, ranked by (jumps asc,
    /// same-faction-as-broker desc, name asc). LLM references by short
    /// <c>dest_N</c> id; the real station GUID rides along (JsonIgnore) for
    /// the factory to feed into <c>TravelToPOI.targetPOI</c>. Omitted
    /// from the JSON when no destinations are reachable (pocket system).</summary>
    [JsonProperty("accessible_destinations", NullValueHandling = NullValueHandling.Ignore)]
    public IReadOnlyList<AccessibleDestination>? AccessibleDestinations { get; set; }

    /// <summary>Systems where the player has accumulated enough visits
    /// (<see cref="RegionallyKnownBuilder.MinVisitsThreshold"/>) that a
    /// broker there can plausibly recognize their face. Optional
    /// <c>recent_activity</c> carries the archetype of their most recent
    /// completed mission in that system, gated on a
    /// <see cref="RegionallyKnownBuilder.StaleActivityDaysThreshold"/>
    /// freshness window — tells the broker HOW to frame the recognition
    /// ("you've been salvaging around Zoran, yeah?"). Null
    /// <c>recent_activity</c> = face-only ("seen you around"). Omitted
    /// from JSON when empty. See
    /// <see cref="RegionallyKnownBuilder"/>.</summary>
    [JsonProperty("regionally_known", NullValueHandling = NullValueHandling.Ignore)]
    public IReadOnlyList<LlmRegionallyKnownEntry>? RegionallyKnown { get; set; }

    [JsonProperty("story_arcs_active")] public IReadOnlyList<string> StoryArcsActive { get; set; } = null!;
    [JsonProperty("waypoints")]    public IReadOnlyList<LlmWaypointSnapshot> Waypoints { get; set; } = null!;
    [JsonProperty("time")]         public LlmTimeSection     Time         { get; set; } = null!;
    [JsonProperty("broker")]       public LlmBrokerSection   Broker       { get; set; } = null!;
}

internal sealed class LlmPlayerSection
{
    [JsonProperty("level")]                 public int Level { get; set; }
    // `credits` (bank balance) is NOT exposed — a broker NPC can't see a
    // wallet any more than they can see into a cargo hold. The historical
    // note in MissionGuidanceBuilder used "bank balance" as its explicit
    // analogy when removing cargo-item classification; keeping the credit
    // field here would have contradicted that same principle.
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
    // Hull + shield % survive — visibly beat-up hulls are observable on
    // the dock (a broker can tell a battered pilot from a pristine one).
    [property: JsonProperty("hull_pct")]      int HullPct,
    [property: JsonProperty("shield_pct")]    int ShieldPct,
    // `cargo_used_pct` is NOT exposed — how full your hold is is
    // internal state the broker has no line of sight to. Removed
    // 2026-04-23 along with player.credits on the same narrative-
    // honesty principle.
    // Hardpoint-derived loadout flags — the game's own HasLoadout check
    // (reads turrets + drone bay + torpedo bay, classified by the
    // GameplayType the mounted items declare). Externally observable:
    // mounted turrets and tools are visible on a parked ship.
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
    /// <summary>Single-word atmosphere tag derived by
    /// <see cref="StationConditionInferrer"/>. Feeds the prompt's
    /// linguistic-register rule. Values: <c>war-torn</c>, <c>peaceful</c>,
    /// <c>bustling</c>, <c>frontier</c>, <c>normal</c>.</summary>
    [JsonProperty("station_condition")]   public string StationCondition { get; set; } = "normal";
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
/// (local / network / rumors) and one for IN-FLIGHT missions (active).
/// Each entry is a compact snapshot the LLM reads as narrative anchors
/// — "you've been salvaging here lately" / "I heard you're already
/// running a Corsair job, let me pitch something different."
///
/// <para>Reach model: each resolved entry passes a reach gate that
/// combines distance (jumps_away), age, magnitude, faction alignment,
/// and player fame. Gossip always travels to adjacent systems; far
/// systems only hear big events, old events, or events involving
/// famous players. See <c>MagnitudeReachFormula</c>.</para>
///
/// <para>Why active is a peer window, not a substate: an offered or
/// accepted mission that hasn't resolved yet is load-bearing for
/// duplicate avoidance. Without it, two brokers at the same bar can
/// offer near-identical jobs because neither sees the other's
/// in-flight entry.</para></summary>
internal sealed class LlmJournalSection
{
    /// <summary>Resolved events at THIS station. Bar-gossip level of
    /// detail; always visible, you were here when it happened.</summary>
    [JsonProperty("local")]   public IReadOnlyList<LlmJournalEntry> Local   { get; set; } = null!;
    /// <summary>Resolved events within reach via the faction's internal
    /// network — same <c>source_faction</c> as the broker, close enough
    /// to have filtered through channels. Renamed from <c>factional</c>
    /// in JC-T6; the new name tracks the reach-formula meaning rather
    /// than the bare faction filter that preceded it.</summary>
    [JsonProperty("network")] public IReadOnlyList<LlmJournalEntry> Network { get; set; } = null!;
    /// <summary>Resolved events that reached the broker despite being
    /// out-of-network — either different faction, or the same faction
    /// far enough away that only the magnitude pushed them through.
    /// Distant hearsay register: acknowledge the distance when the
    /// broker references these ("heard out of Zoran, way on the edge").
    /// Renamed from <c>notable</c> in JC-T6.</summary>
    [JsonProperty("rumors")]  public IReadOnlyList<LlmJournalEntry> Rumors  { get; set; } = null!;
    /// <summary>IN-FLIGHT missions the broker can plausibly know about
    /// (same station OR same faction elsewhere). Outcome is always
    /// <c>"in_progress"</c>. Used by the prompt's non-duplication rule.</summary>
    [JsonProperty("active")]  public IReadOnlyList<LlmJournalEntry> Active  { get; set; } = null!;
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
    [property: JsonProperty("magnitude")]            int    Magnitude,
    // Jumps from the record's resolved station to the broker's current
    // system, computed at context-build time by
    // <see cref="JournalContextBuilder"/>. 0 for <c>local</c> entries;
    // positive for network/rumors; lets the LLM voice the narrative
    // distance ("way over in X" vs "couple jumps from here"). Default
    // 0 keeps the field backwards-compatible with fixtures that don't
    // set it.
    [property: JsonProperty("jumps_from_here")]      int    JumpsFromHere = 0);

/// <summary>One system where the player is a regular — enough visits
/// accumulated that the broker can plausibly recognize their face.
/// <see cref="RecentActivity"/>, when present, elevates the recognition
/// from "seen you around" to "know what you've been up to" (the
/// broker frames their pitch against the player's recent behavior).
/// Omitted when the most-recent mission in this system is older than
/// <see cref="RegionallyKnownBuilder.StaleActivityDaysThreshold"/>
/// game-days — stale activity is no longer load-bearing signal.</summary>
internal sealed record LlmRegionallyKnownEntry(
    [property: JsonProperty("system")]               string  System,
    [property: JsonProperty("visits")]               int     Visits,
    [property: JsonProperty("last_visit_days_ago")]  int     LastVisitDaysAgo,
    [property: JsonProperty("recent_activity", NullValueHandling = NullValueHandling.Ignore)]
    string? RecentActivity);

/// <summary>Live snapshot of the bar's other patrons — what else is
/// "for sale" at this station right now, so the broker can reference
/// the room.</summary>
internal sealed class LlmBarEcosystemSection
{
    [JsonProperty("other_salesmen_here")]
    public IReadOnlyList<LlmBarSalesmanEntry> OtherSalesmenHere { get; set; } = null!;
}

/// <summary>Counts of each canonical purchase type the player has made
/// across their whole playthrough. Lifetime figures; fully zeroed out
/// profiles are not emitted (builder returns null — saves context tokens).
///
/// <para>Two distinct sources:
/// <list type="bullet">
///   <item><b>Bar salesmen</b> (claims / png / equipment pieces) — high-
///     signal narrative investment ("bought a salvage claim" = intent to
///     work that claim).</item>
///   <item><b>Station commodity shops</b> (mining / salvage / general /
///     other) — lower-signal participation in that shop's trade loop
///     ("shops at Salvage Shop often" = active in salvage trading).</item>
/// </list>
/// Keeping the two groups separate lets the LLM weight them differently.</para></summary>
internal sealed class LlmPurchaseProfileSection
{
    // Bar salesmen — written by BarPurchasePatches.
    [JsonProperty("mining_claims_bought")]  public int MiningClaimsBought  { get; set; }
    [JsonProperty("salvage_claims_bought")] public int SalvageClaimsBought { get; set; }
    [JsonProperty("space_ship_png_bought")] public int SpaceShipPngBought  { get; set; }
    [JsonProperty("equipment_bought")]      public int EquipmentBought     { get; set; }

    // Station commodity shops — written by ShopPurchasePatches.
    [JsonProperty("mining_shop_buys")]   public int MiningShopBuys   { get; set; }
    [JsonProperty("salvage_shop_buys")]  public int SalvageShopBuys  { get; set; }
    [JsonProperty("general_shop_buys")]  public int GeneralShopBuys  { get; set; }
    [JsonProperty("other_shop_buys")]    public int OtherShopBuys    { get; set; }
}

internal sealed record LlmBarSalesmanEntry(
    // One of: Prospector / Salvage Scout / Slick Entrepreneur /
    // Equipment Rep / Crew Recruiter. Source of truth:
    // BarEcosystemBuilder.Kinds.
    [property: JsonProperty("kind")]             string  Kind,
    [property: JsonProperty("name")]             string  Name,
    // Canonical item identifier (MiningClaim / SalvageClaim / SpaceShipPng /
    // equipment-builder id). Null for Crew Recruiter or unresolvable.
    [property: JsonProperty("item_identifier",   NullValueHandling = NullValueHandling.Ignore)]
    string? ItemIdentifier,
    // Only Equipment Reps carry a faction signal (via their item's
    // manufacturer). Null for every other kind and for generic brands.
    [property: JsonProperty("faction",           NullValueHandling = NullValueHandling.Ignore)]
    string? Faction);
