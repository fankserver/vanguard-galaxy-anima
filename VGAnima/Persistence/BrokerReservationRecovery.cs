using System;

namespace VGAnima.Persistence;

internal static class BrokerReservationRecovery
{
    internal static bool Retry(PersistedBrokerRegistry registry, BrokerReservation reservation,
        Action<string> revoke, Func<string, bool> remove)
    {
        var entry = registry.FindBySeed(reservation.Seed);
        if (!reservation.Aborted && entry != null && !entry.BarRetirementPending)
        {
            registry.FinishBarReservation(reservation.Seed);
            return true;
        }
        revoke(reservation.Seed);
        if (!remove(reservation.Seed)) return false;
        if (entry != null) registry.Remove(entry.StoryId);
        registry.FinishBarReservation(reservation.Seed);
        return true;
    }
}
