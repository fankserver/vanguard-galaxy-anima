using System;
using Source.Player;

namespace VGAnima.Persistence;

/// <summary>Production <see cref="IClock"/>. Game seconds come from
/// <see cref="GamePlayer.current"/>'s elapsedTime accumulator (same
/// property <see cref="VGAnima.Llm.GameStateView.ElapsedSeconds"/> reads);
/// falls back to 0 when there's no active player (tests, pre-init). Real
/// time is always <see cref="DateTime.UtcNow"/>.</summary>
internal sealed class GameClock : IClock
{
    public double GameSeconds => GamePlayer.current?.elapsedTime ?? 0.0;

    public DateTime UtcNow => DateTime.UtcNow;
}
