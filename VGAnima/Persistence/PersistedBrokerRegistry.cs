using System.Collections.Generic;
using System.Linq;

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
    // Per-system visit tallies keyed by the native system identifier.
    // Mutated by SystemVisitObserver on every witnessed cross-system
    // arrival. Unbounded — the galaxy has O(100) systems so storage is
    // trivial, and dropping entries would invalidate regional-recognition
    // signals.
    private readonly Dictionary<string, VisitedSystem>  _visitedSystems = new();

    public void Clear()
    {
        _byStoryId.Clear();
        _storyIdBySeed.Clear();
        _visitedSystems.Clear();
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

    /// <summary>Replaces the visited-systems map wholesale. Called by
    /// <c>SaveLoadPatch</c> after deserializing a sidecar. Records on a
    /// freshly-upgraded v2 sidecar arrive as null — treated as an empty
    /// map, identical to a brand-new save.</summary>
    public void LoadVisitedSystems(IEnumerable<VisitedSystem>? records)
    {
        _visitedSystems.Clear();
        if (records == null) return;
        foreach (var r in records) _visitedSystems[r.Guid] = r;
    }

    /// <summary>Records one arrival at the named system. First visit
    /// creates the entry with both timestamps equal to
    /// <paramref name="gameSeconds"/>; subsequent visits increment the
    /// counter and update <c>LastVisitGameSeconds</c> only. Callers (the
    /// Harmony patch) are responsible for the transition-latching; this
    /// method trusts that every invocation is a genuine new arrival.
    /// <para>Snapshotting the display name on every visit (not just the
    /// first) lets a later rename propagate — cheap robustness since the
    /// write is already happening. A null <paramref name="name"/> means the
    /// observed location carried no label: the previously stored label is
    /// preserved (empty for a first visit) rather than replaced by a lazily
    /// generated vanilla name.</para></summary>
    public void NoteSystemVisit(string guid, string? name, double gameSeconds)
    {
        if (_visitedSystems.TryGetValue(guid, out var existing))
        {
            _visitedSystems[guid] = existing with
            {
                Name                 = name ?? existing.Name,
                VisitCount           = existing.VisitCount + 1,
                LastVisitGameSeconds = gameSeconds,
            };
        }
        else
        {
            _visitedSystems[guid] = new VisitedSystem(
                Guid:                  guid,
                Name:                  name ?? string.Empty,
                VisitCount:            1,
                FirstVisitGameSeconds: gameSeconds,
                LastVisitGameSeconds:  gameSeconds);
        }
    }

    /// <summary>Read-only view over the visited-systems map.
    /// <c>RegionallyKnownBuilder</c> consumes it to produce the LLM's
    /// regional-recognition signal; <c>SaveWritePatch</c> materializes it
    /// into <see cref="SidecarSchema.VisitedSystems"/>.</summary>
    public IReadOnlyDictionary<string, VisitedSystem> VisitedSystems => _visitedSystems;
}
