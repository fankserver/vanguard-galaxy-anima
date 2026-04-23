using System.Collections.Generic;

namespace VGAnima.Llm;

/// <summary>Validated mission block. Produced by
/// <see cref="MissionBlockValidator.Parse"/>, consumed by
/// <see cref="VGAnima.Missions.MissionFactoryFromJson"/>.
///
/// <para><b>v2 design principle: LLM authors narrative, plugin owns
/// mechanics.</b> The LLM picks ONE <see cref="LlmIntent"/> per step from
/// a closed whitelist. The plugin owns every mechanical translation —
/// which vanilla <c>MissionObjective</c> classes get instantiated, which
/// POIs get spawned, which triggers fire. Intents can't be emitted
/// without their required parameters, so the v1 bug class where an
/// LLM-authored <c>KillEnemies</c> shipped with no POI is structurally
/// impossible.</para>
///
/// <para>Everything is immutable — downstream code treats the block as
/// read-only blueprint data.</para></summary>
internal sealed record LlmMissionBlock(
    string Name,
    string Description,
    string CompletionText,
    string SourceFaction,
    IReadOnlyList<LlmMissionStep> Steps,
    IReadOnlyList<LlmReward> Rewards);

/// <summary>One step = one narrative beat = one Locate waypoint. Wraps
/// exactly ONE <see cref="LlmIntent"/>; NOT a list of objectives. The
/// plugin may expand a single intent into multiple vanilla objectives
/// internally (e.g. <c>haul_goods</c> emits both
/// <c>CollectItemTypes(TradeGoods)</c> and <c>TravelToPOI</c>) — that's
/// an implementation detail invisible to the LLM.</summary>
internal sealed record LlmMissionStep(LlmIntent Intent);

/// <summary>Base type for validated intents. One concrete subtype per
/// narrative shape. Factory pattern-matches to build the mechanical
/// step.</summary>
internal abstract record LlmIntent(string Description);

/// <summary>"Fight at a specific place." Plugin spawns a Combat POI with
/// hostile guards of <paramref name="EnemyFaction"/> in the broker
/// station's system, pins it to the step, and emits a <c>KillEnemies</c>
/// objective whose <c>requiredAmount</c> = the spawn's
/// <c>totalUnitCount</c>. Mirrors vanilla <c>BountyHunt.GenerateMission</c>.
///
/// <para><see cref="Flavor"/> (optional) selects one of a small closed
/// list of narrative shapes (<see cref="CombatFlavorWhitelist"/>). The
/// plugin owns the concrete ship composition per flavor — initial
/// spawn + reinforcement timing — and the LLM picks whichever flavor
/// matches its pitch ("we spotted scouts" → scouting, "they're dug
/// in" → outpost, "hidden lair" → lair). Null falls back to the
/// balanced default composition.</para></summary>
internal sealed record ClearCombatSiteIntent(
    string EnemyFaction,
    string Description,
    string? Flavor = null) : LlmIntent(Description);

/// <summary>"Go mine at an asteroid field." Plugin spawns a Mining POI
/// (via <c>AddMiningPoi</c>) in the broker's system, pins it to the
/// step, and emits a quantity-counting <c>Mining</c> objective for
/// <see cref="RequiredAmount"/> units of <c>Ore</c>.</summary>
internal sealed record GatherOreIntent(
    int RequiredAmount,
    string Description) : LlmIntent(Description);

/// <summary>"Go scavenge a derelict." Plugin spawns a Salvage POI (via
/// <c>AddDerelictFleetPoi</c>) in the broker's system, pins it to the
/// step, and emits a quantity-counting <c>Mining</c> objective for
/// <see cref="RequiredAmount"/> units of <c>Salvage</c>.</summary>
internal sealed record GatherSalvageIntent(
    int RequiredAmount,
    string Description) : LlmIntent(Description);

/// <summary>"Fight and loot the same place (ore)." Plugin spawns a
/// Mining POI with hostile guards attached (one POI, defenders inside,
/// mirrors vanilla's defended-mining pattern). Emits
/// <c>Mining(Ore, RequiredAmount)</c> — the objective completes on
/// collection; clearing the guards is implicit because you can't mine
/// under fire.</summary>
internal sealed record DefendedGatherOreIntent(
    int RequiredAmount,
    string GuardsFaction,
    string Description) : LlmIntent(Description);

/// <summary>Salvage counterpart to <see cref="DefendedGatherOreIntent"/>.
/// Mirrors vanilla <c>SalvageWreck.GenerateMission</c> on Hard+.</summary>
internal sealed record DefendedGatherSalvageIntent(
    int RequiredAmount,
    string GuardsFaction,
    string Description) : LlmIntent(Description);

/// <summary>"Go dock at a specific station." Plugin emits a
/// <c>TravelToPOI</c> objective pointing at the destination's station
/// GUID. Completes when the player docks there (vanilla tracks
/// <c>lastVisitedTime</c> on the POI).</summary>
internal sealed record DeliverToStationIntent(
    string DestinationShortId,
    string Description) : LlmIntent(Description);

/// <summary>"Haul N trade goods to a station." Plugin emits TWO vanilla
/// objectives in one step: quantity-counting
/// <c>Mining(TradeGoods, RequiredAmount)</c> + <c>TravelToPOI(destination)</c>.
/// Both must complete — player must have the goods AND dock at the
/// destination.</summary>
internal sealed record HaulGoodsIntent(
    int RequiredAmount,
    string DestinationShortId,
    string Description) : LlmIntent(Description);

/// <summary>Reward blocks — untouched from v1. Credits / Experience /
/// Reputation / Item all still map 1:1 to vanilla reward classes.</summary>
internal abstract record LlmReward;

internal sealed record LlmCreditsReward(int BaseValue) : LlmReward;

internal sealed record LlmExperienceReward(int BaseValue) : LlmReward;

internal sealed record LlmReputationReward(string Faction, int Amount) : LlmReward;

/// <summary>Item reward — the broker hands over a tangible item on
/// completion. Three kinds, all anchored to the broker station's system:
/// MiningClaim, SalvageClaim, MaterialMiningClaim. Factory emits vanilla's
/// <c>Source.MissionSystem.Rewards.Item</c>.</summary>
internal sealed record LlmItemReward(string Kind) : LlmReward;
