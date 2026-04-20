using System.Collections.Generic;
using VGAnima.Patches;

namespace VGAnima.Missions;

/// <summary>Decides which vanilla <c>storyId</c> a newly-converted broker
/// should offer. Returns <c>null</c> when no candidate passes the availability
/// / dedup filters — the caller (<see cref="Patches.BarRefreshPatches"/>) must
/// skip conversion and drop the patron candidate.</summary>
internal interface IMissionAssigner
{
    /// <param name="salesmanSeed">The broker's salesman seed. Used as hash
    /// input for deterministic candidate rotation.</param>
    /// <param name="alreadyAssignedInThisBar">Story IDs already assigned to
    /// other brokers in the same bar this refresh cycle. Used for per-bar dedup.
    /// Typed as <see cref="ISet{T}"/> (not <c>IReadOnlySet&lt;T&gt;</c>, which
    /// isn't available in netstandard2.1); callers pass a <see cref="HashSet{T}"/>.</param>
    /// <param name="player">Player-state view for availability filtering.</param>
    /// <returns>The selected storyId, or null if every candidate is filtered out.</returns>
    string? Assign(string salesmanSeed,
                   ISet<string> alreadyAssignedInThisBar,
                   IGamePlayerView player);
}
