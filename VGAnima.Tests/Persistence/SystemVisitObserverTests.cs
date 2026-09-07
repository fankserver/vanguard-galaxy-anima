using System;
using System.Collections.Generic;
using VGAnima.Persistence;
using VGModAPI;
using Xunit;

namespace VGAnima.Tests.Persistence;

/// <summary>Consumer semantics against a true public <see cref="ITravelEvents"/>
/// double. Nothing here touches vanilla travel types: the observer sees only
/// the API's published facts.</summary>
public sealed class SystemVisitObserverTests
{
    private sealed class TravelEventsDouble : ITravelEvents
    {
        private readonly List<Action<TravelTransition>> _callbacks = new();
        private long _sequence;
        public Guid? SessionId { get; set; } = Guid.NewGuid();
        public TravelLocation? CurrentLocation { get; private set; }
        public bool IsDispatchingCallbacks { get; private set; }
        internal Action<TravelTransition>? BeforeDispatch;

        private sealed class Subscription : IDisposable
        {
            private readonly TravelEventsDouble _events;
            private readonly Action<TravelTransition> _callback;
            internal Subscription(TravelEventsDouble events, Action<TravelTransition> callback)
            { _events = events; _callback = callback; }
            public void Dispose() => _events._callbacks.Remove(_callback);
        }

        public IDisposable Subscribe(string owner, Action<TravelTransition> callback)
        {
            Assert.False(string.IsNullOrWhiteSpace(owner));
            _callbacks.Add(callback);
            return new Subscription(this, callback);
        }

        internal void ReplaceSession()
        {
            SessionId = Guid.NewGuid(); _sequence = 0; CurrentLocation = null;
        }

        /// <summary>Publishes a fact for the current session, mirroring the API's
        /// own per-session sequence numbering and current-location bookkeeping.</summary>
        internal TravelTransition Send(TravelTransitionKind kind, TravelMode mode, double gameSeconds,
            TravelLocation? actual = null, TravelLocation? requested = null, Guid? operation = null)
        {
            var fact = new TravelTransition(SessionId!.Value, operation, ++_sequence, kind, mode,
                origin: CurrentLocation, requestedDestination: requested, actualLocation: actual,
                gameSeconds: gameSeconds, dwellSeconds: null);
            if (kind == TravelTransitionKind.Departed) CurrentLocation = null;
            if (kind is TravelTransitionKind.InitialPlacement or TravelTransitionKind.RecoveredPlacement or TravelTransitionKind.Arrived)
                CurrentLocation = actual;
            Dispatch(fact);
            return fact;
        }

        /// <summary>Redelivers an already-published fact verbatim, as a duplicate
        /// or replayed callback would arrive.</summary>
        internal void Redeliver(TravelTransition fact) => Dispatch(fact);

        /// <summary>Publishes a fact owned by a different session; the API drops
        /// these itself, so a consumer must never mutate state for them.</summary>
        internal void SendForeign(TravelTransitionKind kind, TravelMode mode, TravelLocation actual, Guid? operation = null) =>
            Dispatch(new TravelTransition(Guid.NewGuid(), operation, 1, kind, mode, null, null, actual, 42.0, null));

        private void Dispatch(TravelTransition fact)
        {
            IsDispatchingCallbacks = true;
            try
            {
                BeforeDispatch?.Invoke(fact);
                foreach (var callback in _callbacks.ToArray()) callback(fact);
            }
            finally { IsDispatchingCallbacks = false; }
        }
    }

    private static TravelLocation Location(string id, string? name = null, string? poi = "poi") =>
        new(id, poi, name, poi == null ? null : "Dock");

    private static (TravelEventsDouble Events, PersistedBrokerRegistry Registry) Fresh()
        => (new TravelEventsDouble(), new PersistedBrokerRegistry());

    [Fact]
    public void InitialPlacementSeedsCurrentSystemWithoutCountingAVisit()
    {
        var (events, registry) = Fresh();
        using var observer = new SystemVisitObserver(events, registry);

        events.Send(TravelTransitionKind.InitialPlacement, TravelMode.Unknown, 100.0, Location("sys-a", "Alpha"));

        Assert.Empty(registry.VisitedSystems);
        // Seeded identity: arriving back at the placed system is not a new visit.
        events.Send(TravelTransitionKind.Arrived, TravelMode.JumpGate, 200.0, Location("sys-a", "Alpha"), operation: Guid.NewGuid());
        Assert.Empty(registry.VisitedSystems);
    }

    [Fact]
    public void RecoveredPlacementSeedsIdentityAndKeepsRecordedHistory()
    {
        var (events, registry) = Fresh();
        registry.NoteSystemVisit("sys-a", "Alpha", 10.0);
        using var observer = new SystemVisitObserver(events, registry);

        events.Send(TravelTransitionKind.RecoveredPlacement, TravelMode.Unknown, 100.0, Location("sys-a", "Alpha"));

        Assert.Equal(1, registry.VisitedSystems["sys-a"].VisitCount);
        Assert.Equal(10.0, registry.VisitedSystems["sys-a"].LastVisitGameSeconds);
    }

    [Theory]
    [InlineData(TravelMode.JumpGate)]
    [InlineData(TravelMode.Wormhole)]
    public void ArrivalThroughGateOrWormholeRecordsTheActualSystemAtEventTime(TravelMode mode)
    {
        var (events, registry) = Fresh();
        using var observer = new SystemVisitObserver(events, registry);
        events.Send(TravelTransitionKind.InitialPlacement, TravelMode.Unknown, 100.0, Location("sys-a", "Alpha"));

        events.Send(TravelTransitionKind.Arrived, mode, 750.5, Location("sys-b", "Beta"), operation: Guid.NewGuid());

        var visit = registry.VisitedSystems["sys-b"];
        Assert.Equal(1, visit.VisitCount);
        Assert.Equal("Beta", visit.Name);
        Assert.Equal(750.5, visit.FirstVisitGameSeconds);
        Assert.Equal(750.5, visit.LastVisitGameSeconds);
    }

    [Fact]
    public void RedirectedArrivalRecordsActualLocationNotRequestedDestination()
    {
        var (events, registry) = Fresh();
        using var observer = new SystemVisitObserver(events, registry);
        events.Send(TravelTransitionKind.InitialPlacement, TravelMode.Unknown, 1.0, Location("tutorial", "Tutorial"));
        var leg = Guid.NewGuid();

        events.Send(TravelTransitionKind.Requested, TravelMode.JumpGate, 10.0, requested: Location("sys-nominal", "Nominal"), operation: leg);
        events.Send(TravelTransitionKind.Departed, TravelMode.JumpGate, 20.0, operation: leg);
        events.Send(TravelTransitionKind.Arrived, TravelMode.JumpGate, 30.0,
            actual: Location("sys-sandbox", "Sandbox"), requested: Location("sys-nominal", "Nominal"), operation: leg);

        Assert.Equal(new[] { "sys-sandbox" }, registry.VisitedSystems.Keys);
        Assert.Equal(1, registry.VisitedSystems["sys-sandbox"].VisitCount);
    }

    [Fact]
    public void RepeatedAndReplayedArrivalEvidenceCountsOnce()
    {
        var (events, registry) = Fresh();
        using var observer = new SystemVisitObserver(events, registry);
        var leg = Guid.NewGuid();

        var arrival = events.Send(TravelTransitionKind.Arrived, TravelMode.JumpGate, 300.0, Location("sys-b", "Beta"), operation: leg);
        events.Redeliver(arrival);                                                  // same sequence
        events.Send(TravelTransitionKind.Arrived, TravelMode.JumpGate, 400.0, Location("sys-b", "Beta"), operation: leg); // same leg, later sequence

        Assert.Equal(1, registry.VisitedSystems["sys-b"].VisitCount);
        Assert.Equal(300.0, registry.VisitedSystems["sys-b"].LastVisitGameSeconds);
    }

    [Fact]
    public void ReturnTripThroughAnotherSystemCountsBothVisits()
    {
        var (events, registry) = Fresh();
        using var observer = new SystemVisitObserver(events, registry);

        events.Send(TravelTransitionKind.Arrived, TravelMode.JumpGate, 100.0, Location("sys-a", "Alpha"), operation: Guid.NewGuid());
        events.Send(TravelTransitionKind.Arrived, TravelMode.JumpGate, 200.0, Location("sys-b", "Beta"), operation: Guid.NewGuid());
        events.Send(TravelTransitionKind.Arrived, TravelMode.Wormhole, 300.0, Location("sys-a", "Alpha"), operation: Guid.NewGuid());

        Assert.Equal(2, registry.VisitedSystems["sys-a"].VisitCount);
        Assert.Equal(1, registry.VisitedSystems["sys-b"].VisitCount);
        Assert.Equal(100.0, registry.VisitedSystems["sys-a"].FirstVisitGameSeconds);
        Assert.Equal(300.0, registry.VisitedSystems["sys-a"].LastVisitGameSeconds);
    }

    [Fact]
    public void RequestsDeparturesCancellationsRouteCompletionAndInSystemArrivalsNeverCount()
    {
        var (events, registry) = Fresh();
        using var observer = new SystemVisitObserver(events, registry);
        events.Send(TravelTransitionKind.InitialPlacement, TravelMode.Unknown, 10.0, Location("sys-a", "Alpha"));
        var leg = Guid.NewGuid();

        events.Send(TravelTransitionKind.Requested, TravelMode.JumpGate, 20.0, requested: Location("sys-b", "Beta"), operation: leg);
        events.Send(TravelTransitionKind.Departed, TravelMode.JumpGate, 30.0, operation: leg);
        events.Send(TravelTransitionKind.Cancelled, TravelMode.JumpGate, 40.0, operation: leg);
        events.Send(TravelTransitionKind.Arrived, TravelMode.InSystem, 50.0, Location("sys-a", "Alpha", "asteroid"), operation: Guid.NewGuid());
        events.Send(TravelTransitionKind.RouteCompleted, TravelMode.InSystem, 60.0, Location("sys-a", "Alpha", "asteroid"), operation: Guid.NewGuid());

        Assert.Empty(registry.VisitedSystems);
    }

    [Fact]
    public void UnavailableSystemNamePreservesTheStoredLabel()
    {
        var (events, registry) = Fresh();
        using var observer = new SystemVisitObserver(events, registry);

        events.Send(TravelTransitionKind.Arrived, TravelMode.JumpGate, 100.0, Location("sys-a", "Alpha"), operation: Guid.NewGuid());
        events.Send(TravelTransitionKind.Arrived, TravelMode.JumpGate, 200.0, Location("sys-b", "Beta"), operation: Guid.NewGuid());
        events.Send(TravelTransitionKind.Arrived, TravelMode.JumpGate, 300.0, Location("sys-a", name: null), operation: Guid.NewGuid());
        events.Send(TravelTransitionKind.Arrived, TravelMode.JumpGate, 400.0, Location("sys-c", name: null), operation: Guid.NewGuid());

        Assert.Equal("Alpha", registry.VisitedSystems["sys-a"].Name);
        Assert.Equal(2, registry.VisitedSystems["sys-a"].VisitCount);
        // An unnamed first sighting stores no label; it is never resolved lazily.
        Assert.Equal(string.Empty, registry.VisitedSystems["sys-c"].Name);
    }

    [Fact]
    public void ForeignAndStaleSessionEvidenceCannotMutateTheRegistry()
    {
        var (events, registry) = Fresh();
        using var observer = new SystemVisitObserver(events, registry);
        events.Send(TravelTransitionKind.Arrived, TravelMode.JumpGate, 100.0, Location("sys-a", "Alpha"), operation: Guid.NewGuid());
        var stale = events.Send(TravelTransitionKind.Arrived, TravelMode.JumpGate, 200.0, Location("sys-b", "Beta"), operation: Guid.NewGuid());

        events.SendForeign(TravelTransitionKind.Arrived, TravelMode.JumpGate, Location("sys-x", "Foreign"), Guid.NewGuid());
        events.ReplaceSession();
        events.Redeliver(stale);

        Assert.Equal(new[] { "sys-a", "sys-b" }, registry.VisitedSystems.Keys);
        Assert.Equal(1, registry.VisitedSystems["sys-b"].VisitCount);
    }

    [Fact]
    public void ForeignEvidenceDeliveredDuringDispatchCannotMutateTheRegistry()
    {
        var (events, registry) = Fresh();
        using var observer = new SystemVisitObserver(events, registry);
        events.BeforeDispatch = fact =>
        {
            if (fact.Kind != TravelTransitionKind.Arrived) return;
            events.BeforeDispatch = null;
            events.SendForeign(TravelTransitionKind.Arrived, TravelMode.Wormhole, Location("sys-x", "Foreign"), Guid.NewGuid());
        };

        events.Send(TravelTransitionKind.Arrived, TravelMode.JumpGate, 100.0, Location("sys-a", "Alpha"), operation: Guid.NewGuid());

        Assert.Equal(new[] { "sys-a" }, registry.VisitedSystems.Keys);
    }

    [Fact]
    public void SessionReplacementResetsTrackingWithoutTruncatingHistory()
    {
        var (events, registry) = Fresh();
        using var observer = new SystemVisitObserver(events, registry);
        events.Send(TravelTransitionKind.Arrived, TravelMode.JumpGate, 100.0, Location("sys-a", "Alpha"), operation: Guid.NewGuid());

        events.ReplaceSession();
        // A new session starts unlatched: the first arrival counts even in the same system.
        events.Send(TravelTransitionKind.Arrived, TravelMode.JumpGate, 500.0, Location("sys-a", "Alpha"), operation: Guid.NewGuid());

        Assert.Equal(2, registry.VisitedSystems["sys-a"].VisitCount);
        Assert.Equal(100.0, registry.VisitedSystems["sys-a"].FirstVisitGameSeconds);
        Assert.Equal(500.0, registry.VisitedSystems["sys-a"].LastVisitGameSeconds);
    }

    [Fact]
    public void ResetVisitTrackingRebasesOnTheLoadedSlotAndKeepsItsHistory()
    {
        var (events, registry) = Fresh();
        using var observer = new SystemVisitObserver(events, registry);
        events.Send(TravelTransitionKind.Arrived, TravelMode.JumpGate, 100.0, Location("sys-a", "Alpha"), operation: Guid.NewGuid());

        // Slot load: SaveLoadPatch replaces the registry contents, then resets tracking.
        registry.Clear();
        registry.LoadVisitedSystems(new[] { new VisitedSystem("sys-a", "Alpha", 7, 5.0, 50.0) });
        observer.ResetVisitTracking();
        events.Send(TravelTransitionKind.Arrived, TravelMode.JumpGate, 900.0, Location("sys-a", "Alpha"), operation: Guid.NewGuid());

        var visit = registry.VisitedSystems["sys-a"];
        Assert.Equal(8, visit.VisitCount);
        Assert.Equal(5.0, visit.FirstVisitGameSeconds);
        Assert.Equal(900.0, visit.LastVisitGameSeconds);
    }

    [Fact]
    public void DisposedObserverStopsRecordingAndLeavesHistoryIntact()
    {
        var (events, registry) = Fresh();
        var observer = new SystemVisitObserver(events, registry);
        events.Send(TravelTransitionKind.Arrived, TravelMode.JumpGate, 100.0, Location("sys-a", "Alpha"), operation: Guid.NewGuid());

        observer.Dispose();
        events.Send(TravelTransitionKind.Arrived, TravelMode.JumpGate, 200.0, Location("sys-b", "Beta"), operation: Guid.NewGuid());

        Assert.False(observer.IsRecording);
        Assert.Equal(new[] { "sys-a" }, registry.VisitedSystems.Keys);
    }

    [Fact]
    public void ObserverFailureIsLatchedAndReportedOnceWithoutTruncatingHistory()
    {
        var (events, registry) = Fresh();
        var failures = 0;
        using var observer = new SystemVisitObserver(events, registry, _ => failures++);
        events.Send(TravelTransitionKind.Arrived, TravelMode.JumpGate, 100.0, Location("sys-a", "Alpha"), operation: Guid.NewGuid());

        // A malformed callback payload is the only way a pure consumer can throw.
        events.Redeliver(null!);
        events.Redeliver(null!);
        events.Send(TravelTransitionKind.Arrived, TravelMode.JumpGate, 200.0, Location("sys-b", "Beta"), operation: Guid.NewGuid());

        Assert.True(observer.Faulted);
        Assert.False(observer.IsRecording);
        Assert.Equal(1, failures);
        Assert.Equal(new[] { "sys-a" }, registry.VisitedSystems.Keys);
        Assert.Equal(1, registry.VisitedSystems["sys-a"].VisitCount);
    }
}
