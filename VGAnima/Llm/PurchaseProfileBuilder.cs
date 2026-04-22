using Source.Player;
using VGAnima.Patches;

namespace VGAnima.Llm;

/// <summary>Reads the VGAnima-prefixed purchase counters written by
/// <see cref="BarPurchasePatches"/> from vanilla's <see cref="Register"/>
/// store, producing a compact profile the LLM reads as player-preference
/// signal: "you've bought mining claims" ↔ "offer mining-claim rewards."
///
/// <para>Pure read — no mutation, no hooks. Safe from any thread. Gated
/// behind a <see cref="System.IO.FileNotFoundException"/>-style swallow
/// if <c>Register</c> isn't available (unit tests with no vanilla
/// runtime); in that case returns null so the field is omitted from
/// the LlmContext JSON.</para></summary>
internal static class PurchaseProfileBuilder
{
    public static LlmPurchaseProfileSection? Build()
    {
        try
        {
            var mining    = Register.GetCounter(BarPurchasePatches.MiningClaimCounter);
            var salvage   = Register.GetCounter(BarPurchasePatches.SalvageClaimCounter);
            var shipPng   = Register.GetCounter(BarPurchasePatches.SpaceShipPngCounter);
            var equipment = Register.GetCounter(BarPurchasePatches.EquipmentCounter);

            // If the player has bought literally nothing ever, the
            // profile adds no signal — omit entirely rather than emit
            // four zeros that waste context tokens.
            if (mining == 0 && salvage == 0 && shipPng == 0 && equipment == 0)
                return null;

            return new LlmPurchaseProfileSection
            {
                MiningClaimsBought  = mining,
                SalvageClaimsBought = salvage,
                SpaceShipPngBought  = shipPng,
                EquipmentBought     = equipment,
            };
        }
        catch
        {
            // Tests or pre-init — just skip the section.
            return null;
        }
    }
}
