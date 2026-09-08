using System;
using System.Collections.Generic;
using Behaviour.Dialogues;
using Behaviour.UI.Spacestation.Bar;
using Behaviour.Util;
using HarmonyLib;
using Source.Dialogues;
using Source.Galaxy.POI.Station.Patrons;
using Source.MissionSystem;
using Source.Player;
using VGAnima.Cache;
using VGAnima.Pitch;
using UObject = UnityEngine.Object;

namespace VGAnima.Patches;

/// <summary>
/// Intercepts <see cref="Salesman.InteractWithPatron"/> for Salesmen in our
/// registry (i.e. the converted brokers). Dispatches on <see cref="BrokerState"/>
/// derived from the broker's assigned storyId, builds a dialogue list with
/// state-specific lines + completion triggers, and hands it to vanilla's
/// <c>DialogueManager.StartDialogue</c>.
///
/// Lifecycle wiring per state:
///   Initial      → onComplete: GamePlayer.AddMissionWithLog(storyId)
///   InProgress   → onComplete: (none)
///   ReadyToClaim → WithTrigger on second-to-last line: CompleteMission(mission);
///                  onComplete: Depart(salesman, record)
///   Done         → onComplete: Depart(salesman, record)
/// </summary>
[HarmonyPatch(typeof(Salesman))]
internal static class SalesmanPatches
{
    // Managed API contacts invoke authored dialogue through the retained Anima record;
    // an absent record must never fall through to an unrelated native sale.
    internal static void InteractOwned(Salesman patron) => _ = InteractWithPatron_Prefix(patron);

    [HarmonyPrefix]
    [HarmonyPatch(nameof(Salesman.InteractWithPatron))]
    private static bool InteractWithPatron_Prefix(Salesman __instance)
    {
        try
        {
            if (Plugin.Instance is not { } plugin) return true;
            if (!plugin.Registry.TryGet(__instance, out var record)) return true;

            var state = BrokerStateDetector.Detect(record.StoryId, plugin.PlayerView);

            // For ReadyToClaim we need the live Mission instance to pass to
            // CompleteMission. Fetch it here so the closure below captures the
            // same reference the dialogue is advertising.
            Mission? activeMission = state == BrokerState.ReadyToClaim
                ? plugin.PlayerView.GetActive(record.StoryId)
                : null;

            // A ReadyToClaim click after the mission has already been completed
            // by some other path (board UI, auto-complete, whatever) will have
            // activeMission == null. Fall through to vanilla in that rare case.
            if (state == BrokerState.ReadyToClaim && activeMission == null)
            {
                Plugin.Log.LogDebug(
                    $"'{__instance.name}' ReadyToClaim but no active mission for " +
                    $"storyId={record.StoryId}; falling through");
                return true;
            }

            var patronCtx = new PatronContext(
                __instance.name, __instance.isMale, record.Station, activeMission!);
            var pitch = plugin.PitchProvider.PitchForState(patronCtx, state);

            // Build DialogueLine list. No per-line WithTrigger hooks — the vanilla
            // DialogueLine.trigger field is only fired AFTER the dialogue's
            // onComplete (observed in game 2026-04-20: CompleteMission logged
            // after Depart ran when the trigger was attached mid-line). Chaining
            // CompleteMission → Depart in onComplete is the order we need so the
            // mission archives BEFORE the BarUI.RefreshPatrons side-effect inside
            // Depart re-enters InjectMissionBroker (otherwise the assigner still
            // sees the storyId as available and spawns a fresh broker in the same
            // seat — the double-click-to-leave bug).
            var lines = new List<DialogueLine>(pitch.Lines.Count);
            var nonBlank = new List<string>();
            foreach (var t in pitch.Lines)
                if (!string.IsNullOrWhiteSpace(t)) nonBlank.Add(t);
            if (nonBlank.Count == 0) return true;

            foreach (var text in nonBlank)
            {
                var character = new Character(__instance.name).WithPortret(__instance.icon);
                lines.Add(DialogueLine.cDL(character, text));
            }

            var capturedMission = activeMission;
            Action onComplete = state switch
            {
                BrokerState.Initial      => () => AddMission(record.StoryId),
                BrokerState.InProgress   => NoOp,
                BrokerState.ReadyToClaim => () =>
                {
                    // Order matters — archive first so Depart's RefreshPatrons
                    // doesn't trip InjectMissionBroker with the same storyId.
                    CompleteMission(capturedMission!);
                    Depart(__instance, record);
                },
                BrokerState.Done         => () => Depart(__instance, record),
                _                        => NoOp,
            };

            Plugin.Log.LogDebug(
                $"'{__instance.name}' state={state} storyId={record.StoryId} lines={lines.Count}");

            Singleton<DialogueManager>.Instance.StartDialogue(lines, onComplete);
            return false;
        }
        catch (Exception ex)
        {
            Plugin.Log.LogError($"InteractWithPatron_Prefix threw: {ex}");
            return true;
        }
    }

    /// <summary>Hands the storyId to the vanilla factory. Mirrors
    /// <c>SideMissions.CreatePatrolDialogue</c>'s onComplete.</summary>
    private static void AddMission(string storyId)
    {
        try
        {
            var player = GamePlayer.current;
            if (player == null)
            {
                Plugin.Log.LogWarning($"AddMission: GamePlayer.current is null (storyId={storyId})");
                return;
            }
            player.AddMissionWithLog(storyId);
            Plugin.Log.LogInfo($"AddMissionWithLog({storyId}) dispatched via broker");
        }
        catch (Exception ex)
        {
            Plugin.Log.LogError($"AddMission threw: {ex}");
        }
    }

    /// <summary>Triggers the vanilla mission completion — pays rewards,
    /// archives the storyId, removes from the active list.</summary>
    private static void CompleteMission(Mission mission)
    {
        try
        {
            var player = GamePlayer.current;
            if (player == null)
            {
                Plugin.Log.LogWarning($"CompleteMission: GamePlayer.current is null (mission={mission?.name})");
                return;
            }
            player.CompleteMission(mission);
            Plugin.Log.LogInfo($"CompleteMission('{mission?.name}') fired via broker");
        }
        catch (Exception ex)
        {
            Plugin.Log.LogError($"CompleteMission threw: {ex}");
        }
    }

    /// <summary>Removes the broker from the bar roster, drops warmed TTS
    /// lines from the cache, and triggers a BarUI refresh so the scene no
    /// longer renders the broker's stool sprite.</summary>
    private static void Depart(Salesman patron, ConversionRecord record)
    {
        try
        {
            if (Plugin.Instance is not { } plugin) return;

            var bar = record.Station?.bar;
            var removed = plugin.ManagedBarsSelected
                ? plugin.ManagedBars?.Remove(patron.seed) == true
                : bar != null && bar.availablePatrons.Remove(patron);
            if (plugin.ManagedBarsSelected && !removed)
            {
                Plugin.Log.LogWarning("Managed broker retirement refused; keeping its behavior until removal is safe.");
                return;
            }
            plugin.Registry.Remove(patron);

            foreach (var (speaker, text) in record.WarmedLines)
                plugin.Vgtts.DropCache(speaker, text);

            Plugin.Log.LogInfo(
                $"Broker '{patron.name}' departed after storyId={record.StoryId} " +
                $"resolved (removed from roster: {removed})");

            var barUI = UObject.FindAnyObjectByType<BarUI>();
            barUI?.RefreshPatrons();
        }
        catch (Exception ex)
        {
            Plugin.Log.LogError($"Depart threw: {ex}");
        }
    }

    private static readonly Action NoOp = static () => { };
}
