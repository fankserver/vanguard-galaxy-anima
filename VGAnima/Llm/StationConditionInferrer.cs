using System.Linq;

namespace VGAnima.Llm;

/// <summary>Derives a single-word "station condition" tag from a built
/// <see cref="LlmContext"/>. Feeds a vibes signal into the prompt so the
/// broker's linguistic register shifts to match the local atmosphere —
/// clipped/urgent in a war-torn outpost, flowery/leisurely at a luxury
/// hub, sparse at a frontier rock.
///
/// <para>v1 intentionally picks just 4 conditions with deterministic
/// priority: <c>war-torn</c> > <c>peaceful</c> > <c>bustling</c> >
/// <c>frontier</c>, falling through to <c>normal</c>. Priority-first
/// because a luxury hub in an active war zone still FEELS war-torn to a
/// broker pitching missions there; the war concern dominates.</para>
///
/// <para>Pure function over <see cref="LlmContext"/>; safe to call on the
/// Unity main thread or from tests. No game-state reads.</para></summary>
internal static class StationConditionInferrer
{
    public const string WarTorn  = "war-torn";
    public const string Peaceful = "peaceful";
    public const string Bustling = "bustling";
    public const string Frontier = "frontier";
    public const string Normal   = "normal";

    public static string Infer(LlmContext ctx)
    {
        // 1. War-torn dominates. Any hostile faction in the factions dict
        //    (rep < -500 or at war) signals combat pressure on the area.
        //    Holds even when combat archetype weight is low for THIS
        //    broker — the ambient state is still war-torn.
        var anyHostile = ctx.Factions != null
            && ctx.Factions.Any(kv => kv.Value.Relation == "hostile");
        var combatForbidden = ctx.MissionGuidance?.ForbiddenArchetypes != null
            && ctx.MissionGuidance.ForbiddenArchetypes.Contains("combat");
        if (anyHostile && !combatForbidden) return WarTorn;

        // 2. Peaceful fires only when the guidance builder explicitly
        //    forbade combat. That happens in contexts with no hostile
        //    factions reachable — a rare but narratively distinct state.
        if (combatForbidden) return Peaceful;

        // 3/4. Bustling vs frontier — facility count proxies population /
        //    economic activity. 5+ facilities (trade + refinery + yard +
        //    board + bar etc.) reads as a hub; 0-2 reads as a rock with a
        //    landing pad. Between those: normal.
        var facilityCount = ctx.Location?.StationFacilities?.Count ?? 0;
        if (facilityCount >= 5) return Bustling;
        if (facilityCount <= 2) return Frontier;

        return Normal;
    }
}
