using System;

namespace VGAnima.Persistence;

/// <summary>Time accessor for persistence timestamps. Two readings per
/// call: the in-game elapsed seconds (vanilla's game-time concept) and the
/// real-world UTC now. Both are written to every
/// <see cref="PersistedTimestamps"/> so future pruning policies (TTL, LRU)
/// can choose which clock matters.</summary>
internal interface IClock
{
    double GameSeconds { get; }
    DateTime UtcNow { get; }
}
