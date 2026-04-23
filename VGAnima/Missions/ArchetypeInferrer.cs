using VGAnima.Llm;
using VGAnima.Persistence;

namespace VGAnima.Missions;

/// <summary>Derives a coarse archetype string from an
/// <see cref="LlmMissionBlock"/>'s intent mix. Pure function — no game
/// state, no factory side effects. Used at mission-resolution time by
/// <c>MissionLifecyclePatches</c> to tag a
/// <see cref="CompletedMissionRecord"/> so the journal context builder
/// can filter by archetype later ("last 3 salvage jobs at this station").
///
/// <para>Labels come from <see cref="MissionArchetypes"/>. Since v2 intents
/// are type-safe the mapping is a simple per-type switch — compare to v1
/// which had to reason across objective combinations. Mixed multi-step
/// missions collapse to the "heaviest" archetype present (priority order:
/// defended-collect > combat > salvage > gather > deliver).</para></summary>
internal static class ArchetypeInferrer
{
    public static string Infer(LlmMissionBlock block)
    {
        // Accumulate flags across all steps so multi-step missions pick
        // up every present archetype, then collapse via priority below.
        var hasCombat          = false;
        var hasOre             = false;
        var hasSalvage         = false;
        var hasDefendedOre     = false;
        var hasDefendedSalvage = false;
        var hasDeliver         = false;
        var hasHaul            = false;

        foreach (var step in block.Steps)
        {
            switch (step.Intent)
            {
                case ClearCombatSiteIntent:       hasCombat          = true; break;
                case GatherOreIntent:             hasOre             = true; break;
                case GatherSalvageIntent:         hasSalvage         = true; break;
                case DefendedGatherOreIntent:     hasDefendedOre     = true; break;
                case DefendedGatherSalvageIntent: hasDefendedSalvage = true; break;
                case DeliverToStationIntent:      hasDeliver         = true; break;
                case HaulGoodsIntent:             hasHaul            = true; break;
            }
        }

        // Defended variants are the hybrid combat+gather shape — tag them
        // accordingly so journal queries that ask for combat OR salvage
        // history still match. Cross-step combat+gather also counts as
        // defended-collect.
        if (hasDefendedOre || hasDefendedSalvage)             return MissionArchetypes.DefendedCollect;
        if (hasCombat && (hasOre || hasSalvage))              return MissionArchetypes.DefendedCollect;

        if (hasCombat)                                        return MissionArchetypes.Combat;
        if (hasSalvage)                                       return MissionArchetypes.Salvage;
        if (hasOre)                                           return MissionArchetypes.Gather;
        if (hasHaul || hasDeliver)                            return MissionArchetypes.Deliver;
        return MissionArchetypes.Other;
    }
}
