using System.Collections.Generic;

namespace VGAnima.Llm;

/// <summary>Whitelisted reward <c>type</c> discriminator values. Spec §4.</summary>
internal static class RewardTypeWhitelist
{
    private static readonly HashSet<string> Names = new()
    {
        "Credits",
        "Experience",
        "Reputation",
    };

    public static IReadOnlyCollection<string> All => Names;

    public static bool Contains(string name) => name != null && Names.Contains(name);
}
