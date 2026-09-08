using System;
using System.Collections;
using System.Collections.Generic;
using HarmonyLib;
using BepInEx.Logging;
using Source.MissionSystem;
using Source.Util;
using VGAnima.Missions;
using VGAnima.Persistence;
using StoryMissionRegistry = Source.MissionSystem.StoryMission;

namespace VGAnima.Patches;

/// <summary>Harmony prefix on <see cref="SaveGameFile.LoadSaveGame"/>. Runs
/// BEFORE <see cref="SaveGame.LoadState"/> → <c>GamePlayer.FromJson</c> →
/// <c>Mission.FromJson</c>, so our rebuild-from-block factories are
/// registered in <c>StoryMission.allMissions</c> when vanilla's mission
/// list deserialization asks for them.
///
/// <list type="number">
///   <item>Clear in-memory registry + unregister any previously-registered
///     VGAnima factories from vanilla's <c>allMissions</c> (prevents
///     cross-slot leakage when the player loads a different save
///     mid-session).</item>
///   <item>Read the paired sidecar. Missing / corrupt / unsupported-version
///     → empty registry, placeholder-factory safety net (T7) covers the
///     miss case.</item>
///   <item>Register a rebuild-from-block factory for every loaded entry.
///     Vanilla's mission-list deserialization uses its own
///     <c>DataFromJson</c> path for active missions (scout finding #6), so
///     these factories are insurance — they fire only for string-path
///     <c>Mission.FromJson(storyId)</c> calls.</item>
/// </list>
///
/// <para>Wiring: <see cref="Plugin"/> assigns
/// <see cref="Registry"/> / <see cref="Io"/> / <see cref="Log"/> during
/// <c>Awake</c>. When null, the prefix is a no-op.</para>
///
/// <para>Orphan purge runs LATER (first post-load bar refresh, T16) once
/// <c>GamePlayer.current</c> and patron state are populated. The flag
/// <see cref="OrphanPurgePending"/> coordinates that handoff.</para></summary>
[HarmonyPatch(typeof(SaveGameFile), nameof(SaveGameFile.LoadSaveGame))]
internal static class SaveLoadPatch
{
    public static PersistedBrokerRegistry? Registry;
    public static SidecarIO? Io;
    public static ManualLogSource? Log;

    /// <summary>Assigned by <see cref="Plugin"/> only while native travel
    /// events are bound. Its visit tracking is reset here so the first
    /// witnessed arrival after a load records against the slot just loaded.
    /// Null when travel observation is unavailable — the load prefix itself
    /// stays independent of it.</summary>
    public static SystemVisitObserver? VisitObserver;

    /// <summary>IDs we've registered in <c>StoryMission.allMissions</c> so
    /// we can unregister them on a subsequent load. Vanilla has no public
    /// unregister API; we poke the private dict via <see cref="AccessTools"/>.</summary>
    public static readonly HashSet<string> RegisteredStoryIds = new();

    /// <summary>Set when a load just populated the registry from a sidecar.
    /// Consumed + cleared by the first post-load <c>BarRefreshPatches</c>
    /// postfix (T16) when it runs the orphan purge.</summary>
    public static bool OrphanPurgePending;

    /// <summary>Remembered for the <c>ApplicationQuit</c> flush safety net
    /// in <c>Plugin.cs</c>.</summary>
    public static string? LastKnownSavePath;

    [HarmonyPrefix]
    private static void Prefix(SaveGameFile __instance)
    {
        if (Registry is null || Io is null) return;

        try
        {
            // (1) Clear in-memory state + unregister vanilla dict entries
            // for previously-loaded factories.
            UnregisterPreviouslyRegistered();
            Registry.Clear();
            // Take ownership of the new slot before any sidecar IO: the
            // latch must not outlive the history it was latched against.
            // A corrupt/unreadable sidecar leaves an empty visit map, so a
            // surviving latch could suppress the first arrival of the newly
            // loaded slot. Independent of the API's own per-load session
            // reset; this prefix does not rely on it.
            VisitObserver?.ResetVisitTracking();

            // (2) Derive sidecar path from the SaveGameFile instance and
            // read it. Quarantine on corrupt / unsupported version.
            var savePath = __instance.File.FullName;
            var sidecarPath = SidecarPathResolver.From(savePath);
            var result = Io.Read(sidecarPath);

            switch (result.Status)
            {
                case SidecarReadStatus.Loaded:
                    Registry.LoadFrom(result.Schema!.Entries);
                    // Visited-systems field is nullable. v3 sidecars had
                    // it populated; v3→v4 upgrades only drop the
                    // completed-missions field, so visited_systems
                    // survives. Null on hand-edited / empty sidecars.
                    Registry.LoadVisitedSystems(result.Schema.VisitedSystems);
                    Registry.LoadBarReservations(result.Schema.BarReservations);
                    var visitedCount = result.Schema.VisitedSystems?.Length ?? 0;
                    Log?.LogInfo(
                        $"Loaded {result.Schema.Entries.Length} broker entr{(result.Schema.Entries.Length == 1 ? "y" : "ies")} + " +
                        $"{visitedCount} visited from {sidecarPath}");
                    break;
                case SidecarReadStatus.MissingFile:
                    Log?.LogInfo($"No sidecar at {sidecarPath} — starting with empty registry");
                    break;
                case SidecarReadStatus.Corrupted:
                    Log?.LogWarning(
                        $"Sidecar corrupted; quarantined to {result.QuarantinedTo} — empty registry");
                    break;
                case SidecarReadStatus.UnsupportedVersion:
                    Log?.LogWarning(
                        $"Sidecar version too new; quarantined to {result.QuarantinedTo} — empty registry. Check for plugin update.");
                    break;
            }

            // (3) Register rebuild-from-block factories for each loaded entry.
            foreach (var entry in Registry.All())
            {
                try { RegisterFactory(entry); }
                catch (Exception e)
                {
                    Log?.LogWarning(
                        $"Failed to register factory for storyId `{entry.StoryId}`: {e.Message}");
                    // continue — placeholder factory covers the miss case.
                }
            }

            LastKnownSavePath   = savePath;
            OrphanPurgePending  = true;
        }
        catch (Exception e)
        {
            Log?.LogError($"Save-load rehydration failed: {e}");
            // Don't rethrow — vanilla's load must still proceed. Placeholder
            // factory (MissionLookupPatch) catches any resulting orphan.
        }
    }

    private static void RegisterFactory(PersistedEntry entry)
    {
        var block      = entry.MissionBlock;
        var storyId    = entry.StoryId;
        var brokerSeed = entry.Broker.Seed;

        // KNOWN GAP: missionLevel hardcoded to 1 on rehydration. Vanilla
        // serializes ACCEPTED missions as full JsonObjects (scout finding #6),
        // so their reward math is baked in at creation time and this path
        // never runs for them. Only the string-path fallback (offered-state
        // rebuilds, or stale cross-references) hits this factory — reward
        // scaling is wrong in those rare cases. Schema extension to persist
        // the original station level would fix it; tracked for a future pass.
        var storyMission = new StoryMissionRegistry(
            storyId,
            _ => MissionFactoryFromJson.Build(
                block, missionLevel: 1, brokerStation: null, brokerSeed: brokerSeed),
            available: null,
            pickupHint: "VGAnima Broker (rehydrated)");
        StoryMissionRegistry.Add(storyMission);
        RegisteredStoryIds.Add(storyId);
    }

    private static void UnregisterPreviouslyRegistered()
    {
        if (RegisteredStoryIds.Count == 0) return;
        var field = AccessTools.Field(typeof(StoryMissionRegistry), "allMissions");
        if (field?.GetValue(null) is IDictionary all)
        {
            foreach (var id in RegisteredStoryIds) all.Remove(id);
        }
        RegisteredStoryIds.Clear();
    }
}
