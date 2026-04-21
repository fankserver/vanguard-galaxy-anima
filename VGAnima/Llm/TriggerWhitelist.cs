using System.Collections.Generic;

namespace VGAnima.Llm;

/// <summary>Whitelisted <see cref="Source.MissionSystem.MissionTrigger"/>
/// values for <c>TriggerObjective</c>. Only the three station-travel triggers
/// that fire during plain gameplay are allowed at launch. Spec §3.
///
/// Note: spec lists <c>TravelToPOI</c> — that is NOT a real enum value. The
/// closest vanilla equivalent for "go somewhere" is <c>MoveToArea</c>. Plan
/// uses <c>MoveToArea</c>.</summary>
internal static class TriggerWhitelist
{
    private static readonly HashSet<string> Names = new()
    {
        "DockedWithSpaceStation",
        "ArrivedAtSpaceStation",
        "MoveToArea",
    };

    public static IReadOnlyCollection<string> All => Names;

    public static bool Contains(string name) => name != null && Names.Contains(name);
}
