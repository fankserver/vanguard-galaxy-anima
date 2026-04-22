using Source.Player;
using VGAnima.Patches;

namespace VGAnima.Llm;

/// <summary>Reads the VGAnima-prefixed purchase counters written by
/// <see cref="BarPurchasePatches"/> (bar salesmen) and
/// <see cref="ShopPurchasePatches"/> (station commodity shops) from
/// vanilla's <see cref="Register"/> store, producing a compact profile
/// the LLM reads as player-preference signal:
/// <list type="bullet">
///   <item>"you've bought mining claims" ↔ offer mining-claim rewards;</item>
///   <item>"you shop at the Salvage Shop often" ↔ frame the pitch around
///         salvage-trade economics.</item>
/// </list>
///
/// <para>Pure read — no mutation, no hooks. Safe from any thread. Gated
/// behind a catch-all so missing vanilla runtime (unit tests) silently
/// returns null rather than crashing the context gather.</para></summary>
internal static class PurchaseProfileBuilder
{
    public static LlmPurchaseProfileSection? Build()
    {
        try
        {
            var mining      = Register.GetCounter(BarPurchasePatches.MiningClaimCounter);
            var salvage     = Register.GetCounter(BarPurchasePatches.SalvageClaimCounter);
            var shipPng     = Register.GetCounter(BarPurchasePatches.SpaceShipPngCounter);
            var equipment   = Register.GetCounter(BarPurchasePatches.EquipmentCounter);

            var miningShop  = Register.GetCounter(ShopPurchasePatches.MiningShopCounter);
            var salvageShop = Register.GetCounter(ShopPurchasePatches.SalvageShopCounter);
            var generalShop = Register.GetCounter(ShopPurchasePatches.GeneralShopCounter);
            var otherShop   = Register.GetCounter(ShopPurchasePatches.OtherShopCounter);

            // If the player has bought literally nothing ever — bar or
            // shop — the profile adds no signal. Omit entirely rather than
            // emit eight zeros that waste context tokens.
            if (mining == 0 && salvage == 0 && shipPng == 0 && equipment == 0
                && miningShop == 0 && salvageShop == 0 && generalShop == 0 && otherShop == 0)
                return null;

            return new LlmPurchaseProfileSection
            {
                MiningClaimsBought  = mining,
                SalvageClaimsBought = salvage,
                SpaceShipPngBought  = shipPng,
                EquipmentBought     = equipment,
                MiningShopBuys      = miningShop,
                SalvageShopBuys     = salvageShop,
                GeneralShopBuys     = generalShop,
                OtherShopBuys       = otherShop,
            };
        }
        catch
        {
            // Tests or pre-init — just skip the section.
            return null;
        }
    }
}
