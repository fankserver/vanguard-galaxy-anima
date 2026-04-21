using System.Collections.Generic;

namespace VGAnima.Persistence;

/// <summary>In-memory authoritative registry of persisted broker entries
/// during a session. Mutated by broker creation, mission lifecycle hooks,
/// and bar refresh (lastSeen bumps). Flushed to disk by
/// <see cref="SidecarIO"/> on vanilla save-write. Replaced wholesale by
/// sidecar contents on vanilla save-load.
///
/// <para>Primary index: <c>storyId → PersistedEntry</c>. Secondary index:
/// <c>seed → storyId</c>, maintained consistently on <see cref="Add"/>,
/// <see cref="Remove"/>, and <see cref="Clear"/>. All mutations preserve
/// both indexes; adding the same storyId twice evicts the old entry's
/// seed from the secondary index before indexing the new one.</para></summary>
internal sealed class PersistedBrokerRegistry
{
    private readonly Dictionary<string, PersistedEntry> _byStoryId = new();
    private readonly Dictionary<string, string>         _storyIdBySeed = new();

    public void Clear()
    {
        _byStoryId.Clear();
        _storyIdBySeed.Clear();
    }

    public void LoadFrom(IEnumerable<PersistedEntry> entries)
    {
        Clear();
        foreach (var e in entries) Add(e);
    }

    public void Add(PersistedEntry entry)
    {
        if (_byStoryId.TryGetValue(entry.StoryId, out var existing))
            _storyIdBySeed.Remove(existing.Broker.Seed);

        _byStoryId[entry.StoryId] = entry;
        _storyIdBySeed[entry.Broker.Seed] = entry.StoryId;
    }

    public void Remove(string storyId)
    {
        if (!_byStoryId.TryGetValue(storyId, out var entry)) return;
        _byStoryId.Remove(storyId);
        _storyIdBySeed.Remove(entry.Broker.Seed);
    }

    public PersistedEntry? Get(string storyId) =>
        _byStoryId.TryGetValue(storyId, out var e) ? e : null;

    public PersistedEntry? FindBySeed(string seed) =>
        _storyIdBySeed.TryGetValue(seed, out var storyId) ? _byStoryId[storyId] : null;

    public IReadOnlyCollection<PersistedEntry> All() => _byStoryId.Values;

    public void MarkAccepted(string storyId)
    {
        if (!_byStoryId.TryGetValue(storyId, out var entry)) return;
        _byStoryId[storyId] = entry with { State = PersistedEntryStates.Accepted };
    }

    public void BumpLastSeen(string storyId, double gameSeconds, string realUtc)
    {
        if (!_byStoryId.TryGetValue(storyId, out var entry)) return;
        _byStoryId[storyId] = entry with
        {
            Timestamps = entry.Timestamps with
            {
                LastSeenGameSeconds = gameSeconds,
                LastSeenRealUtc     = realUtc,
            },
        };
    }
}
