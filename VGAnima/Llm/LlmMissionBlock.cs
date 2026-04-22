using System.Collections.Generic;

namespace VGAnima.Llm;

/// <summary>Validated mission block (spec §2). Produced by
/// <see cref="MissionBlockValidator.Parse"/>; consumed by
/// <see cref="VGAnima.Missions.MissionFactoryFromJson"/>.
///
/// Everything is immutable — downstream code treats it as read-only
/// blueprint data.</summary>
internal sealed record LlmMissionBlock(
    string Name,
    string Description,
    string CompletionText,
    string SourceFaction,
    IReadOnlyList<LlmMissionStep> Steps,
    IReadOnlyList<LlmReward> Rewards);

internal sealed record LlmMissionStep(
    IReadOnlyList<LlmObjective> Objectives);

/// <summary>Base type for validated objectives. Instances are one of the
/// concrete subtypes below — callers pattern-match by type.</summary>
internal abstract record LlmObjective;

internal sealed record LlmKillEnemies(
    string EnemyFaction,
    int RequiredAmount,
    string Description) : LlmObjective;

internal sealed record LlmProtectUnit(
    string ProtectText) : LlmObjective;

internal sealed record LlmTriggerObjective(
    string Trigger,
    int RequiredAmount,
    string Description) : LlmObjective;

/// <summary>"Bring me N types of $category items." Factory spawns a
/// resource POI (Ore → asteroid field, Salvage → derelict fleet) and
/// pins it to the step. RefinedProduct / TradeGoods don't spawn a POI.
///
/// <para><see cref="GuardsFaction"/> is optional. When set on an Ore or
/// Salvage objective, the factory calls <c>poi.AddGuards(...)</c> on the
/// spawned POI with combat units of that faction — mirroring vanilla
/// <c>SalvageWreck.GenerateMission</c> on Hard+ difficulty
/// (Source.MissionSystem.Generator/SalvageWreck.cs:80) and vanilla's
/// <c>AddMiningPoi(pirateChance: true)</c> for defended mining fields.
/// One POI, one step, one Locate target — player fights the defenders
/// AND loots the site, same place. Avoids the "two objectives fighting
/// over dynamicPointOfInterest" pattern that orphaned POIs in v1.
/// Not valid on RefinedProduct / TradeGoods (no POI to attach guards to).</para></summary>
internal sealed record LlmCollectItemTypes(
    string ItemCategory,
    int RequiredAmount,
    string Description,
    string? GuardsFaction = null) : LlmObjective;

/// <summary>"Go to a POI and clear it of enemies." The factory spawns a
/// fresh <c>Combat</c> POI in the broker-station's system (mirrors vanilla
/// <c>BountyHunt</c>), pins it to the containing <c>MissionStep</c>, and
/// returns a <c>KillEnemies</c> objective whose <c>requiredAmount</c> is
/// the spawn's <c>totalUnitCount</c>. The LLM does NOT specify the
/// required_amount — it's derived from the spawn.</summary>
internal sealed record LlmClearPoi(
    string EnemyFaction,
    string Description) : LlmObjective;

internal abstract record LlmReward;

internal sealed record LlmCreditsReward(int BaseValue) : LlmReward;

internal sealed record LlmExperienceReward(int BaseValue) : LlmReward;

internal sealed record LlmReputationReward(string Faction, int Amount) : LlmReward;

/// <summary>Item reward — the broker hands over a tangible item on
/// completion. v1 supports three item kinds, all anchored to the broker
/// station's system at mission-build time:
///   - <c>MiningClaim</c> — procedural asteroid field claim (cashable
///     OR equippable by prospector-oriented players).
///   - <c>SalvageClaim</c> — procedural derelict-field claim.
///   - <c>MaterialMiningClaim</c> — mining claim weighted toward a specific
///     refined material; richer narrative flavor.
/// Constructed via <c>ItemBuilder.Get(kind).CreateX(brokerStation.system)</c>
/// at mission-build time. Factory emits vanilla's
/// <c>Source.MissionSystem.Rewards.Item</c> which on completion calls
/// <c>AddCargo(item, amount, force: true)</c> — no validator / inventory
/// limits to worry about.</summary>
internal sealed record LlmItemReward(string Kind) : LlmReward;
