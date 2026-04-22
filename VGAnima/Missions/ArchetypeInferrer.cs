using System.Linq;
using VGAnima.Llm;
using VGAnima.Persistence;

namespace VGAnima.Missions;

/// <summary>Derives a coarse archetype string from an
/// <see cref="LlmMissionBlock"/>'s objective mix. Pure function — no game
/// state, no factory side effects. Used at mission-resolution time by
/// <c>MissionLifecyclePatches</c> to tag a
/// <see cref="CompletedMissionRecord"/> so the journal context builder
/// can filter by archetype later ("last 3 salvage jobs at this station").
///
/// <para>The labels come from <see cref="MissionArchetypes"/>. Ordering
/// matters — a mission with both combat and gather objectives resolves to
/// <c>defended-collect</c> (hybrid), not <c>combat</c> or <c>gather</c>
/// alone. The journal reader can still match that entry when asking about
/// combat OR gather history via a lenient matcher.</para></summary>
internal static class ArchetypeInferrer
{
    public static string Infer(LlmMissionBlock block)
    {
        var objectives = block.Steps.SelectMany(s => s.Objectives).ToList();

        var hasClearPoi = objectives.Any(o => o is LlmClearPoi);
        var hasKill     = objectives.Any(o => o is LlmKillEnemies);
        var hasSalvage  = objectives.Any(o =>
            o is LlmCollectItemTypes c && c.ItemCategory == "Salvage");
        var hasOre      = objectives.Any(o =>
            o is LlmCollectItemTypes c && c.ItemCategory == "Ore");
        var hasGoods    = objectives.Any(o =>
            o is LlmCollectItemTypes c
            && (c.ItemCategory == "RefinedProduct" || c.ItemCategory == "TradeGoods"));
        var hasGuards   = objectives.Any(o =>
            o is LlmCollectItemTypes c && !string.IsNullOrEmpty(c.GuardsFaction));
        var hasTrigger  = objectives.Any(o => o is LlmTriggerObjective);
        var hasProtect  = objectives.Any(o => o is LlmProtectUnit);

        // Escort beats everything else — ProtectUnit is high-signal.
        if (hasProtect)
            return MissionArchetypes.Escort;

        // Hybrid: explicit guards_faction on a gather objective is the
        // "defended site" shape (vanilla SalvageWreck-on-Hard pattern).
        // Also catches combat-plus-gather split across steps.
        var combatPresent = hasClearPoi || hasKill;
        var gatherPresent = hasSalvage || hasOre;
        if ((combatPresent && gatherPresent) || hasGuards)
            return MissionArchetypes.DefendedCollect;

        if (combatPresent) return MissionArchetypes.Combat;
        if (hasSalvage)    return MissionArchetypes.Salvage;
        if (hasOre)        return MissionArchetypes.Gather;
        if (hasTrigger || hasGoods) return MissionArchetypes.Deliver;
        return MissionArchetypes.Other;
    }
}
