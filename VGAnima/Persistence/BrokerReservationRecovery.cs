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
        // Removal is proven FIRST: the production `remove` proves absence
        // against the live roster before dropping the contact declaration.
        // Revoking first would let `remove` trivially report "never declared"
        // and leave an already-placed patron visible with a dead handle.
        // `revoke` afterwards is the idempotent fallback for seeds that were
        // never declared at all. A refusal keeps the contact tracked so the
        // next reconciliation retries the proven removal.
        if (!remove(reservation.Seed)) return false;
        revoke(reservation.Seed);
        if (entry != null) registry.Remove(entry.StoryId);
        registry.FinishBarReservation(reservation.Seed);
        return true;
    }
}
