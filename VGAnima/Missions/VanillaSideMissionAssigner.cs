using System;
using System.Collections.Generic;
using Source.MissionSystem;
using VGAnima.Patches;

namespace VGAnima.Missions;

/// <summary>Assigns one of the three vanilla side-mission storyIds to a broker.
/// Rotation is deterministic: a stable hash of the salesman seed picks a
/// starting index into the 3-element candidate list, and the assigner walks
/// forward (with wrap-around) until it finds a candidate that passes all
/// three filters in order:
///   <list type="number">
///     <item>Not in the player's <c>missionsArchive</c>.</item>
///     <item><c>StoryMission.IsAvailable(player, id) == true</c>.</item>
///     <item>Not already assigned to another broker in the same bar.</item>
///   </list>
/// Returns null when every candidate is filtered out — the caller skips
/// conversion and the patron stays vanilla.</summary>
internal sealed class VanillaSideMissionAssigner : IMissionAssigner
{
    private static readonly string[] Candidates =
    {
        "SideMissionPatrol",
        "SideMissionBounty",
        "SideMissionFastLane",
    };

    private readonly Func<string, bool> _isAvailable;

    /// <summary>Production ctor. Availability defers to
    /// <see cref="StoryMission.IsAvailable(Source.Player.GamePlayer, string)"/>
    /// with the real <c>GamePlayer.current</c>.</summary>
    public VanillaSideMissionAssigner()
        : this(id => StoryMission.IsAvailable(Source.Player.GamePlayer.current, id))
    { }

    /// <summary>Test ctor — inject a predicate to bypass the real StoryMission
    /// registry (populated by a Unity-bound static ctor).</summary>
    public VanillaSideMissionAssigner(Func<string, bool> isAvailable)
    {
        _isAvailable = isAvailable;
    }

    public string? Assign(string salesmanSeed,
                          ISet<string> alreadyAssignedInThisBar,
                          IGamePlayerView player)
    {
        // Stable hash: sum char codes (seed strings are ASCII-ish in practice).
        // We deliberately avoid string.GetHashCode — it's randomized per-process
        // on modern .NET and would make rotation non-deterministic across runs.
        int sum = 0;
        foreach (var ch in salesmanSeed) sum = unchecked(sum * 31 + ch);
        int start = ((sum % Candidates.Length) + Candidates.Length) % Candidates.Length;

        for (int offset = 0; offset < Candidates.Length; offset++)
        {
            var id = Candidates[(start + offset) % Candidates.Length];
            if (player.IsArchived(id)) continue;
            if (!_isAvailable(id)) continue;
            if (alreadyAssignedInThisBar.Contains(id)) continue;
            return id;
        }
        return null;
    }
}
