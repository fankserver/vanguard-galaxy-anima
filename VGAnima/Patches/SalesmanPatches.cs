using System;
using System.Collections.Generic;
using Behaviour.Dialogues;
using Behaviour.Util;
using HarmonyLib;
using Source.Dialogues;
using Source.Galaxy.POI.Station.Patrons;
using VGAnima.Cache;
using VGAnima.Pitch;

namespace VGAnima.Patches;

/// <summary>
/// Intercepts <see cref="Salesman.InteractWithPatron"/> for brokers in the
/// registry. Rebuilds dialogue lines on every click from
/// <see cref="BrokerStateDetector"/> so the broker's speech tracks the
/// player's progress on the pitched mission. The onComplete handler is
/// state-specific — only the Initial state posts the mission to the board.
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
            if (Plugin.Instance is not { } plugin) return true;
            if (!plugin.Registry.TryGet(__instance, out var record)) return true;

            var state = BrokerStateDetector.Detect(record);

            var patronCtx = new PatronContext(
                __instance.name, __instance.isMale, record.Station, record.Mission);
            var pitch = plugin.PitchProvider.PitchForState(patronCtx, state);

            var lines = new List<DialogueLine>(pitch.Lines.Count);
            foreach (var text in pitch.Lines)
            {
                if (string.IsNullOrWhiteSpace(text)) continue;
                var character = new Character(__instance.name).WithPortret(__instance.icon);
                lines.Add(DialogueLine.cDL(character, text));
            }
            if (lines.Count == 0) return true;  // nothing to show; fall through to vanilla

            Action onComplete = state switch
            {
                BrokerState.Initial => () => PostMission(record),
                BrokerState.Done    => () => Depart(__instance, record),
                _                   => NoOp,
            };

            Plugin.Log.LogDebug(
                $"[vganima] '{__instance.name}' dialogue state={state}, {lines.Count} line(s)");

            Singleton<DialogueManager>.Instance.StartDialogue(lines, onComplete);
            return false;  // skip vanilla
        }
        catch (Exception ex)
        {
            Plugin.Log.LogError($"[vganima] InteractWithPatron_Prefix threw: {ex}");
            return true;
        }
    }

    /// <summary>Posts the broker's mission to the station board on the first
    /// dialogue close. Idempotent — subsequent calls are no-ops. Also flips
    /// <see cref="ConversionRecord.Pitched"/> so the state machine can tell
    /// Initial apart from Done after the mission fully cycles.</summary>
    private static void PostMission(ConversionRecord record)
    {
        try
        {
            record.Pitched = true;
            var board = record.Station?.missionBoard;
            if (board == null) return;
            if (board.availableMissions.Contains(record.Mission)) return;

            board.availableMissions.Add(record.Mission);
            Plugin.Log.LogInfo(
                $"[vganima] Posted mission '{record.Mission.name}' onto board at '{record.Station!.name}'");
        }
        catch (Exception ex)
        {
            Plugin.Log.LogError($"[vganima] PostMission threw: {ex}");
        }
    }

    /// <summary>Called when the Done-state dialogue closes. Removes the broker
    /// from the bar's roster and drops their TTS cache so session audio doesn't
    /// accumulate. The <see cref="ConversionRecord"/> stays in the registry so
    /// lingering re-clicks before BarUI refreshes route through our prefix
    /// (same Done dialogue) instead of falling through to vanilla
    /// <c>Salesman.ShowSalesmanInfo</c> which would try to sell the random
    /// item vanilla <c>Initialize</c> happened to assign. The registry entry
    /// GCs naturally once BarUI destroys the broker's prefab on its next
    /// <c>RefreshPatrons</c> call (ConditionalWeakTable semantics).</summary>
    private static void Depart(Salesman patron, ConversionRecord record)
    {
        try
        {
            if (Plugin.Instance is not { } plugin) return;

            var bar = record.Station?.bar;
            var removed = bar != null && bar.availablePatrons.Remove(patron);

            foreach (var (speaker, text) in record.WarmedLines)
                plugin.Vgtts.DropCache(speaker, text);

            Plugin.Log.LogInfo(
                $"[vganima] Broker '{patron.name}' departed after mission completion " +
                $"(removed from roster: {removed})");
        }
        catch (Exception ex)
        {
            Plugin.Log.LogError($"[vganima] Depart threw: {ex}");
        }
    }

    private static readonly Action NoOp = static () => { };
}
