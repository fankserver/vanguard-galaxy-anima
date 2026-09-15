using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using VGAnima.Persistence;
using Xunit;

namespace VGAnima.Tests.Persistence;

public sealed class BrokerReservationRecoveryTests
{
    [Fact]
    public void RefusedRollbackRevokesBehaviorAndKeepsStationQuarantinedAcrossSerialization()
    {
        var registry = new PersistedBrokerRegistry();
        var pending = new BrokerReservation("seed", "station", Aborted: true);
        registry.ReserveBar(pending);
        int revoked = 0;
        // A refused removal must NOT withdraw the declaration: the contact
        // stays tracked so the next reconciliation retries the proven removal.
        Assert.False(BrokerReservationRecovery.Retry(registry, pending, _ => revoked++, _ => false));
        Assert.Equal(0, revoked);
        Assert.Contains(registry.BarReservations, row => row.StationId == "station");
        var serialized = JsonConvert.SerializeObject(new SidecarSchema(SidecarSchema.CurrentVersion,
            Array.Empty<PersistedEntry>(), BarReservations: registry.BarReservations.ToArray()));
        var reloaded = JsonConvert.DeserializeObject<SidecarSchema>(serialized)!;
        var fresh = new PersistedBrokerRegistry(); fresh.LoadBarReservations(reloaded.BarReservations);
        var restored = Assert.Single(fresh.BarReservations);
        Assert.True(restored.Aborted);
        Assert.True(BrokerReservationRecovery.Retry(fresh, restored, _ => revoked++, _ => true));
        Assert.Empty(fresh.BarReservations);
        Assert.Equal(1, revoked);
    }

    [Fact]
    public void RemovalIsProvenAgainstTheLiveRosterBeforeTheDeclarationIsWithdrawn()
    {
        // Mirrors production coupling: `remove` proves absence against the
        // live patron before dropping the tracked contact; `revoke` is the
        // after-the-fact fallback. Revoking first would let `remove` trivially
        // report "never declared" and leave a placed patron visible with a
        // dead handle.
        var registry = new PersistedBrokerRegistry();
        registry.Add(new PersistedEntry("story", VGAnima.Persistence.PersistedEntryStates.Offered, null!,
            new PersistedBroker("seed", "station", null!), new PersistedTimestamps(0, "2026-01-01T00:00:00Z", 0, "2026-01-01T00:00:00Z"),
            BarRetirementPending: true));
        var pending = new BrokerReservation("seed", "station");
        registry.ReserveBar(pending);

        var tracked = new HashSet<string> { "seed" };
        var placed = true;
        var proofs = 0;
        bool Remove(string seed) { proofs++; return !placed; }
        void Revoke(string seed) => tracked.Remove(seed);

        // Patron still placed: refusal keeps the contact tracked (retryable).
        Assert.False(BrokerReservationRecovery.Retry(registry, pending, Revoke, Remove));
        Assert.Equal(1, proofs);
        Assert.Contains("seed", tracked);
        Assert.Single(registry.BarReservations);
        Assert.NotNull(registry.FindBySeed("seed"));

        // The API has hidden the patron; recovery proves absence, then withdraws.
        placed = false;
        Assert.True(BrokerReservationRecovery.Retry(registry, pending, Revoke, Remove));
        Assert.Equal(2, proofs);
        Assert.DoesNotContain("seed", tracked);
        Assert.Empty(registry.BarReservations);
        Assert.Null(registry.FindBySeed("seed"));
    }

    [Fact]
    public void ReservationWithoutAssignedNarrativeStillNeedsConfirmedRemoval()
    {
        var registry = new PersistedBrokerRegistry(); var pending = new BrokerReservation("seed", "station");
        registry.ReserveBar(pending);
        Assert.False(BrokerReservationRecovery.Retry(registry, pending, _ => { }, _ => false));
        Assert.Single(registry.BarReservations);
    }
}
