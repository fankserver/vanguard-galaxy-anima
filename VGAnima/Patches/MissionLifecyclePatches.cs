using System;
using HarmonyLib;
using Source.MissionSystem;
using Source.Player;
using VGAnima.Persistence;

namespace VGAnima.Patches;

/// <summary>Wires vanilla mission lifecycle events into the persisted
/// broker registry. Acceptance transitions an entry from
/// <c>offered</c>→<c>accepted</c>; completion / failure / archive remove
/// the entry entirely. All hooks are guarded by the
/// <c>vganima_llm_</c> storyId prefix — vanilla missions pass through
/// untouched.
///
/// <para>Targets confirmed in
/// <c>docs/vanilla-reference.md</c> → "Save / Load / Mission-Lifecycle API".
/// Registry is injected by <c>Plugin.Awake</c>; when null (tests or
/// pre-init), every postfix is a no-op.</para>
///
/// <para>Postfix (not prefix) on all four: postfix runs only after
/// vanilla's own state change succeeds. A failed acceptance
/// (<c>IsMissionsLimitExceeded</c>) or failed completion wouldn't have
/// updated vanilla's state, so we don't want to update ours either.</para></summary>
internal static class MissionLifecyclePatches
{
    /// <summary>Shared storyId prefix for authored missions. Must match
    /// what <see cref="VGAnima.Missions.LlmMissionAssigner"/> emits and
    /// what <see cref="MissionLookupPatch"/> recognizes.</summary>
    internal const string LlmStoryIdPrefix = "vganima_llm_";

    /// <summary>In-memory persisted-broker registry. Plugin.Awake sets
    /// this after loading the sidecar. Null until the persistence epic
    /// is fully wired; every postfix treats null as "no-op".</summary>
    public static PersistedBrokerRegistry? Registry;

    private static bool IsAuthored(string? storyId) =>
        !string.IsNullOrEmpty(storyId)
        && storyId.StartsWith(LlmStoryIdPrefix, StringComparison.Ordinal);

    /// <summary>Postfix on <see cref="GamePlayer.AddMissionWithLog(Mission)"/>
    /// — the <c>Mission</c> overload. The <c>string</c> overload delegates
    /// to this one internally via <c>StoryMission.Get</c>, so patching just
    /// the <c>Mission</c> overload covers both entry paths. The explicit
    /// <c>typeof(Mission)</c> in the attribute disambiguates the two overloads.</summary>
    [HarmonyPatch(typeof(GamePlayer), nameof(GamePlayer.AddMissionWithLog), new[] { typeof(Mission) })]
    internal static class OnAcceptPatch
    {
        [HarmonyPostfix]
        // Harmony003 can fire on `mission?.storyId` null-propagation patterns;
        // extract to a local first to keep the analyzer quiet and the guard
        // explicit. No struct mutation — `mission` is a class reference and
        // `storyId` is a public field read.
#pragma warning disable Harmony003
        private static void Postfix(Mission mission)
        {
            var id = mission?.storyId;
            // Unconditional entry-point trace so we can tell the patch fired
            // even for non-VGAnima missions. Diagnoses "patch not attached"
            // vs "patch attached but IsAuthored rejected the id".
            Plugin.Log.LogDebug($"OnAcceptPatch fired (storyId={id ?? "<null>"})");
            if (!IsAuthored(id)) return;
            Registry?.MarkAccepted(id!);
            Plugin.Log.LogDebug($"OnAcceptPatch: marked accepted storyId={id}");
        }
#pragma warning restore Harmony003
    }

    /// <summary>Postfix on <see cref="GamePlayer.CompleteMission(Mission, bool)"/>
    /// — removes the registry entry once vanilla has marked the mission
    /// complete. The string overload (<c>CompleteMission(string name)</c>)
    /// is not patched; it's an alternate entry point used by tutorials and
    /// doesn't apply to our authored missions.</summary>
    [HarmonyPatch(typeof(GamePlayer), nameof(GamePlayer.CompleteMission), new[] { typeof(Mission), typeof(bool) })]
    internal static class OnCompletePatch
    {
        [HarmonyPostfix]
#pragma warning disable Harmony003
        private static void Postfix(Mission m)
        {
            var id = m?.storyId;
            Plugin.Log.LogDebug($"OnCompletePatch fired (storyId={id ?? "<null>"})");
            if (!IsAuthored(id)) return;
            Registry?.Remove(id!);
            Plugin.Log.LogDebug($"OnCompletePatch: removed storyId={id}");
        }
#pragma warning restore Harmony003
    }

    /// <summary>Postfix on <see cref="Mission.MissionFailed(string)"/> —
    /// instance method, so Harmony provides the Mission as
    /// <c>__instance</c>. Removes the registry entry on failure.</summary>
    [HarmonyPatch(typeof(Mission), nameof(Mission.MissionFailed))]
    internal static class OnFailPatch
    {
        [HarmonyPostfix]
#pragma warning disable Harmony003
        private static void Postfix(Mission __instance)
        {
            var id = __instance?.storyId;
            Plugin.Log.LogDebug($"OnFailPatch fired (storyId={id ?? "<null>"})");
            if (!IsAuthored(id)) return;
            Registry?.Remove(id!);
            Plugin.Log.LogDebug($"OnFailPatch: removed storyId={id}");
        }
#pragma warning restore Harmony003
    }

    /// <summary>Postfix on <see cref="GamePlayer.ArchiveMission(string, bool)"/>
    /// — takes the storyId directly, so no <c>Mission</c> indirection
    /// needed. Fires for both the "mission resolution complete, archive"
    /// path and any explicit archive calls.</summary>
    [HarmonyPatch(typeof(GamePlayer), nameof(GamePlayer.ArchiveMission))]
    internal static class OnArchivePatch
    {
        [HarmonyPostfix]
        private static void Postfix(string? id)
        {
            Plugin.Log.LogDebug($"OnArchivePatch fired (storyId={id ?? "<null>"})");
            if (!IsAuthored(id)) return;
            Registry?.Remove(id!);
            Plugin.Log.LogDebug($"OnArchivePatch: removed storyId={id}");
        }
    }
}
