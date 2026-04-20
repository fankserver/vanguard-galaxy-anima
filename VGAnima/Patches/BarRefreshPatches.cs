using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using HarmonyLib;
using Source.Galaxy.POI.Station;

namespace VGAnima.Patches;

/// <summary>
/// On bar daily-refresh, patrons that roll off need cleanup:
///   1. Remove their injected mission from the station's mission board if still present.
///   2. Drop their warmed TTS audio from VGTTS's cache.
///   3. Drop their registry entry.
///
/// Prefix snapshots <c>availablePatrons</c>; postfix diffs against the new
/// list. HarmonyBefore/After "vgtts" so VGTTS and VGAnima coexist cleanly.
/// </summary>
[HarmonyPatch(typeof(Bar))]
internal static class BarRefreshPatches
{
    private static readonly ConditionalWeakTable<Bar, List<BarPatron>> _snapshots = new();

    [HarmonyPrefix]
    [HarmonyBefore("vgtts")]
    [HarmonyPatch(nameof(Bar.CheckUpdatePatrons))]
    private static void CheckUpdatePatrons_Prefix(Bar __instance)
    {
        _snapshots.AddOrUpdate(__instance, new List<BarPatron>(__instance.availablePatrons));
    }

    [HarmonyPostfix]
    [HarmonyAfter("vgtts")]
    [HarmonyPatch(nameof(Bar.CheckUpdatePatrons))]
    private static void CheckUpdatePatrons_Postfix(Bar __instance)
    {
        try
        {
            if (!_snapshots.TryGetValue(__instance, out var before)) return;
            _snapshots.Remove(__instance);

            if (Plugin.Instance is not { } plugin) return;

            var after = new HashSet<BarPatron>(__instance.availablePatrons);
            foreach (var patron in before)
            {
                if (after.Contains(patron)) continue;
                if (!plugin.Registry.TryGet(patron, out var record)) continue;

                // Remove injected mission from the board if it's still there
                // (player hasn't accepted it and the board hasn't regenerated).
                if (record.Station?.missionBoard != null)
                {
                    record.Station.missionBoard.availableMissions.Remove(record.Mission);
                }

                // Drop VGTTS audio.
                foreach (var (speaker, text) in record.WarmedLines)
                    plugin.Vgtts.DropCache(speaker, text);

                plugin.Registry.Remove(patron);
                Plugin.Log.LogDebug($"[vganima] Evicted rolled-off patron '{patron.name}'");
            }
        }
        catch (Exception ex)
        {
            Plugin.Log.LogError($"[vganima] CheckUpdatePatrons_Postfix threw: {ex}");
        }
    }
}
