using System;
using BepInEx.Logging;
using Behaviour.Managers;
using HarmonyLib;
using Source.Galaxy.POI;
using Source.Player;
using VGAnima.Persistence;

namespace VGAnima.Patches;

/// <summary>Harmony prefix on <see cref="TravelManager"/>.<c>JumpToSystem</c>
/// (protected <c>IEnumerator</c>, decomp line 107514). Fires once per
/// jumpgate travel before the coroutine yields, giving us access to the
/// destination <see cref="JumpGate.targetSystem"/> without having to hook
/// post-yield scene-load completion or patch the state-machine's
/// <c>MoveNext</c>.
///
/// <para>Why JumpToSystem specifically: it's the <b>one</b> vanilla
/// method every real gameplay jumpgate travel path funnels through. The
/// other <c>currentSystem</c> write-sites in the decomp are either
/// world-gen initial placement (pre-sidecar, runs once on new-game) or
/// debug teleport (<c>CheatManager.Teleport</c> — not real gameplay).
/// Covering JumpToSystem gets ~100% of real player-initiated system
/// transitions.</para>
///
/// <para>Latch: a process-scoped <c>_lastLatchedGuid</c> dedups no-op
/// transitions. In practice a jumpgate jump always goes to a different
/// system so the guard is defensive — but the latch also avoids
/// double-recording if vanilla ever calls <c>JumpToSystem</c> twice for
/// one logical travel (e.g. tutorial→sandbox transition rewrites
/// <c>targetSystem</c> inside the coroutine at decomp line 107537). The
/// latch resets on save-load via <see cref="ResetLatch"/> so a fresh
/// session doesn't inherit the prior slot's last-system.</para>
///
/// <para>Wiring: <see cref="Plugin"/> assigns <see cref="Registry"/>,
/// <see cref="Clock"/>, and <see cref="Log"/> during <c>Awake</c>. When
/// null, the prefix is a no-op — never blocks vanilla.</para></summary>
[HarmonyPatch(typeof(TravelManager), "JumpToSystem", new[] { typeof(JumpGate) })]
internal static class SystemEntryPatch
{
    public static PersistedBrokerRegistry? Registry;
    public static IClock?                  Clock;
    public static ManualLogSource?         Log;

    /// <summary>Most recent system guid recorded by the patch, used for
    /// the transition latch. Empty at plugin load and after
    /// <see cref="ResetLatch"/>. Intentionally static: process-scoped
    /// state is correct here — every save-load triggers
    /// <see cref="ResetLatch"/> via <see cref="SaveLoadPatch"/>, and a
    /// stale latch across sessions would merely under-count (not
    /// corrupt). Internal visibility is for
    /// <c>VGAnima.Tests</c> assertions.</summary>
    internal static string _lastLatchedGuid = string.Empty;

    [HarmonyPrefix]
    private static void Prefix(JumpGate jumpGatePoi)
    {
        if (Registry is null) return;
        if (jumpGatePoi?.targetSystem is null) return;

        try
        {
            var target      = jumpGatePoi.targetSystem;
            var guid        = target.guid  ?? string.Empty;
            var name        = target.name  ?? string.Empty;
            var gameSeconds = Clock?.GameSeconds ?? GamePlayer.current?.elapsedTime ?? 0.0;

            var newLatch = SystemVisitRecorder.RecordVisitIfTransitioned(
                Registry, _lastLatchedGuid, guid, name, gameSeconds);

            if (newLatch != _lastLatchedGuid)
            {
                var visitCount = Registry.VisitedSystems.TryGetValue(guid, out var v)
                    ? v.VisitCount : 0;
                Log?.LogDebug(
                    $"SystemEntry: {name} (guid={guid}, visitCount={visitCount})");
                _lastLatchedGuid = newLatch;
            }
        }
        catch (Exception e)
        {
            // Never rethrow from a Harmony prefix — vanilla's travel coroutine
            // must proceed. A failed visit-recording is a journal gap, not
            // a game crash.
            Log?.LogError($"SystemEntryPatch prefix failed: {e}");
        }
    }

    /// <summary>Clears the transition latch. Called by
    /// <see cref="SaveLoadPatch"/> so the first jumpgate post-load
    /// always records, regardless of which system the player happens to
    /// be in when the save was made.</summary>
    public static void ResetLatch() => _lastLatchedGuid = string.Empty;
}

/// <summary>Pure latch-and-record logic split out of
/// <see cref="SystemEntryPatch"/> so it can be unit-tested without
/// instantiating vanilla types. The patch itself delegates here after
/// the Harmony boilerplate extracts the primitive values.</summary>
internal static class SystemVisitRecorder
{
    /// <summary>Records a visit to <paramref name="arrivingGuid"/> iff
    /// it differs from <paramref name="currentLatchedGuid"/>. Returns
    /// the guid that should now be considered latched: either the
    /// unchanged <paramref name="currentLatchedGuid"/> (skipped) or
    /// <paramref name="arrivingGuid"/> (recorded).
    ///
    /// <para>Empty / null <paramref name="arrivingGuid"/> is treated as
    /// "unknown system" and returns the existing latch unchanged — we
    /// can't record a visit without a stable identifier.</para></summary>
    public static string RecordVisitIfTransitioned(
        PersistedBrokerRegistry registry,
        string currentLatchedGuid,
        string arrivingGuid,
        string arrivingName,
        double gameSeconds)
    {
        if (string.IsNullOrEmpty(arrivingGuid)) return currentLatchedGuid;
        if (arrivingGuid == currentLatchedGuid) return currentLatchedGuid;

        registry.NoteSystemVisit(arrivingGuid, arrivingName, gameSeconds);
        return arrivingGuid;
    }
}
