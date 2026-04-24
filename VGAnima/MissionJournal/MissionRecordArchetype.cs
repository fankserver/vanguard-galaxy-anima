using System.Collections.Generic;
using VGMissionJournal.Logging;

namespace VGAnima.MissionJournal;

/// <summary>Archetype inference for VGMissionJournal's
/// <see cref="MissionRecord"/> — counterpart to
/// <see cref="VGAnima.Missions.ArchetypeInferrer"/> which operates on
/// VGAnima's own <see cref="VGAnima.Llm.LlmMissionBlock"/> intents.
///
/// <para>Priority ladder (first match wins). Economic identity
/// (salvage / mining / trade) outranks role identity (escort /
/// combat) because the broker's lasting memory of the player is "a
/// salvager" before "a fighter who once salvaged." Drop-off travel is
/// lowest — it's the connective tissue of multi-step missions, not a
/// mission shape in itself.</para>
///
/// <para>Important subtlety: VGAnima's mission factory emits vanilla's
/// <c>Mining</c>-class objective for both ore and salvage (and trade
/// goods) missions, because that class has proper quantity semantics.
/// VGAnima-authored salvage therefore arrives here as <c>Type="Mining"
/// </c> with <c>itemCategory="Salvage"</c> in the Fields dictionary —
/// so we can't trust the class name alone; we must peek at
/// <c>itemCategory</c>. Vanilla's own salvage missions use the real
/// <c>Salvage</c> class.</para></summary>
internal static class MissionRecordArchetype
{
    // Objective type-name constants (case-sensitive ordinal per the
    // VGMissionJournal API contract).
    private const string TypeMining           = "Mining";
    private const string TypeSalvage          = "Salvage";
    private const string TypeCollectItemTypes = "CollectItemTypes";
    private const string TypeKillEnemies      = "KillEnemies";
    private const string TypeProtectUnit      = "ProtectUnit";
    private const string TypeTravelToPOI      = "TravelToPOI";

    // ItemCategory enum string values (read via Fields["itemCategory"]
    // which camelCases the backing enum → ToString()).
    private const string CatOre             = "Ore";
    private const string CatSalvage         = "Salvage";
    private const string CatRefinedProduct  = "RefinedProduct";
    private const string CatTradeGoods      = "TradeGoods";

    // Subclass fallbacks when the objective list is empty / inconclusive.
    private const string SubclassBounty   = "BountyMission";
    private const string SubclassPatrol   = "PatrolMission";
    private const string SubclassIndustry = "IndustryMission";

    public static string Infer(MissionRecord record)
    {
        var hasSalvage = false;
        var hasMining  = false;
        var hasTrade   = false;
        var hasCombat  = false;
        var hasEscort  = false;
        var hasTravel  = false;

        if (record.Steps is not null)
        {
            foreach (var step in record.Steps)
            {
                if (step.Objectives is null) continue;
                foreach (var obj in step.Objectives)
                {
                    switch (obj.Type)
                    {
                        case TypeSalvage:
                            hasSalvage = true;
                            break;
                        case TypeMining:
                            ClassifyMining(obj.Fields,
                                ref hasSalvage, ref hasMining, ref hasTrade);
                            break;
                        case TypeCollectItemTypes:
                            hasTrade  = true;
                            break;
                        case TypeKillEnemies:
                            hasCombat = true;
                            break;
                        case TypeProtectUnit:
                            hasEscort = true;
                            break;
                        case TypeTravelToPOI:
                            hasTravel = true;
                            break;
                    }
                }
            }
        }

        if (hasSalvage) return Persistence.MissionArchetypes.Salvage;
        if (hasMining)  return Persistence.MissionArchetypes.Mining;
        if (hasTrade)   return Persistence.MissionArchetypes.Trade;
        if (hasEscort)  return Persistence.MissionArchetypes.Escort;
        if (hasCombat)  return Persistence.MissionArchetypes.Combat;
        if (hasTravel)  return Persistence.MissionArchetypes.Deliver;

        // Subclass fallback for missions with no classifying objectives
        // (e.g. TriggerObjective-only missions like ClearAsteroidField).
        return record.MissionSubclass switch
        {
            SubclassBounty   => Persistence.MissionArchetypes.Combat,
            SubclassPatrol   => Persistence.MissionArchetypes.Combat,
            SubclassIndustry => Persistence.MissionArchetypes.Mining,
            _                => Persistence.MissionArchetypes.Other,
        };
    }

    /// <summary>Mining-class objective disambiguator via
    /// <c>itemCategory</c>. VGAnima's factory re-uses the Mining class
    /// for salvage and trade missions too; without this field peek
    /// every haul_goods / gather_salvage would misreport as mining.</summary>
    private static void ClassifyMining(
        IReadOnlyDictionary<string, object?>? fields,
        ref bool hasSalvage, ref bool hasMining, ref bool hasTrade)
    {
        if (fields is null
            || !fields.TryGetValue("itemCategory", out var catObj)
            || catObj is not string cat)
        {
            // No category visible → default to mining (class name
            // implies ore unless overridden).
            hasMining = true;
            return;
        }

        switch (cat)
        {
            case CatSalvage:        hasSalvage = true; break;
            case CatOre:            hasMining  = true; break;
            case CatTradeGoods:
            case CatRefinedProduct: hasTrade   = true; break;
            default:                hasMining  = true; break;
        }
    }

    public static string OutcomeString(Outcome? outcome) => outcome switch
    {
        Outcome.Completed => Persistence.CompletedMissionOutcomes.Completed,
        Outcome.Failed    => Persistence.CompletedMissionOutcomes.Failed,
        Outcome.Abandoned => Persistence.CompletedMissionOutcomes.Abandoned,
        _                 => Persistence.CompletedMissionOutcomes.InProgress,
    };

    public static double ResolvedGameSeconds(MissionRecord record) =>
        record.TerminalAtGameSeconds ?? 0.0;
}
