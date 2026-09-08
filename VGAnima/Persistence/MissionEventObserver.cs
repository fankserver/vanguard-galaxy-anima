using System;
using System.Collections.Generic;
using VGModAPI;

namespace VGAnima.Persistence;

/// <summary>Updates owned provider definitions from witnessed events, without owning mission history.</summary>
internal sealed class MissionEventObserver : IDisposable
{
    internal const string StoryPrefix = "vganima_llm_";
    private readonly PersistedBrokerRegistry _registry;
    private readonly Dictionary<string, HashSet<Guid>> _live = new(StringComparer.Ordinal);
    private readonly IDisposable _subscription;
    private readonly Action<Exception>? _failed;
    private readonly Func<PersistedEntry, bool>? _retireBar;
    internal bool Faulted { get; private set; }
    private Guid? _session;
    private bool _disposed;

    internal MissionEventObserver(IMissionEvents events, PersistedBrokerRegistry registry, Action<Exception>? failed = null, Func<PersistedEntry, bool>? retireBar = null)
    { _registry = registry; _failed = failed; _retireBar = retireBar; _subscription = events.Subscribe("vganima", Receive); }

    private void Receive(MissionTransition transition)
    {
        if (_disposed || Faulted) return;
        try { Observe(transition); }
        catch (Exception error) { Faulted = true; _failed?.Invoke(error); }
    }
    private void Observe(MissionTransition transition)
    {
        var snapshot = transition.Mission;
        if (_session != snapshot.SessionId) { _live.Clear(); _session = snapshot.SessionId; }
        var id = snapshot.DefinitionId;
        if (id == null || !id.StartsWith(StoryPrefix, StringComparison.Ordinal)) return;
        if (transition.Kind == MissionTransitionKind.Accepted || transition.Kind == MissionTransitionKind.Restored)
        {
            if (_registry.Get(id) == null) return;
            if (!_live.TryGetValue(id, out var instances)) _live[id] = instances = new HashSet<Guid>();
            instances.Add(snapshot.InstanceId);
            // Restoration associates current live instances with an owned definition; it is not a new acceptance.
            if (transition.Kind == MissionTransitionKind.Accepted) _registry.MarkAccepted(id);
            return;
        }
        if (transition.Kind is not (MissionTransitionKind.Completed or MissionTransitionKind.Failed or MissionTransitionKind.Abandoned or MissionTransitionKind.Removed)) return;
        if (!_live.TryGetValue(id, out var active) || !active.Remove(snapshot.InstanceId)) return;
        if (active.Count != 0) return;
        _live.Remove(id);
        var entry = _registry.Get(id);
        if (entry != null && _retireBar != null)
        {
            // Keep the identity durably available if save-in-flight or another guard refuses removal.
            _registry.MarkBarRetirement(id);
            if (!_retireBar(entry)) return;
        }
        _registry.Remove(id);
    }
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true; _subscription.Dispose(); _live.Clear();
    }
}
