using System;
using VGAnima.Persistence;
using VGModAPI;
using Xunit;

namespace VGAnima.Tests.Persistence;
public sealed class MissionEventObserverTests
{
    private const string Id = "vganima_llm_test";
    private sealed class Events : IMissionEvents, IDisposable
    {
        private Action<MissionTransition>? _receive;
        private long _sequence;
        internal Guid Session = Guid.NewGuid();
        public IDisposable Subscribe(string owner, Action<MissionTransition> callback) { _receive = callback; return this; }
        internal void Send(Guid instance, MissionTransitionKind kind, string id = Id) => _receive?.Invoke(new MissionTransition(kind,
            new MissionSnapshot(Session, instance, id, "mission", Array.Empty<string>(), kind == MissionTransitionKind.Accepted), ++_sequence));
        internal void Malformed() => _receive?.Invoke(null!);
        public void Dispose() => _receive = null;
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
}
