using VGAnima.Llm;
using VGAnima.Persistence;

namespace VGAnima.Missions;

/// <summary>Derives a canonical archetype string from an
/// <see cref="LlmMissionBlock"/>'s intent mix. Pure function; mirrors
/// <see cref="VGAnima.MissionJournal.MissionRecordArchetype"/> which
/// does the same job against VGMissionJournal's observed records, so
/// an in-flight VGAnima offer and its resolved VGMissionJournal twin
/// surface the same archetype to the LLM.
///
/// <para>Priority ladder (first match wins). Economic identity
/// outranks role identity — a broker remembers the player as "a
/// salvager who also fought" before "a fighter who salvaged":
/// <list type="number">
///   <item><c>salvage</c> — any GatherSalvage / DefendedGatherSalvage
///     intent.</item>
///   <item><c>mining</c> — any GatherOre / DefendedGatherOre intent.</item>
///   <item><c>trade</c> — HaulGoods (commodity turn-in shape).</item>
///   <item><c>combat</c> — ClearCombatSite without any economic
///     intent above.</item>
///   <item><c>deliver</c> — DeliverToStation without any qualifying
///     intent above.</item>
///   <item><c>other</c> — unknown intent mix.</item>
/// </list>
/// Note: VGAnima's LLM whitelist has no ProtectUnit intent, so
/// <c>escort</c> never applies here — it only surfaces through
/// <see cref="VGAnima.MissionJournal.MissionRecordArchetype"/> for
/// vanilla Escort / HelpMiner missions observed via
/// VGMissionJournal.</para></summary>
internal static class ArchetypeInferrer
{
    public static string Infer(LlmMissionBlock block)
    {
        var hasSalvage  = false;
        var hasMining   = false;
        var hasTrade    = false;
        var hasCombat   = false;
        var hasDeliver  = false;

        foreach (var step in block.Steps)
        {
            switch (step.Intent)
            {
                case GatherSalvageIntent:
                case DefendedGatherSalvageIntent:
                    hasSalvage = true; break;
                case GatherOreIntent:
                case DefendedGatherOreIntent:
                    hasMining  = true; break;
                case HaulGoodsIntent:
                    hasTrade   = true; break;
                case ClearCombatSiteIntent:
                    hasCombat  = true; break;
                case DeliverToStationIntent:
                    hasDeliver = true; break;
            }
        }

        if (hasSalvage) return MissionArchetypes.Salvage;
        if (hasMining)  return MissionArchetypes.Mining;
        if (hasTrade)   return MissionArchetypes.Trade;
        if (hasCombat)  return MissionArchetypes.Combat;
        if (hasDeliver) return MissionArchetypes.Deliver;
        return MissionArchetypes.Other;
    }
}
