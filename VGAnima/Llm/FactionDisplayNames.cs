using System.Collections.Generic;
using System.Linq;

namespace VGAnima.Llm;

/// <summary>Identifier → in-game display-name mapping for faction
/// identifiers. The game resolves these at UI render time via
/// <c>Translation.Translate(@FactionName&lt;Identifier&gt;)</c> against the
/// locale TextAsset in <c>Resources/Language/</c>. We mirror the English
/// strings here so the LLM can write dialogue that matches what the player
/// sees in the mission board — while mission block fields
/// (<c>source_faction</c> / <c>enemy_faction</c> / reward <c>faction</c>)
/// continue to use the stable identifier for <c>Faction.Get(id)</c> lookups.
///
/// Values extracted from the live game's <c>en-US</c> TextAsset (see the
/// research agent report 2026-04-21). Hardcoded by design:
///   - Runtime translation (<c>Faction.Get(id).name</c>) is Unity-bound and
///     NREs in our xUnit appdomain (Faction..cctor limitation, same one
///     that forces skip-by-design on reward tests), so tests need a
///     dependency-free source of truth.
///   - If the game patches a display name we'd rather notice via stale
///     copy than silently drift through unverified translation.
///   - The identifier set is closed — spec §3 enumerates 18 factions
///     (plus Player, reserved). No dynamic loading path exists in
///     <c>Source.Galaxy.Faction</c>.
///
/// If an identifier isn't in the dict the fallback is the identifier
/// itself — defensive only; the whitelist and this table are checked by
/// tests to stay in sync.</summary>
internal static class FactionDisplayNames
{
    private static readonly IReadOnlyDictionary<string, string> Names = new Dictionary<string, string>
    {
        { "Marauders",       "Corsair Syndicate"   },
        { "PoliceGuild",     "Canisec"             },
        { "BountyGuild",     "Orsanon Security"    },
        { "TradingGuild",    "Intertrade Network"  },
        { "MiningGuild",     "Mindus Holdings"     },
        { "IndustrialGuild", "Forge Industries"    },
        { "SalvageGuild",    "Steel Vultures"      },
        { "Stranded",        "Stranded"            },
        { "MercenaryGuild",  "Omnitac Agency"      },
        { "Smugglers",       "Void Drifters"       },
        { "Darkspacers",     "Darkspace Compact"   },
        { "Puppeteers",      "Your Employer"       },
        { "Fanatics",        "Meridia's Chosen"    },
        { "HolyRadicals",    "Meridia's Radicals"  },
        { "Amalgam",         "Amalgam"             },
        { "Gold",            "Luminate Combine"    },
        { "Red",             "Kolyatov Collective" },
        { "Blue",            "Stellar Industries"  },
    };

    public static string Lookup(string identifier) =>
        Names.TryGetValue(identifier, out var display) ? display : identifier;

    public static IEnumerable<string> Identifiers => Names.Keys;

    public static bool Contains(string identifier) =>
        identifier != null && Names.ContainsKey(identifier);
}
