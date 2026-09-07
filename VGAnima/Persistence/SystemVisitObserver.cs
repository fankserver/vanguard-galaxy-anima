using System;
using System.Collections.Generic;
using VGModAPI;

namespace VGAnima.Persistence;

/// <summary>Records per-system visits from witnessed <see cref="ITravelEvents"/>
/// arrivals. Replaces the retired <c>TravelManager.JumpToSystem</c> Harmony
/// prefix, which recorded a <em>requested</em> destination before the jump
/// coroutine had proven anything.
///
/// <para>Only <see cref="TravelTransitionKind.Arrived"/> facts in
/// <see cref="TravelMode.JumpGate"/> or <see cref="TravelMode.Wormhole"/> mode
/// can increment a visit, and always at
/// <see cref="TravelTransition.ActualLocation"/> — never
/// <see cref="TravelTransition.RequestedDestination"/>, so a tutorial-exit
/// rewrite is recorded where the ship actually ended up. Requests, departures,
/// cancellations, route completion and in-system POI arrivals never count as a
/// cross-system visit; placements seed the current identity without counting.</para>
///
/// <para>Dedup is session-scoped: an event whose session is not the provider's
/// current session cannot mutate the registry, a replaced session resets all
/// tracking, out-of-order/replayed sequences are dropped, and one witnessed
/// travel leg (operation id) can count at most one visit. Visiting A, then B,
/// then A again is three legs and therefore two visits to A.</para>
///
/// <para><see cref="TravelTransition.GameSeconds"/> is the authoritative visit
/// time; no player or global clock is consulted. System labels come from the
/// public location snapshot only — a null
/// <see cref="TravelLocation.SystemName"/> keeps whatever label the registry
/// already holds instead of forcing a lazy vanilla name lookup.</para></summary>
internal sealed class SystemVisitObserver : IDisposable
{
    private readonly ITravelEvents _events;
    private readonly PersistedBrokerRegistry _registry;
    private readonly Action<Exception>? _failed;
    private readonly IDisposable _subscription;
    /// <summary>Legs already accounted for in the current session. Cleared on
    /// session replacement and slot load, so it is bounded by the travel legs
    /// of one session.</summary>
    private readonly HashSet<Guid> _countedLegs = new();
    private Guid? _session;
    private string? _currentSystemId;
    private long _lastSequence;
    private bool _disposed;

    /// <summary>Latched on the first observer failure. A faulted observer stops
    /// recording; already-recorded history stays in the registry.</summary>
    internal bool Faulted { get; private set; }

    /// <summary>False once disposed or faulted. Anima omits the
    /// <c>regionally_known</c> prompt window while this is false rather than
    /// pitching stale visit counts.</summary>
    internal bool IsRecording => !_disposed && !Faulted;

    internal SystemVisitObserver(ITravelEvents events, PersistedBrokerRegistry registry, Action<Exception>? failed = null)
    { _events = events; _registry = registry; _failed = failed; _subscription = events.Subscribe("vganima", Receive); }

    /// <summary>Drops the current-system latch and leg dedup so the first
    /// arrival after a slot load records against the newly loaded history.
    /// Called by <c>SaveLoadPatch</c>, whose prefix also replaces the registry
    /// contents; recorded visits are never truncated here.</summary>
    internal void ResetVisitTracking()
    {
        _session = null; _currentSystemId = null; _lastSequence = 0; _countedLegs.Clear();
    }

    private void Receive(TravelTransition transition)
    {
        if (_disposed || Faulted) return;
        try { Observe(transition); }
        catch (Exception error) { Faulted = true; _failed?.Invoke(error); }
    }

    private void Observe(TravelTransition transition)
    {
        // Foreign or stale evidence (including anything queued for a replaced
        // session) can never mutate the current registry.
        if (_events.SessionId != transition.SessionId) return;
        if (_session != transition.SessionId) { _session = transition.SessionId; _currentSystemId = null; _lastSequence = 0; _countedLegs.Clear(); }
        if (transition.Sequence <= _lastSequence) return;
        _lastSequence = transition.Sequence;

        switch (transition.Kind)
        {
            case TravelTransitionKind.InitialPlacement:
            case TravelTransitionKind.RecoveredPlacement:
                // Verified identity, not travel: seeds the latch, counts nothing.
                if (transition.ActualLocation != null) _currentSystemId = transition.ActualLocation.SystemId;
                return;
            case TravelTransitionKind.Arrived:
                var arrival = transition.ActualLocation;
                if (arrival == null) return;
                if (transition.Mode is not (TravelMode.JumpGate or TravelMode.Wormhole))
                {
                    // In-system POI arrival: confirms where the ship is, never a system visit.
                    _currentSystemId = arrival.SystemId;
                    return;
                }
                if (transition.OperationId is not { } leg || !_countedLegs.Add(leg)) return;
                var transitioned = _currentSystemId != arrival.SystemId;
                _currentSystemId = arrival.SystemId;
                if (transitioned) _registry.NoteSystemVisit(arrival.SystemId, arrival.SystemName, transition.GameSeconds);
                return;
            default:
                // Requested, Departed, Cancelled and RouteCompleted are not arrivals.
                return;
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true; _subscription.Dispose(); _countedLegs.Clear();
    }
}
