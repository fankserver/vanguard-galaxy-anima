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
        // v1-item-rewards: LLM can emit tangible-item payouts using
        // vanilla's Rewards.Item pipeline. Item kind lives in the
        // reward object's `kind` field and is narrowly whitelisted —
        // see ItemRewardKindWhitelist.
        "Item",
    };

    public static IReadOnlyCollection<string> All => Names;

    public static bool Contains(string name) => name != null && Names.Contains(name);
}
