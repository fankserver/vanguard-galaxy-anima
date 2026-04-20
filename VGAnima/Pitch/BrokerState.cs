namespace VGAnima.Pitch;

/// <summary>Phase A: the broker's dialogue varies by the player's progress on
/// the pitched mission. All states are derived from game-observable data
/// (mission board roster, player missions, CanClaimRewards) plus one mutable
/// bit on <c>ConversionRecord.Pitched</c> to distinguish "never talked" from
/// "mission cycled through and got rewarded".</summary>
internal enum BrokerState
{
    /// <summary>Player has not yet finished the initial pitch dialogue.
    /// Broker recites the pitch + posts the mission on close.</summary>
    Initial,

    /// <summary>Mission is on the station's board; player has not accepted it.
    /// Broker nudges: "it's still up on the board".</summary>
    Waiting,

    /// <summary>Mission is in <see cref="Source.Player.GamePlayer.missions"/>
    /// but <see cref="Source.MissionSystem.Mission.CanClaimRewards"/> is false.
    /// Broker asks how it's going.</summary>
    InProgress,

    /// <summary>Player has accepted and completed the mission; rewards are
    /// ready to claim at the mission board.</summary>
    ReadyToClaim,

    /// <summary>Mission has left the board and the player's mission list —
    /// they claimed the reward. Broker wraps up politely.</summary>
    Done,
}
