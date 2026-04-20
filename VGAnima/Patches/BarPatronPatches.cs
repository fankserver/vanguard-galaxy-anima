using System;
using System.Collections.Generic;
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
/// Converts eligible <see cref="Salesman"/> patrons into mission-pitchers on
/// <see cref="BarPatron.Initialize"/>. Priority.First so our dialogue
/// replacement lands before VGTTS's postfix reads and warms the text.
/// </summary>
[HarmonyPatch(typeof(BarPatron))]
internal static class BarPatronPatches
{
    // Procedural voice defaults — same Kokoro SIDs VGTTS's own BarPatronPatches
    // pick, so mod-ordering doesn't change what the player hears.
    private const string ProceduralMaleVoice   = "kokoro:12";
    private const string ProceduralFemaleVoice = "kokoro:9";

    [HarmonyPostfix]
    [HarmonyPriority(Priority.First)]
    [HarmonyPatch(nameof(BarPatron.Initialize))]
    private static void Initialize_Postfix(BarPatron __instance)
    {
        try
        {
            InitializeSafe(__instance);
        }
        catch (Exception ex)
        {
            Plugin.Log.LogError($"[vganima] Initialize_Postfix threw: {ex}");
        }
    }

    private static void InitializeSafe(BarPatron patron)
    {
        if (patron is not Salesman salesman) return;             // v0.1: only salesmen
        if (Plugin.Instance is not { } plugin) return;
        if (!plugin.Cfg.Enabled.Value) return;

        // Idempotent: skip if we've already converted this patron.
        if (plugin.Registry.TryGet(salesman, out _)) return;

        // Config gate: per-salesman probability roll.
        if (UnityEngine.Random.value > plugin.Cfg.MissionChance.Value)
        {
            Plugin.Log.LogDebug($"[vganima] Salesman '{salesman.name}' — MissionChance roll failed, leaving vanilla");
            return;
        }

        var station = salesman.spaceStation;
        if (station == null) return;

        // 1. Generate the real game mission.
        var ctx = new MissionContext(station, station.level, salesman);
        var mission = plugin.MissionSource.Generate(ctx);
        if (mission == null)
        {
            Plugin.Log.LogDebug($"[vganima] MissionSource declined for station '{station.name}'");
            return;
        }

        // 2. Inject into the station's mission board (unconditional Add — the
        // next timer-based RegenerateMissions clears the list anyway).
        if (station.missionBoard != null)
        {
            station.missionBoard.availableMissions.Add(mission);
            Plugin.Log.LogInfo($"[vganima] Injected mission '{mission.name}' onto board at '{station.name}'");
        }

        // 3. Build pitch lines.
        var patronCtx = new PatronContext(salesman.name, salesman.isMale, station, mission);
        var pitch = plugin.PitchProvider.Pitch(patronCtx);

        // 4. Replace dialogueLines via Traverse (private field on Salesman).
        var lines = pitch.Lines;
        var dialogueLines = new List<DialogueLine>(lines.Count);
        foreach (var text in lines)
        {
            if (string.IsNullOrWhiteSpace(text)) continue;
            // Mirror Salesman.SalesmanSpaceShipPNG IL: construct a Character
            // from the salesman name + icon, then use DialogueLine.cDL factory.
            var character = new Character(salesman.name).WithPortret(salesman.icon);
            dialogueLines.Add(DialogueLine.cDL(character, text));
        }

        Traverse.Create(salesman).Field<List<DialogueLine>>("dialogueLines").Value = dialogueLines;

        // 5. Register voice with VGTTS and warm cache in the background.
        var voice = salesman.isMale ? ProceduralMaleVoice : ProceduralFemaleVoice;
        plugin.Vgtts.RegisterVoice(salesman.name, voice);

        var warmedPairs = new List<(string Speaker, string Text)>();
        foreach (var text in lines) warmedPairs.Add((salesman.name, text));

        _ = Task.Run(async () =>
        {
            foreach (var (speaker, text) in warmedPairs)
            {
                try { await plugin.Vgtts.WarmCacheAsync(speaker, text, CancellationToken.None); }
                catch { /* best-effort; live TTS warms again on dialogue open */ }
            }
        });

        // 6. Record in registry so InteractWithPatron_Prefix knows we own this
        // patron and so BarRefreshPatches can evict on roll-off.
        plugin.Registry.Register(salesman, new ConversionRecord(mission, warmedPairs, station));

        Plugin.Log.LogInfo($"[vganima] Converted salesman '{salesman.name}' — {lines.Count} pitch lines, voice={voice}");
    }
}
