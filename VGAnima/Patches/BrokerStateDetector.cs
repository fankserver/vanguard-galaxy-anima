using Source.Player;
using VGAnima.Cache;
using VGAnima.Pitch;

namespace VGAnima.Patches;

/// <summary>Computes the current <see cref="BrokerState"/> from
/// game-observable state plus the in-memory <see cref="ConversionRecord.Pitched"/>
/// bit (cleared on plugin reload, not persisted to the savegame). No side effects.</summary>
internal static class BrokerStateDetector
{
    public static BrokerState Detect(ConversionRecord record)
    {
        if (record == null) return BrokerState.Initial;
        var mission = record.Mission;
        var station = record.Station;

        // Player has the mission in their active list — either still working on
        // it or ready to claim rewards.
        var playerMissions = GamePlayer.current?.missions;
        if (playerMissions != null && playerMissions.Contains(mission))
        {
            return mission.CanClaimRewards() ? BrokerState.ReadyToClaim : BrokerState.InProgress;
        }

        // Mission sitting on this station's board, player hasn't accepted yet.
        var board = station?.missionBoard;
        if (board != null && board.availableMissions != null &&
            board.availableMissions.Contains(mission))
        {
            return BrokerState.Waiting;
        }

        // Mission neither on the board nor in the player's missions. Without
        // the Pitched flag we can't tell "first contact" from "mission cycled
        // through, rewarded" — both look the same to game state.
        return record.Pitched ? BrokerState.Done : BrokerState.Initial;
    }
}
