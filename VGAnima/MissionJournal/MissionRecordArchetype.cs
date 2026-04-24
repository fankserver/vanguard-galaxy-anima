using VGMissionJournal.Logging;

namespace VGAnima.MissionJournal;

/// <summary>Archetype inference for VGMissionJournal's
/// <see cref="MissionRecord"/> — counterpart to
/// <see cref="VGAnima.Missions.ArchetypeInferrer"/> which operates on
/// VGAnima's own <c>LlmMissionBlock</c>.
///
/// <para>Combat/gather detection mirrors
/// <see cref="MagnitudeDerivation"/>'s classifier; archetype adds
/// salvage vs mining split, deliver/escort, and defended-collect
/// stacking. Output strings are VGAnima's canonical
/// <see cref="VGAnima.Persistence.MissionArchetypes"/> constants so the
/// LLM context JSON looks identical whether a record originated in
/// VGAnima or VGMissionJournal.</para></summary>
internal static class MissionRecordArchetype
{
    public static string Infer(MissionRecord record)
    {
        var hasCombat   = record.MissionSubclass == "BountyMission"
                       || record.MissionSubclass == "PatrolMission";
        var hasMining   = false;
        var hasSalvage  = false;
        var hasCollect  = false;
        var hasTravel   = false;
        var hasEscort   = false;

        if (record.Steps is not null)
        {
            foreach (var step in record.Steps)
            {
                if (step.Objectives is null) continue;
                foreach (var obj in step.Objectives)
                {
                    switch (obj.Type)
                    {
                        case "KillEnemies":  hasCombat  = true; break;
                        case "ProtectUnit":  hasEscort  = true; hasCombat = true; break;
                        case "Mining":       hasMining  = true; break;
                        case "Salvage":      hasSalvage = true; break;
                        case "CollectItemTypes": hasCollect = true; break;
                        case "TravelToPOI":  hasTravel  = true; break;
                    }
                }
            }
        }

        var hasGather = hasMining || hasCollect;

        if (hasCombat && (hasSalvage || hasGather))
            return Persistence.MissionArchetypes.DefendedCollect;
        if (hasCombat && hasEscort)
            return Persistence.MissionArchetypes.Escort;
        if (hasCombat)
            return Persistence.MissionArchetypes.Combat;
        if (hasSalvage)
            return Persistence.MissionArchetypes.Salvage;
        if (hasMining)
            return Persistence.MissionArchetypes.Gather;
        if (hasCollect || hasTravel)
            return Persistence.MissionArchetypes.Deliver;
        return Persistence.MissionArchetypes.Other;
    }

    public static string OutcomeString(Outcome? outcome) => outcome switch
    {
        Outcome.Completed => Persistence.CompletedMissionOutcomes.Completed,
        Outcome.Failed    => Persistence.CompletedMissionOutcomes.Failed,
        Outcome.Abandoned => Persistence.CompletedMissionOutcomes.Abandoned,
        _                 => Persistence.CompletedMissionOutcomes.InProgress,
    };

    /// <summary>Timestamp of the terminal timeline entry in game-seconds,
    /// or 0 when the mission is still active. Used as the record's
    /// "resolved_at" for the journal snapshot.</summary>
    public static double ResolvedGameSeconds(MissionRecord record) =>
        record.TerminalAtGameSeconds ?? 0.0;
}
