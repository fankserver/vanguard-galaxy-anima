using System.Collections.Generic;

namespace VGAnima.Llm;

/// <summary>Curated whitelist of faction identifier strings the LLM may emit
/// in <c>source_faction</c> / <c>enemy_faction</c> / reputation <c>faction</c>
/// fields. Membership is checked via <see cref="Contains"/>; the value is the
/// literal <c>Faction.identifier</c> (PascalCase class name, confirmed in
/// <c>Source.Galaxy/Faction.cs</c> static fields) so callers can resolve to
/// a live <c>Source.Galaxy.Faction</c> via <c>Faction.Get(id)</c>.
///
/// Excludes <c>Player</c> (reserved for the commanding side). The corporations
/// trio (<c>Gold</c> / <c>Red</c> / <c>Blue</c>, grouped as
/// <c>Faction.corporations</c> in the game) is included in full — stations
/// routinely report their allegiance as one of these, so brokers legitimately
/// pitch on their behalf. Spec §3 / §4.</summary>
internal static class FactionWhitelist
{
    private static readonly HashSet<string> Ids = new()
    {
        "Marauders",
        "PoliceGuild",
        "BountyGuild",
        "TradingGuild",
        "MiningGuild",
        "IndustrialGuild",
        "SalvageGuild",
        "Stranded",
        "MercenaryGuild",
        "Smugglers",
        "Darkspacers",
        "Puppeteers",
        "Fanatics",
        "HolyRadicals",
        "Amalgam",
        "Gold",
        "Red",
        "Blue",
    };

    public static IReadOnlyCollection<string> All => Ids;

    public static bool Contains(string id) => id != null && Ids.Contains(id);
}
