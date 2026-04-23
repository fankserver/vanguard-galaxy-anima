using System.Collections.Generic;

namespace VGAnima.Llm;

/// <summary>Closed list of narrative intents the LLM may pick for a
/// mission step. Replaces v1's <c>ObjectiveTypeWhitelist</c> /
/// <c>TriggerWhitelist</c> / <c>ItemCategoryWhitelist</c> — the LLM now
/// speaks in intents, the plugin translates each intent into vanilla
/// objective + POI mechanics.
///
/// <para>Kept intentionally small (7 entries). Each intent maps 1:1 to a
/// factory method in <see cref="VGAnima.Missions.MissionFactoryFromJson"/>;
/// adding a new intent = add a string here, add a record in
/// <see cref="LlmIntent"/>, add a parse branch in
/// <see cref="MissionBlockValidator"/>, add a factory method. One
/// dimension, four coordinated edits.</para>
///
/// <para>Deferred for a later version: <c>escort_to_station</c>. Vanilla's
/// escort pattern needs <c>CreateFixedPayload</c> + <c>playerFriendly</c>
/// + <c>AddGuards</c> + <c>CreateEscortLocation</c> +
/// <c>EscortUnitCargoUnloaded</c> trigger all lining up; shipping 7
/// intents that all work beats shipping 8 where the 8th is flaky.</para></summary>
internal static class IntentWhitelist
{
    public const string ClearCombatSite       = "clear_combat_site";
    public const string GatherOre             = "gather_ore";
    public const string GatherSalvage         = "gather_salvage";
    public const string DefendedGatherOre     = "defended_gather_ore";
    public const string DefendedGatherSalvage = "defended_gather_salvage";
    public const string DeliverToStation      = "deliver_to_station";
    public const string HaulGoods             = "haul_goods";

    public static readonly IReadOnlyList<string> All = new[]
    {
        ClearCombatSite,
        GatherOre,
        GatherSalvage,
        DefendedGatherOre,
        DefendedGatherSalvage,
        DeliverToStation,
        HaulGoods,
    };

    private static readonly HashSet<string> Set = new(All);

    public static bool Contains(string s) => Set.Contains(s);

    /// <summary>Maps an intent to the archetype keys used by
    /// <see cref="MissionGuidanceBuilder"/>'s weight + forbidden list.
    /// Composite intents (defended-gather, haul) surface BOTH archetypes;
    /// if ANY of those archetypes is forbidden, the intent is blocked by
    /// <see cref="MissionBlockValidator"/>.</summary>
    public static IReadOnlyList<string> Archetypes(string intent) => intent switch
    {
        ClearCombatSite       => new[] { "combat" },
        GatherOre             => new[] { "gather" },
        GatherSalvage         => new[] { "salvage" },
        // Composite: both archetypes apply. A defended-ore intent is
        // blocked if either `combat` OR `gather` is in forbidden_archetypes.
        DefendedGatherOre     => new[] { "combat", "gather" },
        DefendedGatherSalvage => new[] { "combat", "salvage" },
        DeliverToStation      => new[] { "deliver" },
        // haul = gather trade goods + deliver to station. Composite.
        HaulGoods             => new[] { "gather", "deliver" },
        _                     => System.Array.Empty<string>(),
    };
}
