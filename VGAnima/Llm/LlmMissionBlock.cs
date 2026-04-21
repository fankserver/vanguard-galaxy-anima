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

internal sealed record LlmCollectItemTypes(
    string ItemCategory,
    int RequiredAmount,
    string Description) : LlmObjective;

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
