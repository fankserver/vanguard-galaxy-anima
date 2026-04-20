namespace VGAnima.Pitch;

/// <summary>The broker's dialogue state for its assigned vanilla storyId.
/// Pure function of <c>GamePlayer.missionsArchive</c> and
/// <c>GamePlayer.GetActiveStoryMission(storyId)</c> — recomputed every click,
/// never persisted.</summary>
public enum BrokerState
{
    /// <summary>Player has not yet accepted this mission. The broker recites
    /// a pitch; on dialogue close, <c>AddMissionWithLog(storyId)</c> runs
    /// and the vanilla factory materializes the mission.</summary>
    Initial,

    /// <summary>Mission is active (<c>GetActiveStoryMission(storyId)</c>
    /// returns non-null) and <c>CanClaimRewards()</c> is false. The broker
    /// offers a short check-in.</summary>
    InProgress,

    /// <summary>Mission is active and <c>CanClaimRewards()</c> is true.
    /// The broker delivers a congrats-and-handoff dialogue; a specific line
    /// triggers <c>CompleteMission(mission)</c>, vanilla pays rewards, archives
    /// the storyId, and the broker departs on dialogue close.</summary>
    ReadyToClaim,

    /// <summary>The storyId is in <c>missionsArchive</c>. The broker says
    /// farewell and departs on dialogue close.</summary>
    Done,
}
