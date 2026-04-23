using System.Collections.Generic;

namespace VGAnima.Llm;

/// <summary>Closed list of combat-encounter flavors the LLM may pick for
/// <see cref="ClearCombatSiteIntent"/>. Each flavor is a distinct
/// narrative shape backed by a plugin-owned ship composition — the LLM
/// picks the flavor that matches its pitch, the plugin owns the
/// mechanical fleet layout and reinforcement timing.
///
/// <para>Why an enum rather than LLM-authored composition JSON: the
/// v2 architectural principle is "LLM authors narrative, plugin owns
/// mechanics." Letting the LLM emit exact ship counts would let it
/// produce shapes that validate but don't play well, and would bloat
/// the prompt with composition rules. The enum is the contract
/// surface: narrative intent in, mechanical fleet out.</para>
///
/// <para>Null (no flavor set) keeps the balanced default — 3-5 ships
/// initial + 2-3 reinforcement wave. Flavored combat is opt-in; the
/// LLM only sets a flavor when its pitch narrows to one of these
/// shapes.</para>
///
/// <para>Names are enemy-behavior-centric (what the enemy IS DOING or
/// HAS BECOME), not player-perspective labels. Validated by qwen
/// against 12 pitch prompts before shipping — the six distinct
/// narrative shapes cover roughly ~80% of likely LLM-generated combat
/// pitches; the remaining ~20% falls through to the null default.</para></summary>
internal static class CombatFlavorWhitelist
{
    /// <summary>Patrol that sighted the player. Escalating threat over
    /// time — scouts call home, then heavy reaction force arrives.
    /// Initial 2 small + 1 medium. Fast wave (+15s) 2 small. Slow wave
    /// (+45s) 1 big.</summary>
    public const string Scouting = "scouting";

    /// <summary>Fortified garrison at a known position — command ship
    /// with escorts, perimeter patrols, then HQ reserves. Protracted
    /// siege feel. Initial 1 big + 4 small. Fast wave (+20s) 4 small.
    /// Slow wave (+60s) 2 big.</summary>
    public const string Outpost  = "outpost";

    /// <summary>Hidden base that ambushes on arrival. Heavy up front,
    /// perimeter scouts race back, reserves from a second hideout.
    /// Most dangerous flavor — peak threat at t=0. Initial 3 big.
    /// Fast wave (+15s) 3 small. Slow wave (+45s) 2 big.</summary>
    public const string Lair     = "lair";

    /// <summary>Mobile nomadic warband — uniform pack, no base, no
    /// command hierarchy. Short engagement, nobody to call for help.
    /// Initial 5 medium (uniform). Fast wave (+15s) 3 small (tail of
    /// pack). No slow wave — they ARE the full threat.</summary>
    public const string Raid     = "raid";

    /// <summary>Cornered enemies throwing everything at the player.
    /// Peak threat is at t=0 because they've already gathered everyone
    /// they have. Staggered sub-spawn prevents an instakill wall of
    /// fire. t=0 2 big. t=5s 4 small. No reinforcements — nobody else
    /// to call. Name encodes the "no waves" mechanic via "remnants."</summary>
    public const string CorneredRemnants = "cornered_remnants";

    /// <summary>Low-quality high-count horde — drones, zealots, cheap
    /// disposable pirates. Lots of targets, individually trivial,
    /// dangerous in aggregate. Capped at 8 total to avoid Unity
    /// pathfinding stutter. Initial 5 small. Fast wave (+15s) 3 small.
    /// No big ships anywhere.</summary>
    public const string Swarm    = "swarm";

    public static readonly IReadOnlyList<string> All = new[]
    {
        Scouting,
        Outpost,
        Lair,
        Raid,
        CorneredRemnants,
        Swarm,
    };

    private static readonly HashSet<string> Set = new(All);

    public static bool Contains(string s) => Set.Contains(s);
}
