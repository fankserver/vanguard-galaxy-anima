using System;
using VGMissionJournal.Logging;

namespace VGAnima.MissionJournal;

/// <summary>Derives a 1..10 "notability" score for a resolved
/// <see cref="MissionRecord"/>. Feeds the journal's reach formula
/// (<see cref="VGAnima.Llm.MagnitudeReachFormula"/>) — higher-magnitude
/// events propagate further across the galaxy as broker gossip.
///
/// <para>Formula:
/// <list type="bullet">
///   <item>base = effective-level / 2 (1..~20 → 0..~10)</item>
///   <item>+1 multi-step</item>
///   <item>+1 combat (BountyMission / PatrolMission subclass, OR
///     a KillEnemies / ProtectUnit objective — both are combat-risk
///     shapes the player fought through)</item>
///   <item>-2 abandoned, -1 failed (no success chatter)</item>
///   <item>clamped [1, 10]</item>
/// </list></para>
///
/// <para>Effective-level fallback chain:
/// <see cref="MissionRecord.MissionLevel"/> when populated, else
/// <see cref="MissionRecord.PlayerLevel"/>, else
/// <paramref name="fallbackLevel"/>. Reason: VGMissionJournal 0.1.0
/// lists MissionLevel as a known gap (always 0); PlayerLevel at accept
/// time is the best available proxy.</para>
///
/// <para>Earlier versions of this formula stacked a "+2 defended-
/// collect" bonus when both combat and a gather objective were
/// present. That was a VGAnima invention — vanilla has no such
/// archetype, and the user called it out as such. Removed.</para></summary>
internal static class MagnitudeDerivation
{
    private const string ObjKillEnemies = "KillEnemies";
    private const string ObjProtectUnit = "ProtectUnit";

    private const string SubclassBounty = "BountyMission";
    private const string SubclassPatrol = "PatrolMission";

    public static int Derive(MissionRecord record, int fallbackLevel = 0)
    {
        var level = record.MissionLevel > 0 ? record.MissionLevel
                  : record.PlayerLevel  > 0 ? record.PlayerLevel
                  : fallbackLevel;

        var score = level / 2;

        if ((record.Steps?.Count ?? 0) > 1) score += 1;

        if (IsCombatShaped(record)) score += 1;

        if (record.Outcome == Outcome.Abandoned) score -= 2;
        else if (record.Outcome == Outcome.Failed) score -= 1;

        return Math.Max(1, Math.Min(10, score));
    }

    /// <summary>Combat-shaped means the player engaged or stood a
    /// defense — subclass fast-path for BountyMission / PatrolMission,
    /// or a KillEnemies / ProtectUnit objective anywhere in the step
    /// tree. Mining / salvage / trade / deliver archetypes are
    /// deliberately NOT combat-shaped: vanilla gather jobs run in
    /// peaceful POIs unless explicitly defended, and the defended case
    /// still surfaces via the ProtectUnit or KillEnemies scan.</summary>
    private static bool IsCombatShaped(MissionRecord record)
    {
        if (record.MissionSubclass == SubclassBounty
            || record.MissionSubclass == SubclassPatrol)
            return true;
        if (record.Steps is null) return false;
        foreach (var step in record.Steps)
        {
            if (step.Objectives is null) continue;
            foreach (var obj in step.Objectives)
            {
                if (obj.Type == ObjKillEnemies || obj.Type == ObjProtectUnit)
                    return true;
            }
        }
        return false;
    }
}
