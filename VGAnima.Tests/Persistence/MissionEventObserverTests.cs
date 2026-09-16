using System;
using System.Collections.Generic;
using Source.MissionSystem;
using VGAnima.Persistence;
using VGModAPI;
using Xunit;

namespace VGAnima.Tests.Persistence;
public sealed class MissionEventObserverTests
{
    private const string Id = "vganima_llm_test";
    /// <summary>An <see cref="IMissionService"/> double: event-based dispatch
    /// plus the native escape hatch the production observer uses for ownership
    /// correlation. Native mission objects are minted read-only per lookup,
    /// exactly as during real dispatch.</summary>
    private sealed class Events : IMissionService
    {
        private readonly Dictionary<MissionSnapshot, string> _storyIds = new();
        private long _sequence;
        internal Guid Session = Guid.NewGuid();
        public event Action<MissionTransition>? Transitioned;
        public ServiceAvailability Availability => ServiceAvailability.Available;
        public event Action<ServiceAvailability>? AvailabilityChanged { add { } remove { } }
        public IServiceStatus IdentityContinuity { get; } = new StatusOk();
        internal void Send(Guid instance, MissionTransitionKind kind, string id = Id, string? definitionId = null)
        {
            var snapshot = new MissionSnapshot(Session, instance, definitionId ?? id, "mission", Array.Empty<string>(), kind == MissionTransitionKind.Accepted);
            _storyIds[snapshot] = id;
            Transitioned?.Invoke(new MissionTransition(kind, snapshot, ++_sequence));
        }
        /// <summary>Dispatches a snapshot with no resolvable native — the API
        /// refuses attribution rather than trusting the opaque DefinitionId.</summary>
        internal void RaiseUnresolvable(Guid instance, MissionTransitionKind kind, string opaqueDefinitionId = Id)
        {
            var snapshot = new MissionSnapshot(Session, instance, opaqueDefinitionId, "mission", Array.Empty<string>(), false);
            Transitioned?.Invoke(new MissionTransition(kind, snapshot, ++_sequence));
        }
        internal void Malformed() => Transitioned?.Invoke(null!);
        public bool TryGetNative(MissionSnapshot snapshot, out object? native)
        {
            if (_storyIds.TryGetValue(snapshot, out var storyId)) { native = new Mission { storyId = storyId }; return true; }
            native = null;
            return false;
        }
        private sealed class StatusOk : IServiceStatus
        {
            public ServiceAvailability Availability => ServiceAvailability.Available;
            public event Action<ServiceAvailability>? AvailabilityChanged { add { } remove { } }
        }
    }
    private static PersistedEntry Entry() => new(Id, PersistedEntryStates.Offered, null!, new PersistedBroker("seed", "station", null!), new PersistedTimestamps(0, "2026-01-01T00:00:00Z", 0, "2026-01-01T00:00:00Z"));
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ExternalResolutionKeepsDurableIdentityUntilManagedRemovalSucceeds(bool removed)
    {
        var events = new Events(); var registry = new PersistedBrokerRegistry(); registry.Add(Entry());
        int attempts = 0;
        using var observer = new MissionEventObserver(events, registry, retireBar: entry => { attempts++; return removed; });
        var instance = Guid.NewGuid();
        events.Send(instance, MissionTransitionKind.Accepted);
        events.Send(instance, MissionTransitionKind.Completed);
        Assert.Equal(1, attempts);
        if (removed) Assert.Null(registry.Get(Id));
        else
        {
            var persisted = registry.Get(Id)!;
            var reloaded = Newtonsoft.Json.JsonConvert.DeserializeObject<PersistedEntry>(Newtonsoft.Json.JsonConvert.SerializeObject(persisted))!;
            Assert.True(reloaded.BarRetirementPending);
            Assert.Equal("seed", reloaded.Broker.Seed);
            Assert.Equal("station", reloaded.Broker.StationId);
        }
    }

    [Fact]
    public void AcceptanceAndWitnessedOutcomeUpdateOnlyOwnedDefinition()
    {
        var events = new Events(); var registry = new PersistedBrokerRegistry(); registry.Add(Entry());
        using var observer = new MissionEventObserver(events, registry); var instance = Guid.NewGuid();
        events.Send(instance, MissionTransitionKind.Archived); Assert.NotNull(registry.Get(Id));
        events.Send(instance, MissionTransitionKind.Accepted); Assert.Equal(PersistedEntryStates.Accepted, registry.Get(Id)!.State);
        events.Send(Guid.NewGuid(), MissionTransitionKind.Completed); Assert.NotNull(registry.Get(Id));
        events.Send(instance, MissionTransitionKind.Completed); Assert.Null(registry.Get(Id));
    }
    [Fact]
    public void RepeatedLiveInstancesKeepDefinitionUntilLastOutcome()
    {
        var events = new Events(); var registry = new PersistedBrokerRegistry(); registry.Add(Entry());
        using var observer = new MissionEventObserver(events, registry); var a = Guid.NewGuid(); var b = Guid.NewGuid();
        events.Send(a, MissionTransitionKind.Accepted); events.Send(a, MissionTransitionKind.Accepted); events.Send(b, MissionTransitionKind.Accepted);
        events.Send(a, MissionTransitionKind.Failed); events.Send(a, MissionTransitionKind.Removed); Assert.NotNull(registry.Get(Id));
        events.Send(b, MissionTransitionKind.Removed); Assert.Null(registry.Get(Id));
    }
    [Fact]
    public void RestorationDoesNotInventAcceptanceAndSessionChangeDropsOldAssociation()
    {
        var events = new Events(); var registry = new PersistedBrokerRegistry(); registry.Add(Entry());
        using var observer = new MissionEventObserver(events, registry); var a = Guid.NewGuid();
        events.Send(a, MissionTransitionKind.Restored); Assert.Equal(PersistedEntryStates.Offered, registry.Get(Id)!.State);
        events.Session = Guid.NewGuid(); events.Send(a, MissionTransitionKind.Completed); Assert.NotNull(registry.Get(Id));
        var b = Guid.NewGuid(); events.Send(b, MissionTransitionKind.Restored); events.Send(b, MissionTransitionKind.Abandoned); Assert.Null(registry.Get(Id));
    }
    [Fact]
    public void ObserverFailureIsLatchedAndReportedOnce()
    {
        var events = new Events(); var registry = new PersistedBrokerRegistry(); registry.Add(Entry());
        var failures = 0;
        using var observer = new MissionEventObserver(events, registry, _ => failures++);
        events.Malformed(); events.Malformed(); events.Send(Guid.NewGuid(), MissionTransitionKind.Accepted);
        Assert.True(observer.Faulted); Assert.Equal(1, failures);
        Assert.Equal(PersistedEntryStates.Offered, registry.Get(Id)!.State);
    }
    [Fact]
    public void ForeignDefinitionsAndDisposedObserverAreInert()
    {
        var events = new Events(); var registry = new PersistedBrokerRegistry(); registry.Add(Entry());
        var observer = new MissionEventObserver(events, registry);
        events.Send(Guid.NewGuid(), MissionTransitionKind.Accepted, "vanilla"); Assert.Equal(PersistedEntryStates.Offered, registry.Get(Id)!.State);
        observer.Dispose(); events.Send(Guid.NewGuid(), MissionTransitionKind.Accepted); Assert.Equal(PersistedEntryStates.Offered, registry.Get(Id)!.State);
    }
    [Fact]
    public void TransitionsWithoutResolvableNativeAreNeverAttributed()
    {
        // The public DefinitionId is opaque; even carrying our prefix shape,
        // a snapshot whose native cannot be resolved during dispatch must not
        // move the durable registry.
        var events = new Events(); var registry = new PersistedBrokerRegistry(); registry.Add(Entry());
        using var observer = new MissionEventObserver(events, registry);
        events.RaiseUnresolvable(Guid.NewGuid(), MissionTransitionKind.Accepted);
        Assert.Equal(PersistedEntryStates.Offered, registry.Get(Id)!.State);
        events.RaiseUnresolvable(Guid.NewGuid(), MissionTransitionKind.Removed);
        Assert.NotNull(registry.Get(Id));
    }

    [Fact]
    public void OwnershipComesFromTheResolvedNativeStoryIdNotTheDefinitionId()
    {
        // The published DefinitionId is opaque API state; only the native
        // Mission resolved during dispatch carries the Anima storyId. A
        // regression that string-matched DefinitionId (even while still
        // calling TryGetNative) would fail this test.
        var events = new Events(); var registry = new PersistedBrokerRegistry(); registry.Add(Entry());
        using var observer = new MissionEventObserver(events, registry);
        events.Send(Guid.NewGuid(), MissionTransitionKind.Accepted, definitionId: "opaque-42");
        Assert.Equal(PersistedEntryStates.Accepted, registry.Get(Id)!.State);
    }
}
