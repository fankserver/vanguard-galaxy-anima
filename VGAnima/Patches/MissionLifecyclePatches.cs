using System;
using HarmonyLib;
using Source.MissionSystem;
using Source.Player;
using VGAnima.Persistence;

namespace VGAnima.Patches;

/// <summary>Wires vanilla mission lifecycle events into the persisted
/// broker registry. Acceptance transitions an entry from
/// <c>offered</c>→<c>accepted</c>; completion / failure / archive /
/// abandon remove the entry. All hooks are guarded by the
/// <c>vganima_llm_</c> storyId prefix — vanilla missions pass through
/// untouched.
///
/// <para>Resolved-mission history lives in VGMissionJournal (read via
/// <see cref="MissionJournal.VgMissionJournalBridge"/>), not VGAnima —
/// MJ-T4 retired the local completed-mission log. The terminal patches
/// here exist only to drop the in-flight entry from the registry so
/// stale offered/accepted rows don't accumulate.</para>
///
/// <para>Postfix (not prefix) on all four: postfix runs only after
/// vanilla's own state change succeeds. A failed acceptance
/// (<c>IsMissionsLimitExceeded</c>) or failed completion wouldn't have
/// updated vanilla's state, so we don't want to update ours either.</para></summary>
internal static class MissionLifecyclePatches
{
    internal const string LlmStoryIdPrefix = "vganima_llm_";

    public static PersistedBrokerRegistry? Registry;

    private static bool IsAuthored(string? storyId) =>
        !string.IsNullOrEmpty(storyId)
        && storyId.StartsWith(LlmStoryIdPrefix, StringComparison.Ordinal);

    /// <summary>Postfix on <see cref="GamePlayer.AddMissionWithLog(Mission)"/>
    /// — the <c>Mission</c> overload. The <c>string</c> overload delegates
    /// to this one internally via <c>StoryMission.Get</c>, so patching just
    /// the <c>Mission</c> overload covers both entry paths.</summary>
    [HarmonyPatch(typeof(GamePlayer), nameof(GamePlayer.AddMissionWithLog), new[] { typeof(Mission) })]
    internal static class OnAcceptPatch
    {
        [HarmonyPostfix]
#pragma warning disable Harmony003
        private static void Postfix(Mission mission)
        {
            var id = mission?.storyId;
            Plugin.Log.LogDebug($"OnAcceptPatch fired (storyId={id ?? "<null>"})");
            if (!IsAuthored(id)) return;
            Registry?.MarkAccepted(id!);
            Plugin.Log.LogDebug($"OnAcceptPatch: marked accepted storyId={id}");
        }
#pragma warning restore Harmony003
    }

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
    /// — fires for "mission resolution complete, archive" path and any
    /// explicit archive calls, but NOT for player-initiated abandon (that
    /// routes through <see cref="OnAbandonPatch"/>).</summary>
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

    /// <summary>Postfix on <see cref="GamePlayer.RemoveMission(Mission, bool)"/>
    /// — vanilla's single funnel for mission removal. The <c>completed</c>
    /// flag distinguishes terminal paths: <c>true</c> routes through
    /// <see cref="OnArchivePatch"/>; <c>false</c> is player-initiated
    /// abandon, handled here.</summary>
    [HarmonyPatch(typeof(GamePlayer), nameof(GamePlayer.RemoveMission),
        new[] { typeof(Mission), typeof(bool) })]
    internal static class OnAbandonPatch
    {
        [HarmonyPostfix]
#pragma warning disable Harmony003
        private static void Postfix(Mission mission, bool completed)
        {
            var id = mission?.storyId;
            Plugin.Log.LogDebug(
                $"OnAbandonPatch fired (storyId={id ?? "<null>"}, completed={completed})");
            if (completed) return;
            if (!IsAuthored(id)) return;
            Registry?.Remove(id!);
            Plugin.Log.LogDebug($"OnAbandonPatch: removed storyId={id}");
        }
#pragma warning restore Harmony003
    }
}
