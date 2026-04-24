using System;
using System.Collections.Generic;
using VGMissionJournal.Logging;

namespace VGAnima.MissionJournal;

/// <summary>Objective-tag extraction for VGMissionJournal's
/// <see cref="MissionRecord"/>. Returns a multi-label list of canonical
/// tags drawn from vanilla's own objective vocabulary — no invented
/// archetype aggregates, no priority ladder, no forced single-label
/// collapse. A defended-salvage mission surfaces as
/// <c>[collect_salvage, kill_enemies]</c>, not "salvage" (which would
/// hide the combat) or "combat" (which would hide the salvage).
///
/// <para>Tag vocabulary (sorted alphabetically in the returned list):
/// <c>collect_items</c> / <c>collect_salvage</c> / <c>haul_goods</c> /
/// <c>kill_enemies</c> / <c>mine_ore</c> / <c>protect_unit</c> /
/// <c>travel</c>. Dedup via <see cref="HashSet{T}"/> so a two-step
/// mission with two KillEnemies objectives surfaces one
/// <c>kill_enemies</c> tag. Returns an empty list for missions with no
/// qualifying objectives (e.g. TriggerObjective-only missions like
/// ClearAsteroidField) — the LLM still sees the mission name and
/// subclass through other journal fields.</para>
///
/// <para>The <c>Mining</c> branch peeks at <c>Fields["itemCategory"]</c>
/// because vanilla's <c>Mining</c> class is a misnomer: it's a
/// polymorphic "gather N of category X" class, disambiguated by the
/// itemCategory field on the class itself. The peek is reading the
/// canonical discriminator vanilla designed, not papering over a
/// VGAnima hack. Note: since the factory now emits the real
/// <c>Salvage</c> subclass for gather_salvage (see
/// <c>MissionFactoryFromJson.BuildGather</c>), new salvage records
/// arrive as <c>Type="Salvage"</c> directly — the Mining+Salvage
/// category branch only matters for pre-AT-T1 records still in the
/// journal.</para></summary>
internal static class MissionRecordArchetype
{
    // Objective-type constants (case-sensitive ordinal per the
    // VGMissionJournal API contract — see VGMissionJournal/api.md).
    private const string TypeKillEnemies      = "KillEnemies";
    private const string TypeProtectUnit      = "ProtectUnit";
    private const string TypeMining           = "Mining";
    private const string TypeSalvage          = "Salvage";
    private const string TypeCollectItemTypes = "CollectItemTypes";
    private const string TypeTravelToPOI      = "TravelToPOI";

    // ItemCategory enum values surfaced via Fields["itemCategory"]
    // (camelCased key; value is the enum ToString()).
    private const string CatOre             = "Ore";
    private const string CatSalvage         = "Salvage";
    private const string CatTradeGoods      = "TradeGoods";
    private const string CatRefinedProduct  = "RefinedProduct";

    // Canonical tag strings emitted into LlmJournalEntry.Objectives
    // and LlmRegionallyKnownEntry.RecentActivity.
    public const string TagKillEnemies    = "kill_enemies";
    public const string TagProtectUnit    = "protect_unit";
    public const string TagMineOre        = "mine_ore";
    public const string TagCollectSalvage = "collect_salvage";
    public const string TagHaulGoods      = "haul_goods";
    public const string TagCollectItems   = "collect_items";
    public const string TagTravel         = "travel";

    /// <summary>Extract the distinct objective tags present in the
    /// mission record. Sorted alphabetically (ordinal) for a stable
    /// prompt rendering.</summary>
    public static IReadOnlyList<string> ObjectiveTags(MissionRecord record)
    {
        if (record.Steps is null) return Array.Empty<string>();

        var tags = new HashSet<string>(StringComparer.Ordinal);
        foreach (var step in record.Steps)
        {
            if (step.Objectives is null) continue;
            foreach (var obj in step.Objectives)
            {
                var tag = ClassifyObjective(obj);
                if (tag is not null) tags.Add(tag);
            }
        }

        if (tags.Count == 0) return Array.Empty<string>();
        var arr = new string[tags.Count];
        tags.CopyTo(arr);
        Array.Sort(arr, StringComparer.Ordinal);
        return arr;
    }

    private static string? ClassifyObjective(MissionObjectiveDefinition obj) =>
        obj.Type switch
        {
            TypeKillEnemies      => TagKillEnemies,
            TypeProtectUnit      => TagProtectUnit,
            TypeSalvage          => TagCollectSalvage,
            TypeCollectItemTypes => TagCollectItems,
            TypeTravelToPOI      => TagTravel,
            TypeMining           => ClassifyMining(obj.Fields),
            _                    => null,
        };

    private static string ClassifyMining(IReadOnlyDictionary<string, object?>? fields)
    {
        if (fields is null
            || !fields.TryGetValue("itemCategory", out var catObj)
            || catObj is not string cat)
        {
            // No category → default to ore (the class name's namesake).
            return TagMineOre;
        }

        return cat switch
        {
            CatSalvage        => TagCollectSalvage, // legacy pre-AT-T1 records
            CatOre            => TagMineOre,
            CatTradeGoods     => TagHaulGoods,
            CatRefinedProduct => TagHaulGoods,
            _                 => TagMineOre,
        };
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
