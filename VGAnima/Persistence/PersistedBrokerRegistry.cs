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
    /// <summary>Upper bound on the completed-mission log size. Older entries
    /// drop off FIFO once this cap is reached. ~50 gives several in-game
    /// weeks of history at realistic mission cadence, enough for the
    /// journal context windows to always have material without unbounded
    /// sidecar growth.</summary>
    public const int MaxCompletedMissions = 50;

    private readonly Dictionary<string, PersistedEntry> _byStoryId = new();
    private readonly Dictionary<string, string>         _storyIdBySeed = new();
    // Rolling log of resolved (completed / failed / abandoned) missions,
    // ordered oldest-first. Capped at MaxCompletedMissions.
    private readonly List<CompletedMissionRecord>       _completedLog = new();

    public void Clear()
    {
        _byStoryId.Clear();
        _storyIdBySeed.Clear();
        _completedLog.Clear();
    }

    public void LoadFrom(IEnumerable<PersistedEntry> entries)
    {
        Clear();
        foreach (var e in entries) Add(e);
    }

    /// <summary>Replaces the completed-mission log wholesale. Called by
    /// <see cref="SaveLoadPatch"/> after deserializing a sidecar. Trims to
    /// the cap if the input exceeds it (paranoia — a sidecar hand-edited
    /// beyond the cap shouldn't break downstream callers).</summary>
    public void LoadCompletedMissions(IEnumerable<CompletedMissionRecord>? records)
    {
        _completedLog.Clear();
        if (records == null) return;
        var materialized = records.ToList();
        var skip = System.Math.Max(0, materialized.Count - MaxCompletedMissions);
        for (var i = skip; i < materialized.Count; i++)
            _completedLog.Add(materialized[i]);
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

    /// <summary>Appends a resolved-mission summary to the completed-mission
    /// log, trimming the oldest entry when the cap is exceeded. Caller
    /// remains responsible for subsequently removing the corresponding
    /// <see cref="PersistedEntry"/> via <see cref="Remove"/> — the two
    /// stores are distinct lifecycles (in-flight vs historical).</summary>
    public void RecordCompletion(CompletedMissionRecord record)
    {
        _completedLog.Add(record);
        if (_completedLog.Count > MaxCompletedMissions)
            _completedLog.RemoveAt(0);
    }

    /// <summary>Ordered oldest-first. The context builder reverses per
    /// window as needed (recent-first is more natural for "last N events"
    /// slicing).</summary>
    public IReadOnlyList<CompletedMissionRecord> CompletedMissions => _completedLog;
}
