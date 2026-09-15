using System;
using System.Collections.Generic;
using Source.MissionSystem;
using VGModAPI;

namespace VGAnima.Persistence;

/// <summary>Updates owned provider definitions from witnessed events, without owning mission history.</summary>
/// <remarks>
/// <para>Ownership correlation goes through the read-only native escape hatch
/// (<see cref="IVersionSensitiveMissionAccess.TryGetNative"/>) and only while
/// the transition is being dispatched: the public
/// <see cref="MissionSnapshot.DefinitionId"/> is an opaque native identifier
/// that consumers must never string-match or strip. A transition whose native
/// cannot be resolved during dispatch is simply not attributed to Anima.</para>
/// <para>Subscribing is event registration on <see cref="IMissionService.Transitioned"/>;
/// delivery is main-thread, synchronous, and never replayed. Registration and
/// disposal are main-thread-only.</para>
/// </remarks>
internal sealed class MissionEventObserver : IDisposable
{
    internal const string StoryPrefix = "vganima_llm_";
    private readonly IMissionService _service;
    private readonly PersistedBrokerRegistry _registry;
    private readonly Dictionary<string, HashSet<Guid>> _live = new(StringComparer.Ordinal);
    private readonly Action<Exception>? _failed;
    private readonly Func<PersistedEntry, bool>? _retireBar;
    internal bool Faulted { get; private set; }
    private Guid? _session;
    private bool _disposed;

    internal MissionEventObserver(IMissionService service, PersistedBrokerRegistry registry, Action<Exception>? failed = null, Func<PersistedEntry, bool>? retireBar = null)
    {
        _service = service ?? throw new ArgumentNullException(nameof(service));
        _registry = registry; _failed = failed; _retireBar = retireBar;
        _service.Transitioned += Receive;
    }

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
        var id = OwnedStoryId(snapshot);
        if (id is null || !id.StartsWith(StoryPrefix, StringComparison.Ordinal)) return;
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

    /// <summary>Attributes a snapshot to this provider by reading the native
    /// storyId during dispatch only. Never mutates or retains the native
    /// object; an unresolved native is deliberately not attributed.</summary>
    private string? OwnedStoryId(MissionSnapshot snapshot)
    {
        if (!_service.TryGetNative(snapshot, out var native)) return null;
        return native is Mission mission ? mission.storyId : null;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true; _service.Transitioned -= Receive; _live.Clear();
    }
}
