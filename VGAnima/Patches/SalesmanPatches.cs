using System;
using System.Collections.Generic;
using Behaviour.Dialogues;
using Behaviour.Util;
using HarmonyLib;
using Source.Dialogues;
using Source.Galaxy.POI.Station;
using Source.Galaxy.POI.Station.Patrons;

namespace VGAnima.Patches;

/// <summary>
/// Intercepts <see cref="Salesman.InteractWithPatron"/> for converted salesmen.
/// Vanilla posts a <c>DialogueManager.StartDialogue(dialogueLines, () =&gt; ui.ShowSalesmanInfo(this))</c>;
/// for converted patrons we rebuild the call with a no-op onComplete so the
/// salesman's (absent) <c>itemForSale</c> UI never opens. Player reads the
/// pitch, dialogue closes, player walks to mission board.
/// </summary>
[HarmonyPatch(typeof(Salesman))]
internal static class SalesmanPatches
{
    [HarmonyPrefix]
    [HarmonyPatch(nameof(Salesman.InteractWithPatron))]
    private static bool InteractWithPatron_Prefix(Salesman __instance)
    {
        try
        {
            if (Plugin.Instance is not { } plugin) return true;             // let vanilla run
            if (!plugin.Registry.TryGet(__instance, out var record)) return true;  // not ours

            var lines = Traverse.Create(__instance).Field<List<DialogueLine>>("dialogueLines").Value;
            if (lines == null || lines.Count == 0) return true;             // nothing to show; let vanilla

            Singleton<DialogueManager>.Instance.StartDialogue(lines, () => PostMission(record));
            return false;                                                   // skip vanilla method
        }
        catch (Exception ex)
        {
            Plugin.Log.LogError($"[vganima] InteractWithPatron_Prefix threw: {ex}");
            return true;  // fall back to vanilla on error
        }
    }

    /// <summary>Invoked when the player finishes the pitch dialogue. Posts the
    /// mission to the station's board if it isn't already there. Idempotent —
    /// re-clicking the patron after posting does not duplicate.</summary>
    private static void PostMission(Cache.ConversionRecord record)
    {
        try
        {
            var board = record.Station?.missionBoard;
            if (board == null) return;
            if (board.availableMissions.Contains(record.Mission)) return;   // already posted

            board.availableMissions.Add(record.Mission);
            Plugin.Log.LogInfo(
                $"[vganima] Posted mission '{record.Mission.name}' onto board at '{record.Station!.name}'");
        }
        catch (Exception ex)
        {
            Plugin.Log.LogError($"[vganima] PostMission threw: {ex}");
        }
    }
}
