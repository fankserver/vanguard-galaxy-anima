using System.Collections.Generic;

namespace VGAnima.Llm;

/// <summary>Whitelisted <see cref="Source.Item.ItemCategory"/> enum names for
/// <c>CollectItemTypes.item_category</c>. Vanilla's <c>CollectItemTypes</c>
/// takes an <c>ItemCategory?</c> field, NOT a list of specific item
/// identifiers, so the LLM emits one enum-name per objective. Spec §3.
///
/// Curated set at launch — extend once we see what the LLM actually produces.
/// Excludes technical categories (Empty, Ammo, Turret, Module, Booster,
/// UnusedMissionItem, Drone, Torpedo, JumpgatePass, Usable, DefensiveTurret,
/// Currency, Crystal) that don't make sense as broker-job targets.</summary>
internal static class ItemCategoryWhitelist
{
    private static readonly HashSet<string> Names = new()
    {
        "Ore",
        "Salvage",
        "RefinedProduct",
        "TradeGoods",
        "Junk",
    };

    public static IReadOnlyCollection<string> All => Names;

    public static bool Contains(string name) => name != null && Names.Contains(name);
}
