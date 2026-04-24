using System;
using VGMissionJournal.Logging;

namespace VGAnima.MissionJournal;

/// <summary>Replacement for <see cref="VGAnima.Missions.MagnitudeScorer"/>
/// that works against VGMissionJournal's <see cref="MissionRecord"/>
/// instead of VGAnima's own <c>LlmMissionBlock</c>. Same 1-10 output
/// scale, same formula shape, different input source.
///
/// <para>Why this lives in VGAnima rather than VGMissionJournal:
/// "notability" is a narrative-reach concept specific to VGAnima's
/// broker-gossip model. VGMissionJournal stays observational.</para>
///
/// <para>Formula:
/// <list type="bullet">
///   <item>base = effective-level / 2 (1..~20 → 0..~10)</item>
///   <item>+1 multi-step</item>
///   <item>+1 combat (BountyMission, PatrolMission, or KillEnemies /
///         ProtectUnit objective present)</item>
///   <item>+2 defended-collect (combat AND gather objectives in the
///         same mission — rare, dramatic)</item>
///   <item>-2 abandoned, -1 failed (no success chatter)</item>
///   <item>clamped [1, 10]</item>
/// </list></para>
///
/// <para>Effective-level fallback chain: <see cref="MissionRecord.MissionLevel"/>
/// when populated, else <see cref="MissionRecord.PlayerLevel"/>, else
/// <paramref name="fallbackLevel"/>. Reason: VGMissionJournal 0.1.0
/// lists MissionLevel as a known gap (always 0); PlayerLevel at accept
/// time is the best available proxy.</para></summary>
internal static class MagnitudeDerivation
{
    /// <summary>Objective type strings that indicate combat — populated
    /// from VGMissionJournal's api.md objective-type list. Case-sensitive
    /// ordinal match per the contract.</summary>
    private const string ObjKillEnemies = "KillEnemies";
    private const string ObjProtectUnit = "ProtectUnit";

    /// <summary>Objective types that indicate gathering (mining / salvage
    /// / generic collection). Paired with a combat objective in the same
    /// mission, these mark a defended-collect shape.</summary>
    private const string ObjMining            = "Mining";
    private const string ObjSalvage           = "Salvage";
    private const string ObjCollectItemTypes  = "CollectItemTypes";

    /// <summary>Mission subclass strings that are categorically combat
    /// regardless of objective inspection. Fast-path shortcut before
    /// the objective scan.</summary>
    private const string SubclassBounty = "BountyMission";
    private const string SubclassPatrol = "PatrolMission";

    public static int Derive(MissionRecord record, int fallbackLevel = 0)
    {
        var level = record.MissionLevel > 0 ? record.MissionLevel
                  : record.PlayerLevel  > 0 ? record.PlayerLevel
                  : fallbackLevel;

        var score = level / 2;

        // Multi-step missions carry more narrative weight — "the
        // three-step Zoran job" is a bigger story than "killed
        // pirates somewhere."
        if ((record.Steps?.Count ?? 0) > 1) score += 1;

        var (isCombat, isGather) = ClassifyObjectives(record);
        if (isCombat) score += 1;
        if (isCombat && isGather) score += 2;  // defended-collect bonus on top

        // Outcome decay — abandoned/failed missions don't generate the
        // success-retelling that drives word-of-mouth propagation.
        if (record.Outcome == Outcome.Abandoned) score -= 2;
        else if (record.Outcome == Outcome.Failed) score -= 1;

        return Math.Max(1, Math.Min(10, score));
    }

    /// <summary>Returns (hasCombatObjective, hasGatherObjective).
    /// Subclass fast-path: Bounty/Patrol subclasses always imply
    /// combat. Beyond that, scan the objective tree.</summary>
    private static (bool isCombat, bool isGather) ClassifyObjectives(MissionRecord record)
    {
        var isCombat = record.MissionSubclass == SubclassBounty
                    || record.MissionSubclass == SubclassPatrol;
        var isGather = false;

        if (record.Steps is null) return (isCombat, isGather);

        foreach (var step in record.Steps)
        {
            if (step.Objectives is null) continue;
            foreach (var obj in step.Objectives)
            {
                switch (obj.Type)
                {
                    case ObjKillEnemies:
                    case ObjProtectUnit:
                        isCombat = true;
                        break;
                    case ObjMining:
                    case ObjSalvage:
                    case ObjCollectItemTypes:
                        isGather = true;
                        break;
                }
                if (isCombat && isGather) return (true, true);  // early exit
            }
        }
        return (isCombat, isGather);
    }
}
