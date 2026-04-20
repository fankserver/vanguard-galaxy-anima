using Source.MissionSystem;

namespace VGAnima.Patches;

/// <summary>Thin abstraction over the bits of <see cref="Source.Player.GamePlayer"/>
/// that <see cref="BrokerStateDetector"/> needs. Exists so the detector can be
/// unit-tested without a real <c>GamePlayer.current</c> (which is a Unity-bound
/// singleton). Prod code uses <see cref="GamePlayerView"/>; tests pass a fake.</summary>
internal interface IGamePlayerView
{
    /// <summary>True when the given storyId is in <c>GamePlayer.current.missionsArchive</c>.</summary>
    bool IsArchived(string storyId);

    /// <summary>Returns the active mission with the given storyId, or null when
    /// no such active mission exists. Mirrors <c>GamePlayer.GetActiveStoryMission</c>.</summary>
    Mission? GetActive(string storyId);
}

/// <summary>Production implementation. Reads from <see cref="Source.Player.GamePlayer.current"/>.
/// Null-safe: if <c>current</c> is null (e.g. patches firing before player init),
/// <see cref="IsArchived"/> returns false and <see cref="GetActive"/> returns null.</summary>
internal sealed class GamePlayerView : IGamePlayerView
{
    public bool IsArchived(string storyId)
    {
        var player = Source.Player.GamePlayer.current;
        if (player == null) return false;
        return player.missionsArchive.Contains(storyId);
    }

    public Mission? GetActive(string storyId)
    {
        var player = Source.Player.GamePlayer.current;
        if (player == null) return null;
        return player.GetActiveStoryMission(storyId);
    }
}
