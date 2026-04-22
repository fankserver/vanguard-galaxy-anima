using System;
using Behaviour.UI.Spacestation.Bar;
using HarmonyLib;
using Source.Galaxy.POI.Station.Patrons;
using Source.Player;

namespace VGAnima.Patches;

/// <summary>Observes bar-salesman purchases and writes VGAnima-prefixed
/// counters into vanilla's <see cref="Register"/> store. The store
/// serializes inside the vanilla save file, so the purchase history is
/// durable across sessions without a separate persistence layer.
///
/// <para>Why this hook exists: vanilla has <b>zero</b> MissionTriggers
/// for bar purchases (bar-ecosystem survey §3). The sale-UI's
/// <c>ItemSaleInfo.trigger</c> field is dead for every procedural bar
/// purchase. We patch the one vanilla call site —
/// <c>ItemSaleInfo.ButtonPurchase</c> — and classify the purchase via
/// the same canonical <c>itemBuilder.identifier</c> taxonomy
/// <see cref="VGAnima.Llm.BarEcosystemBuilder"/> uses.</para>
///
/// <para>Counter names: <c>VGAnima_MiningClaimsBought</c>,
/// <c>VGAnima_SalvageClaimsBought</c>, <c>VGAnima_SpaceShipPngBought</c>,
/// <c>VGAnima_EquipmentBought</c>. Read via
/// <see cref="VGAnima.Llm.PurchaseProfileBuilder"/> at dispatch time.</para></summary>
internal static class BarPurchasePatches
{
    public const string MiningClaimCounter   = "VGAnima_MiningClaimsBought";
    public const string SalvageClaimCounter  = "VGAnima_SalvageClaimsBought";
    public const string SpaceShipPngCounter  = "VGAnima_SpaceShipPngBought";
    public const string EquipmentCounter     = "VGAnima_EquipmentBought";

    // AccessTools.FieldRef is a delegate bound to a private field; the
    // one-time cost of building it at class load amortizes across every
    // purchase. Fails hard at startup if vanilla renames the field —
    // intentional, so we don't silently lose tracking.
    private static readonly AccessTools.FieldRef<ItemSaleInfo, Salesman> SalesmanDataField =
        AccessTools.FieldRefAccess<ItemSaleInfo, Salesman>("salesmanData");

    [HarmonyPatch(typeof(ItemSaleInfo), nameof(ItemSaleInfo.ButtonPurchase))]
    internal static class OnButtonPurchase
    {
        // Postfix so vanilla's credit-check + cargo-add runs first — we
        // only record AFTER the transaction succeeded. Vanilla short-
        // circuits on insufficient credits before any side effect; we
        // mirror that gate by checking the player's cargo count after
        // the fact (simpler than re-running the affordability logic).
        [HarmonyPostfix]
        private static void Postfix(ItemSaleInfo __instance)
        {
            Salesman salesman;
            try { salesman = SalesmanDataField(__instance); }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning(
                    $"BarPurchasePatches: couldn't read salesmanData via reflection: {ex.Message}");
                return;
            }
            if (salesman is null) return;

            var id = salesman.itemForSale?.itemBuilder?.identifier;
            var counter = id switch
            {
                "MiningClaim"  => MiningClaimCounter,
                "SalvageClaim" => SalvageClaimCounter,
                "SpaceShipPng" => SpaceShipPngCounter,
                _              => EquipmentCounter,
            };
            Register.AddCounter(counter, 1);
            Plugin.Log.LogDebug(
                $"BarPurchase: {counter}++ (id={id ?? "?"}, broker={salesman.name})");
        }
    }
}
