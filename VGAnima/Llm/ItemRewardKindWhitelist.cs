using System.Collections.Generic;

namespace VGAnima.Llm;

/// <summary>Whitelisted values for <c>rewards[].kind</c> when
/// <c>type = "Item"</c>. Each kind maps to one of vanilla's
/// <see cref="Behaviour.Item.Builder.ItemBuilder"/> factory methods that
/// can build an <c>InventoryItemType</c> from just the broker station's
/// <c>SystemMapData</c> + optional defaults — no runtime user input
/// required.
///
/// <para>Kept narrow at v1 because each new kind costs factory wiring:
/// a kind not on this list is rejected by the validator.</para></summary>
internal static class ItemRewardKindWhitelist
{
    public const string MiningClaim         = "MiningClaim";
    public const string SalvageClaim        = "SalvageClaim";
    public const string MaterialMiningClaim = "MaterialMiningClaim";

    private static readonly HashSet<string> Names = new()
    {
        MiningClaim,
        SalvageClaim,
        MaterialMiningClaim,
    };

    public static IReadOnlyCollection<string> All => Names;

    public static bool Contains(string name) => name != null && Names.Contains(name);
}
