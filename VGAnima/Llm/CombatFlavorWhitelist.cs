using System.Collections.Generic;

namespace VGAnima.Llm;

/// <summary>Closed list of combat-encounter flavors the LLM may pick for
/// <see cref="ClearCombatSiteIntent"/>. Each flavor is a distinct
/// narrative shape ("patrol sighted", "fortified garrison", "hidden
/// ambush") backed by a plugin-owned ship composition — the LLM picks
/// the flavor that matches its pitch, the plugin owns the mechanical
/// fleet layout and reinforcement timing.
///
/// <para>Why an enum rather than LLM-authored composition JSON: the
/// v2 architectural principle is "LLM authors narrative, plugin owns
/// mechanics." Letting the LLM emit exact ship counts would let it
/// produce shapes that validate but don't play well, and would bloat
/// the prompt with composition rules. The enum is the contract
/// surface: narrative intent in, mechanical fleet out.</para>
///
/// <para>Null (no flavor set) keeps the existing balanced default —
/// 3-5 ships initial + 2-3 reinforcement wave. Flavored combat is
/// opt-in; the LLM only sets a flavor when its pitch narrows to
/// one of these shapes.</para></summary>
internal static class CombatFlavorWhitelist
{
    /// <summary>Patrol that sighted the player. Initial 2 small + 1
    /// medium (2 scouts + lead). Fast reinforcement (2 small) is the
    /// "they called for backup" beat. Slow reinforcement (1 big) is
    /// the reaction-force response.</summary>
    public const string Scouting = "scouting";

    /// <summary>Fortified garrison. Initial 1 big + 4 small
    /// (a command ship + its escorts). Fast reinforcement (4 small)
    /// is the outer perimeter patrols closing in. Slow (2 big) is the
    /// HQ response — pulls the combat out for ~60s.</summary>
    public const string Outpost  = "outpost";

    /// <summary>Hidden base of operations that ambushes the player.
    /// Initial 3 big — heavy up front, the trap was set. Fast
    /// reinforcement (3 small) is the scouts the base had posted
    /// racing back. Slow (2 big) is reserves from another hideout.</summary>
    public const string Lair     = "lair";

    public static readonly IReadOnlyList<string> All = new[]
    {
        Scouting,
        Outpost,
        Lair,
    };

    private static readonly HashSet<string> Set = new(All);

    public static bool Contains(string s) => Set.Contains(s);
}
