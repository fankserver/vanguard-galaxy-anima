namespace VGAnima.Persistence;

/// <summary>Canonical outcome labels used in LLM context JSON.
/// Surfaced in journal windows regardless of mission source.</summary>
internal static class CompletedMissionOutcomes
{
    public const string Completed  = "completed";
    public const string Failed     = "failed";
    public const string Abandoned  = "abandoned";
    /// <summary>Journal active-window only — marks in-flight entries
    /// so the LLM can distinguish them from resolved history.</summary>
    public const string InProgress = "in_progress";
}

/// <summary>Canonical archetype labels. Aligned with vanilla's actual
/// objective vocabulary, not VGAnima-invented aggregates:
/// <list type="bullet">
///   <item><c>combat</c> — <c>KillEnemies</c> (BountyHunt, ClearPoi,
///     ClearAsteroidField, ClearSalvageField, StationBattle).</item>
///   <item><c>escort</c> — <c>ProtectUnit</c> (Escort, HelpMiner).
///     Keep-the-unit-alive is load-bearing, so this overrides
///     <c>combat</c> when both are present.</item>
///   <item><c>mining</c> — <c>Mining</c> objective with
///     <c>itemCategory = Ore</c> (MineOre, OreSamples). Distinct skill
///     tree from salvage — not a generic "gather" label.</item>
///   <item><c>salvage</c> — <c>Salvage</c> objective OR <c>Mining</c>
///     objective with <c>itemCategory = Salvage</c> (SalvageWreck,
///     SalvageSamples, VGAnima's gather_salvage intents). The Mining-
///     class-but-Salvage-category case exists because VGAnima's factory
///     unified both via <c>MiningObjective</c> for quantity semantics.</item>
///   <item><c>trade</c> — <c>CollectItemTypes</c>, or <c>Mining</c>
///     objective with <c>itemCategory</c> in <c>{TradeGoods,
///     RefinedProduct}</c> (TradeMaterials, TradeTerminal, VGAnima's
///     haul_goods intent). Commodity hauling, not production.</item>
///   <item><c>deliver</c> — <c>TravelToPOI</c> alone with no other
///     qualifying objective (Courier, DeliverCraftedGoods). Drop-off
///     travel that's part of a multi-objective mission does NOT trigger
///     <c>deliver</c> on its own.</item>
///   <item><c>other</c> — <c>TriggerObjective</c> / <c>TradeOffer</c> /
///     <c>Reputation</c> / anything unclassified.</item>
/// </list></summary>
internal static class MissionArchetypes
{
    public const string Combat   = "combat";
    public const string Escort   = "escort";
    public const string Mining   = "mining";
    public const string Salvage  = "salvage";
    public const string Trade    = "trade";
    public const string Deliver  = "deliver";
    public const string Other    = "other";
}
