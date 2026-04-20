using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using HarmonyLib;
using Source.Dialogues;
using Source.Galaxy.POI;
using Source.Galaxy.POI.Station;
using Source.Galaxy.POI.Station.Patrons;
using VGAnima.Cache;
using VGAnima.Missions;
using VGAnima.Pitch;

namespace VGAnima.Patches;

/// <summary>
/// Bar-level patches on <see cref="Bar.CheckUpdatePatrons"/>:
///   1. Prefix snapshots the pre-refresh patron roster.
///   2. Postfix evicts registry entries + injected missions for patrons that
///      rolled off (diff snapshot vs. new roster).
///   3. Postfix also injects ONE extra mission-broker <see cref="Salesman"/>
///      into the bar when there's room — alongside vanilla patrons, never
///      replacing one. HarmonyAfter("vgtts") so our injected patron isn't
///      caught by VGTTS's own eviction diff.
/// </summary>
[HarmonyPatch(typeof(Bar))]
internal static class BarRefreshPatches
{
    /// <summary>Hard cap so a heavily-populated vanilla bar isn't overfilled.
    /// Typical bars produce 2–5 patrons; we add at most 1 extra.</summary>
    private const int MaxTotalPatrons = 6;

    /// <summary>Procedural voice defaults — match VGTTS's own defaults so mod
    /// load order doesn't change perceived voice.</summary>
    private const string ProceduralMaleVoice   = "kokoro:12";
    private const string ProceduralFemaleVoice = "kokoro:9";

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
            EvictRolledOff(__instance);
            InjectMissionBroker(__instance);
        }
        catch (Exception ex)
        {
            Plugin.Log.LogError($"[vganima] CheckUpdatePatrons_Postfix threw: {ex}");
        }
    }

    private static void EvictRolledOff(Bar bar)
    {
        if (!_snapshots.TryGetValue(bar, out var before)) return;
        _snapshots.Remove(bar);

        if (Plugin.Instance is not { } plugin) return;

        var after = new HashSet<BarPatron>(bar.availablePatrons);
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

            foreach (var (speaker, text) in record.WarmedLines)
                plugin.Vgtts.DropCache(speaker, text);

            plugin.Registry.Remove(patron);
            Plugin.Log.LogDebug($"[vganima] Evicted rolled-off patron '{patron.name}'");
        }
    }

    private static void InjectMissionBroker(Bar bar)
    {
        if (Plugin.Instance is not { } plugin) return;
        if (!plugin.Cfg.Enabled.Value) return;

        // Hard cap on total patrons (vanilla typically produces 2–5).
        if (bar.availablePatrons.Count >= MaxTotalPatrons) return;

        // Idempotent: don't inject twice on re-entry to the same bar.
        foreach (var p in bar.availablePatrons)
            if (plugin.Registry.TryGet(p, out _)) return;

        // Per-bar probability roll.
        if (UnityEngine.Random.value > plugin.Cfg.MissionChance.Value)
        {
            Plugin.Log.LogDebug("[vganima] MissionChance roll failed, skipping bar injection");
            return;
        }

        // Bar.spaceStation is `private` at runtime (publicizer lies) — Traverse it.
        var station = Traverse.Create(bar).Field<SpaceStation>("spaceStation").Value;
        if (station == null) return;

        // Create the new patron. Salesman(SpaceStation) ctor sets the protected
        // spaceStation field. Assign a seat index that isn't already taken.
        var newPatron = new Salesman(station);
        var usedSeats = new HashSet<int>(bar.availablePatrons.Select(p => p.seat));
        var seat = 0;
        while (usedSeats.Contains(seat)) seat++;
        newPatron.seat = seat;

        // Vanilla init picks a random salesman variant — sets _name, _isMale,
        // _icon, description, itemForSale, and vanilla dialogueLines. We only
        // need the name/icon for the character portrait; we overwrite
        // dialogueLines with our pitch below.
        newPatron.Initialize();

        // Generate the real game mission.
        var ctx = new MissionContext(station, station.level, newPatron);
        var mission = plugin.MissionSource.Generate(ctx);
        if (mission == null)
        {
            Plugin.Log.LogDebug($"[vganima] MissionSource declined; aborting injection at '{station.name}'");
            return;  // Discard the new patron; not added to bar.
        }

        mission.name = $"[VGA] {mission.name}";

        // Build pitch + override dialogueLines.
        var patronCtx = new PatronContext(newPatron.name, newPatron.isMale, station, mission);
        var pitch = plugin.PitchProvider.Pitch(patronCtx);
        var dialogueLines = new List<DialogueLine>(pitch.Lines.Count);
        foreach (var text in pitch.Lines)
        {
            if (string.IsNullOrWhiteSpace(text)) continue;
            var character = new Character(newPatron.name).WithPortret(newPatron.icon);
            dialogueLines.Add(DialogueLine.cDL(character, text));
        }
        Traverse.Create(newPatron).Field<List<DialogueLine>>("dialogueLines").Value = dialogueLines;

        // VGTTS: register voice + warm pitch lines in the background.
        var voice = newPatron.isMale ? ProceduralMaleVoice : ProceduralFemaleVoice;
        plugin.Vgtts.RegisterVoice(newPatron.name, voice);
        var warmedPairs = pitch.Lines
            .Select(text => (Speaker: newPatron.name, Text: text))
            .ToList();
        _ = Task.Run(async () =>
        {
            foreach (var (speaker, text) in warmedPairs)
            {
                try { await plugin.Vgtts.WarmCacheAsync(speaker, text, CancellationToken.None); }
                catch { /* best-effort; live TTS warms again on dialogue open */ }
            }
        });

        // Record for SalesmanPatches routing + future rolloff eviction.
        plugin.Registry.Register(newPatron, new ConversionRecord(mission, warmedPairs, station));

        // Add to the bar's roster.
        bar.availablePatrons.Add(newPatron);

        Plugin.Log.LogInfo(
            $"[vganima] Added mission broker '{newPatron.name}' to bar at '{station.name}' " +
            $"(seat {seat}, {bar.availablePatrons.Count} patrons total)");
    }
}
