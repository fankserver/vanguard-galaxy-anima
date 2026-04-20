using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Behaviour.UI.Spacestation.Bar;
using HarmonyLib;
using Source.Dialogues;
using Source.Galaxy.POI;
using Source.Galaxy.POI.Station;
using Source.Galaxy.POI.Station.Patrons;
using VGAnima.Cache;
using VGAnima.Missions;
using VGAnima.Pitch;
using UObject = UnityEngine.Object;

namespace VGAnima.Patches;

/// <summary>
/// Bar-level patches on <see cref="Bar.CheckUpdatePatrons"/>:
///   1. Prefix snapshots the pre-refresh patron roster.
///   2. Postfix evicts registry entries + injected missions for patrons that
///      rolled off (diff snapshot vs. new roster).
///   3. Postfix also injects ONE extra mission-broker <see cref="Salesman"/>
///      into the bar when the player is at the station AND <see cref="BarUI"/>
///      is loaded (so we can read the real seatIndex pool for the scene).
///      HarmonyAfter("vgtts") so our injected patron isn't caught by VGTTS's
///      own eviction diff.
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

        // Bar.spaceStation is `private` at runtime (publicizer lies) — Traverse it.
        var station = Traverse.Create(bar).Field<SpaceStation>("spaceStation").Value;
        if (station == null) return;

        // Scope: only inject when the player is docked at this station.
        if (SpaceStation.current != station) return;

        // BarUI is a scene MonoBehaviour loaded with SpacestationInterior. Its
        // `patronSprites` list defines the valid (seatIndex, isMale) pairs the
        // bar scene can render. On game boot we may be called before the
        // interior loads — skip and rely on the re-run when the player opens
        // the bar UI (BarUI.RefreshPatrons invokes CheckUpdatePatrons again).
        var barUI = UObject.FindAnyObjectByType<BarUI>();
        if (barUI == null)
        {
            Plugin.Log.LogDebug("[vganima] BarUI not in scene yet; deferring broker injection");
            return;
        }

        var sprites = Traverse.Create(barUI)
            .Field<List<BarPatronSprite>>("patronSprites")
            .Value;
        if (sprites == null || sprites.Count == 0)
        {
            Plugin.Log.LogWarning("[vganima] BarUI.patronSprites empty; aborting injection");
            return;
        }

        // Per-bar probability roll.
        if (UnityEngine.Random.value > plugin.Cfg.MissionChance.Value)
        {
            Plugin.Log.LogDebug("[vganima] MissionChance roll failed, skipping bar injection");
            return;
        }

        // Create the new patron. Salesman(SpaceStation) ctor sets the protected
        // spaceStation field. Initialize() picks a random variant and sets
        // _name, _isMale, _icon, description, itemForSale, vanilla dialogueLines.
        var newPatron = new Salesman(station);
        newPatron.Initialize();

        // Override the random vanilla name so our broker is visually distinct
        // from any coincidental salesman the seed rolls. _name is public.
        Traverse.Create(newPatron).Field<string>("_name").Value = "The Mission Broker";

        // Pick a seatIndex from patronSprites matching the patron's gender. Prefer
        // one not already used by an existing patron of the same gender (the UI
        // filters sprites by both seat AND isMale, and each seat/gender slot
        // renders one sprite).
        var genderSeats = sprites
            .Where(s => s.isMale == newPatron.isMale)
            .Select(s => s.seatIndex)
            .Distinct()
            .ToList();
        if (genderSeats.Count == 0)
        {
            Plugin.Log.LogWarning($"[vganima] No patronSprites for isMale={newPatron.isMale}; aborting");
            return;
        }
        // Seat maps to a physical stool position in the bar scene. Two patrons
        // at the same seat stack on top of each other regardless of gender, so
        // exclude seats already taken by ANY patron, not just same-gender ones.
        var usedByAny = new HashSet<int>(bar.availablePatrons.Select(p => p.seat));
        var freeSeats = genderSeats.Where(s => !usedByAny.Contains(s)).ToList();
        var chosenByFallback = freeSeats.Count == 0;
        newPatron.seat = chosenByFallback ? genderSeats[0] : freeSeats[0];
        Plugin.Log.LogInfo(
            $"[vganima] Seat pick: genderSeats=[{string.Join(",", genderSeats)}]  " +
            $"usedByAny=[{string.Join(",", usedByAny)}]  free=[{string.Join(",", freeSeats)}]  " +
            $"chose={newPatron.seat}{(chosenByFallback ? " (fallback!)" : "")}");

        // Diagnostic: dump the seat/sprite state so we can verify the choice
        // against what BarUI actually renders. Remove once the broker reliably
        // shows up in-game.
        var maleSeats = sprites.Where(s => s.isMale).Select(s => s.seatIndex).Distinct().OrderBy(x => x).ToArray();
        var femaleSeats = sprites.Where(s => !s.isMale).Select(s => s.seatIndex).Distinct().OrderBy(x => x).ToArray();
        var existing = string.Join(", ", bar.availablePatrons.Select(p => $"{p.name}/seat{p.seat}/M={p.isMale}"));
        Plugin.Log.LogInfo(
            $"[vganima] patronSprites: male seats=[{string.Join(",", maleSeats)}]  female seats=[{string.Join(",", femaleSeats)}]");
        Plugin.Log.LogInfo($"[vganima] existing patrons: [{existing}]");

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
            $"(seat {newPatron.seat}, isMale={newPatron.isMale}, {bar.availablePatrons.Count} patrons total)");

        // Verify: the broker should now be in the list.
        var postRoster = string.Join(", ", bar.availablePatrons
            .Select((p, i) => $"[{i}] {p.name}/seat{p.seat}/M={p.isMale}"));
        Plugin.Log.LogInfo($"[vganima] Post-inject bar.availablePatrons: {postRoster}");
    }
}

/// <summary>
/// Debug patches on <see cref="BarUI.RefreshPatrons"/> and
/// <see cref="BarPatronImage.SetPatronSprite"/> to see what the UI actually
/// instantiates vs. what's in the patron list. Remove once the broker reliably
/// renders in the bar.
/// </summary>
[HarmonyPatch(typeof(BarUI))]
internal static class BarUIDebugPatches
{
    [HarmonyPostfix]
    [HarmonyPatch(nameof(BarUI.RefreshPatrons))]
    private static void RefreshPatrons_Postfix()
    {
        try
        {
            var station = SpaceStation.current;
            if (station?.bar == null) { Plugin.Log.LogInfo("[vganima] BarUI.RefreshPatrons ran with no current station"); return; }
            var roster = string.Join(", ", station.bar.availablePatrons
                .Select((p, i) => $"[{i}] {p.name}/seat{p.seat}/M={p.isMale}"));
            Plugin.Log.LogInfo($"[vganima] BarUI.RefreshPatrons finished — list: {roster}");
        }
        catch (Exception ex) { Plugin.Log.LogError($"[vganima] RefreshPatrons_Postfix threw: {ex}"); }
    }
}

[HarmonyPatch(typeof(BarPatronImage))]
internal static class BarPatronImageDebugPatches
{
    [HarmonyPostfix]
    [HarmonyPatch(nameof(BarPatronImage.SetPatronData))]
    private static void SetPatronData_Postfix(BarPatron patron)
    {
        try
        {
            Plugin.Log.LogInfo(
                $"[vganima] BarUI instantiated prefab for: {patron?.name}/seat{patron?.seat}/M={patron?.isMale}");
        }
        catch (Exception ex) { Plugin.Log.LogError($"[vganima] SetPatronData_Postfix threw: {ex}"); }
    }

    [HarmonyPostfix]
    [HarmonyPatch(nameof(BarPatronImage.SetPatronSprite))]
    private static void SetPatronSprite_Postfix(BarPatronImage __instance)
    {
        try
        {
            var patron = Traverse.Create(__instance).Field<BarPatron>("patron").Value;
            Plugin.Log.LogInfo(
                $"[vganima] SetPatronSprite invoked for: {patron?.name}/seat{patron?.seat}/M={patron?.isMale}");
        }
        catch (Exception ex) { Plugin.Log.LogError($"[vganima] SetPatronSprite_Postfix threw: {ex}"); }
    }
}
