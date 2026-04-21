using System.Collections.Generic;

namespace VGAnima.Llm;

/// <summary>Whitelisted objective <c>type</c> discriminator values. Each one
/// maps to a real <c>Source.MissionSystem.Objectives.*</c> class in
/// <see cref="VGAnima.Missions.MissionFactoryFromJson"/>. Spec §3.</summary>
internal static class ObjectiveTypeWhitelist
{
    private static readonly HashSet<string> Names = new()
    {
        "KillEnemies",
        "ProtectUnit",
        "TriggerObjective",
        "CollectItemTypes",
        "ClearPoi",
    };

    public static IReadOnlyCollection<string> All => Names;

    public static bool Contains(string name) => name != null && Names.Contains(name);
}
