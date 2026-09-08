using System;
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
        Assert.False(BrokerReservationRecovery.Retry(registry, pending, _ => revoked++, _ => false));
        Assert.Equal(1, revoked);
        Assert.Contains(registry.BarReservations, row => row.StationId == "station");
        var serialized = JsonConvert.SerializeObject(new SidecarSchema(SidecarSchema.CurrentVersion,
            Array.Empty<PersistedEntry>(), BarReservations: registry.BarReservations.ToArray()));
        var reloaded = JsonConvert.DeserializeObject<SidecarSchema>(serialized)!;
        var fresh = new PersistedBrokerRegistry(); fresh.LoadBarReservations(reloaded.BarReservations);
        var restored = Assert.Single(fresh.BarReservations);
        Assert.True(restored.Aborted);
        Assert.True(BrokerReservationRecovery.Retry(fresh, restored, _ => revoked++, _ => true));
        Assert.Empty(fresh.BarReservations);
        Assert.Equal(2, revoked);
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
