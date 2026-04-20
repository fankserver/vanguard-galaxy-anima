using VGAnima.Pitch;

namespace VGAnima.Patches;

/// <summary>Pure state resolver for a broker's assigned storyId. No side
/// effects, no Unity deps — relies entirely on <see cref="IGamePlayerView"/>
/// so it's trivially unit-testable.
///
/// Order matters — archive check is first because <c>GetActiveStoryMission</c>
/// returns null for archived missions, which would otherwise collapse to
/// <see cref="BrokerState.Initial"/> and re-pitch a completed quest.</summary>
internal static class BrokerStateDetector
{
    public static BrokerState Detect(string storyId, IGamePlayerView player)
    {
        if (player.IsArchived(storyId)) return BrokerState.Done;
        var mission = player.GetActive(storyId);
        if (mission == null) return BrokerState.Initial;
        if (mission.CanClaimRewards()) return BrokerState.ReadyToClaim;
        return BrokerState.InProgress;
    }
}
