using System;
using Behaviour.UI;
using HarmonyLib;
using Source.Galaxy.POI;
using Source.Item;
using Source.Player;

namespace VGAnima.Patches;

/// <summary>Observes station-shop (commodity) purchases — distinct from
/// the bar-salesman path covered by <see cref="BarPurchasePatches"/>.
/// Writes separate <c>VGAnima_*ShopBuys</c> counters into vanilla's
/// <see cref="Register"/> store so the LLM's purchase profile can tell
/// "bought a SalvageClaim from a bar Salvage Scout" (signal of
/// investment in claim-hunting) apart from "bought raw salvage at a
/// station's Salvage Shop" (signal of trade-route participation).
///
/// <para>Choke point: <c>InventoryInteractionManager.BuyAmount(Inventory.InventoryItem,
/// int, InventoryItemSlot, Inventory)</c> at decomp line 75334. The
/// other two <c>BuyAmount</c> overloads (<c>InventoryItemType, int</c> and
/// <c>InventoryItemSlot, int, ...</c>) both funnel into this one, so one
/// postfix catches every commodity-shop purchase path.</para>
///
/// <para>Classification reads <see cref="ShopInventory.facility"/> on the
/// source inventory — same taxonomy vanilla uses for its own shop labels
/// ("Mining Shop" / "Salvage Shop" / "General Shop" / etc. at decomp
/// line 62460-62482). Non-shop purchases (e.g. airlock materials exchange,
/// personal hangar swap) won't have a <c>ShopInventory</c> source and are
/// skipped; <c>item.inventory is ShopInventory</c> is the filter.</para></summary>
internal static class ShopPurchasePatches
{
    public const string MiningShopCounter  = "VGAnima_MiningShopBuys";
    public const string SalvageShopCounter = "VGAnima_SalvageShopBuys";
    public const string GeneralShopCounter = "VGAnima_GeneralShopBuys";
    // Bounty / Patrol / Industry / Conquest shops and any future variant.
    // Grouped because each individual counter would add prompt noise for a
    // signal that's already specific enough at the "shop-style" granularity.
    public const string OtherShopCounter   = "VGAnima_OtherShopBuys";

    [HarmonyPatch(typeof(InventoryInteractionManager), "BuyAmount",
        new[] {
            typeof(Inventory.InventoryItem),
            typeof(int),
            typeof(InventoryItemSlot),
            typeof(Inventory),
        })]
    internal static class OnBuyAmount
    {
        // Postfix so vanilla's credit-check + cargo-full + level-gate + rep-gate
        // (decomp lines 75340-75395) have already fired and the transaction
        // has been committed before we count. `__result == false` means the
        // buy bounced (insufficient credits, rep, level, cargo space); we
        // don't count failed attempts.
        [HarmonyPostfix]
        private static void Postfix(Inventory.InventoryItem item, bool __result)
        {
            if (!__result) return;
            if (item == null) return;
            // `item.inventory` is the source inventory the buy pulled from.
            // Shop purchases always route through a ShopInventory; anything
            // else is a non-shop interaction we don't want to count.
            if (item.inventory is not ShopInventory shop) return;

            try
            {
                var counter = shop.facility switch
                {
                    SpaceStationFacility.MiningShop  => MiningShopCounter,
                    SpaceStationFacility.SalvageShop => SalvageShopCounter,
                    SpaceStationFacility.GeneralShop => GeneralShopCounter,
                    _                                => OtherShopCounter,
                };
                Register.AddCounter(counter, 1);
                // Prefer the specific item's identifier
                // (<see cref="Source.Item.InventoryItemType.identifier"/>)
                // over the ItemBuilder category — the builder's id is the
                // FAMILY (e.g. "SalvageClaim") while distinct equipment
                // items like "Salvage Power I" and "Salvage Grinder MK.III"
                // share the same builder but have their own identifiers
                // on the item. Display name rides alongside for human
                // log-reading; bar ecosystem BarPurchasePatches still
                // uses the builder id deliberately (category counters).
                var itemType  = item.item;
                var specId    = itemType?.identifier;
                var builderId = itemType?.itemBuilder?.identifier;
                var id        = !string.IsNullOrEmpty(specId) ? specId
                              : !string.IsNullOrEmpty(builderId) ? builderId
                              : "?";
                var displayName = itemType?.displayName;
                var displaySuffix = string.IsNullOrEmpty(displayName)
                    ? string.Empty : $" \"{displayName}\"";
                Plugin.Log.LogDebug(
                    $"ShopPurchase: {counter}++ (facility={shop.facility}, item={id}{displaySuffix})");
            }
            catch (Exception ex)
            {
                // Register writes never fail in practice, but a stale item
                // or mid-teardown state shouldn't crash the buy flow.
                Plugin.Log.LogWarning($"ShopPurchasePatches: record failed: {ex.Message}");
            }
        }
    }
}
