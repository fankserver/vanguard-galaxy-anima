using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Behaviour.UI.Spacestation.Bar;
using Newtonsoft.Json;
using HarmonyLib;
using Source.Dialogues;
using Source.Galaxy.POI;
using Source.Galaxy.POI.Station;
using Source.Galaxy.POI.Station.Patrons;
using VGAnima.Cache;
using VGAnima.Llm;
using VGAnima.Pitch;
using UObject = UnityEngine.Object;

namespace VGAnima.Patches;

/// <summary>
/// Bar-level patches on <see cref="Bar.CheckUpdatePatrons"/>:
///   1. Prefix snapshots the pre-refresh patron roster and pins brokers whose
///      mission is still active (so daily rollover doesn't delete them).
///   2. Postfix re-inserts pinned brokers, evicts rolled-off ones, and kicks
///      off an async broker-injection flow when the player is at the station
///      and BarUI is loaded.
///
/// HarmonyAfter("vgtts") on the postfix so our injected patron isn't caught
/// by VGTTS's own eviction diff.
/// </summary>
[HarmonyPatch(typeof(Bar))]
internal static class BarRefreshPatches
{
    /// <summary>Hard cap so a heavily-populated vanilla bar isn't overfilled.</summary>
    private const int MaxTotalPatrons = 6;

    /// <summary>Seed prefix used to identify VGAnima brokers after save/load.
    /// Vanilla BarPatron.ToJson persists salesman.seed, so this prefix survives
    /// a full game restart.</summary>
    public const string BrokerSeedPrefix = "vganima-broker-";

    /// <summary>Procedural voice defaults — match VGTTS's own defaults so mod
    /// load order doesn't change perceived voice.</summary>
    private const string ProceduralMaleVoice   = "kokoro:12";
    private const string ProceduralFemaleVoice = "kokoro:9";

    private static readonly ConditionalWeakTable<Bar, List<BarPatron>> _snapshots = new();

    /// <summary>Brokers whose assigned storyId is still in flight — snapshotted
    /// in the prefix, restored in the postfix so daily rollover doesn't delete
    /// the quest-giver mid-mission.</summary>
    private static readonly ConditionalWeakTable<Bar, List<BarPatron>> _pinnedBrokers = new();

    [HarmonyPrefix]
    [HarmonyBefore("vgtts")]
    [HarmonyPatch(nameof(Bar.CheckUpdatePatrons))]
    private static void CheckUpdatePatrons_Prefix(Bar __instance)
    {
        _snapshots.AddOrUpdate(__instance, new List<BarPatron>(__instance.availablePatrons));

        if (Plugin.Instance is not { } plugin) return;
        var pinned = new List<BarPatron>();
        foreach (var p in __instance.availablePatrons)
        {
            if (!plugin.Registry.TryGet(p, out var record)) continue;
            if (IsActive(record, plugin.PlayerView)) pinned.Add(p);
        }
        if (pinned.Count > 0) _pinnedBrokers.AddOrUpdate(__instance, pinned);
    }

    [HarmonyPostfix]
    [HarmonyAfter("vgtts")]
    [HarmonyPatch(nameof(Bar.CheckUpdatePatrons))]
    private static void CheckUpdatePatrons_Postfix(Bar __instance)
    {
        try
        {
            // Restore pinned brokers before eviction + injection so idempotency
            // sees them and the rolloff diff doesn't include them.
            if (_pinnedBrokers.TryGetValue(__instance, out var pinned))
            {
                _pinnedBrokers.Remove(__instance);
                foreach (var p in pinned)
                    if (!__instance.availablePatrons.Contains(p))
                        __instance.availablePatrons.Add(p);
            }

            EvictRolledOff(__instance);
            StartInjectMissionBroker(__instance);
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

            foreach (var (speaker, text) in record.WarmedLines)
                plugin.Vgtts.DropCache(speaker, text);

            plugin.Registry.Remove(patron);
            Plugin.Log.LogDebug($"[vganima] Evicted rolled-off broker '{patron.name}' (storyId={record.StoryId})");
        }
    }

    /// <summary>Preflight checks + fire the async LLM → main-thread-continuation
    /// pipeline per spec §11. Returns immediately; the broker appears (or
    /// doesn't) ~2-3s later when the continuation lands.</summary>
    private static void StartInjectMissionBroker(Bar bar)
    {
        if (Plugin.Instance is not { } plugin) return;
        if (!plugin.Cfg.Enabled.Value) return;

        if (plugin.LlmClient == null)
        {
            Plugin.Log.LogDebug("[vganima] LLM disabled; skipping broker injection");
            return;
        }

        if (bar.availablePatrons.Count >= MaxTotalPatrons) return;

        // Idempotent: don't inject twice on re-entry.
        foreach (var p in bar.availablePatrons)
            if (plugin.Registry.TryGet(p, out _)) return;

        var station = Traverse.Create(bar).Field<SpaceStation>("spaceStation").Value;
        if (station == null) return;

        // Only inject when the player is docked at this station.
        if (SpaceStation.current != station) return;

        // Stale saved-broker conflict detection — advise the user to advance
        // in-game time rather than auto-deleting (risk of nuking vanilla patrons).
        var dupSeats = bar.availablePatrons.GroupBy(p => p.seat)
            .Where(g => g.Count() > 1).Select(g => g.Key).ToList();
        var dupNames = bar.availablePatrons.GroupBy(p => p.name)
            .Where(g => g.Count() > 1).Select(g => g.Key).ToList();
        if (dupSeats.Count > 0 || dupNames.Count > 0)
        {
            Plugin.Log.LogWarning(
                $"[vganima] Bar at '{station.name}' has stale duplicates: " +
                $"names=[{string.Join(",", dupNames)}] seats=[{string.Join(",", dupSeats)}]. " +
                $"Likely a broker saved by an older plugin version. " +
                $"Advance in-game time (day-refresh) to clear the bar.");
            return;
        }

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

        // Build the per-bar already-assigned set by scanning ALL current
        // patrons' registry entries — handles the edge case where pinned
        // brokers from the last refresh are still present with their storyIds
        // locked in.
        var alreadyAssigned = new HashSet<string>();
        foreach (var existing in bar.availablePatrons)
            if (plugin.Registry.TryGet(existing, out var existingRecord))
                alreadyAssigned.Add(existingRecord.StoryId);

        // Mint the seed with our stable prefix.
        var brokerIndex = alreadyAssigned.Count;
        var candidateSeed = $"{BrokerSeedPrefix}{station.guid}-{brokerIndex}";

        // Ask the assigner for a storyId.
        var storyId = plugin.Assigner.Assign(candidateSeed, alreadyAssigned, plugin.PlayerView);
        if (storyId == null)
        {
            Plugin.Log.LogDebug(
                $"[vganima] Assigner returned null at '{station.name}' — no storyId available, skipping broker injection");
            return;
        }

        // Create the patron up-front (not added to the bar yet) so we know its
        // name + isMale for the broker section of the LLM context, and can
        // re-use the same object on the main-thread continuation.
        var newPatron = new Salesman(candidateSeed, station);
        newPatron.Initialize();
        Traverse.Create(newPatron).Field<string>("_name").Value = "The Mission Broker";
        newPatron.description = "VGAnimaBroker";

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
        var usedByAny = new HashSet<int>(bar.availablePatrons.Select(p => p.seat));
        var freeSeats = genderSeats.Where(s => !usedByAny.Contains(s)).ToList();
        newPatron.seat = freeSeats.Count > 0 ? freeSeats[0] : genderSeats[0];

        var brokerInfo = new BrokerInfo(
            Name:           newPatron.name,
            IsMale:         newPatron.isMale,
            Seed:           candidateSeed,
            StationFaction: station.faction?.identifier ?? string.Empty);

        LlmContext context;
        try
        {
            context = plugin.Gatherer.Gather(plugin.GameStateView, brokerInfo);
        }
        catch (Exception ex)
        {
            Plugin.Log.LogError($"[vganima] ContextGatherer threw at '{station.name}': {ex}");
            return;
        }

        var contextJson = JsonConvert.SerializeObject(context);
        var systemPrompt = BuildSystemPrompt();
        var userPrompt = BuildUserPrompt(contextJson, brokerInfo, station);

        // Fire and forget. The task continuation hops back to the main thread
        // via the scheduler; if the player leaves the station or the bar tears
        // down in the interim, the continuation no-ops.
        Plugin.Log.LogInfo(
            $"[vganima] Dispatching LLM for broker at '{station.name}' " +
            $"(storyId={storyId}, seed={candidateSeed})");

        _ = DispatchAsync(plugin, bar, station, newPatron, storyId, candidateSeed,
            systemPrompt, userPrompt);
    }

    private static async Task DispatchAsync(
        Plugin plugin, Bar bar, SpaceStation station, Salesman newPatron,
        string storyId, string candidateSeed,
        string systemPrompt, string userPrompt)
    {
        string rawContent;
        try
        {
            rawContent = await plugin.LlmClient!
                .CompleteAsync(systemPrompt, userPrompt, CancellationToken.None)
                .ConfigureAwait(false);
        }
        catch (TaskCanceledException)
        {
            Plugin.Log.LogWarning(
                $"[vganima] LLM timeout after {plugin.Cfg.LlmTimeoutSeconds.Value}s; skipping broker at '{station.name}'");
            return;
        }
        catch (System.Net.Http.HttpRequestException ex)
        {
            Plugin.Log.LogWarning(
                $"[vganima] LLM request failed: {ex.Message}; skipping broker at '{station.name}'");
            return;
        }
        catch (Exception ex)
        {
            Plugin.Log.LogError(
                $"[vganima] LLM call threw; skipping broker at '{station.name}': {ex}");
            return;
        }

        LlmStory story;
        try
        {
            story = plugin.Validator.Parse(rawContent);
        }
        catch (LlmValidationException ex)
        {
            var preview = rawContent.Length > 500 ? rawContent.Substring(0, 500) : rawContent;
            Plugin.Log.LogInfo(
                $"[vganima] LLM response failed validation: {ex.Message}; " +
                $"skipping broker at '{station.name}'. First 500 chars: {preview}");
            return;
        }
        catch (Exception ex)
        {
            Plugin.Log.LogError(
                $"[vganima] Unexpected validation failure; skipping broker at '{station.name}': {ex}");
            return;
        }

        // Hop back to the main thread to touch game state + add the patron.
        plugin.Scheduler.Enqueue(() => FinalizeBrokerInjection(
            plugin, bar, station, newPatron, storyId, candidateSeed, story));
    }

    private static void FinalizeBrokerInjection(
        Plugin plugin, Bar bar, SpaceStation station, Salesman newPatron,
        string storyId, string candidateSeed, LlmStory story)
    {
        try
        {
            // Re-check: player may have left the station between LLM dispatch
            // and continuation (spec §11: "If the bar has closed (player left
            // station), checks SpaceStation.current == station and skips").
            if (SpaceStation.current != station)
            {
                Plugin.Log.LogDebug(
                    $"[vganima] Player left '{station.name}' before LLM returned; dropping broker");
                return;
            }
            if (bar.availablePatrons.Contains(newPatron))
            {
                Plugin.Log.LogDebug("[vganima] Broker already added (race); skipping");
                return;
            }

            // Build dialogueLines from the pitch block so VGTTS's BarPatron.Initialize
            // postfix has text to warm.
            var dialogueLines = new List<DialogueLine>(story.Pitch.Count);
            foreach (var text in story.Pitch)
            {
                if (string.IsNullOrWhiteSpace(text)) continue;
                var character = new Character(newPatron.name).WithPortret(newPatron.icon);
                dialogueLines.Add(DialogueLine.cDL(character, text));
            }
            Traverse.Create(newPatron).Field<List<DialogueLine>>("dialogueLines").Value = dialogueLines;

            // Voice + warm all lines across states.
            var voice = newPatron.isMale ? ProceduralMaleVoice : ProceduralFemaleVoice;
            plugin.Vgtts.RegisterVoice(newPatron.name, voice);
            var warmedPairs = new List<(string Speaker, string Text)>();
            foreach (var line in story.Pitch)   if (!string.IsNullOrWhiteSpace(line)) warmedPairs.Add((newPatron.name, line));
            foreach (var line in story.CheckIn) if (!string.IsNullOrWhiteSpace(line)) warmedPairs.Add((newPatron.name, line));
            foreach (var line in story.Payout)  if (!string.IsNullOrWhiteSpace(line)) warmedPairs.Add((newPatron.name, line));
            _ = Task.Run(async () =>
            {
                foreach (var (speaker, text) in warmedPairs)
                {
                    try { await plugin.Vgtts.WarmCacheAsync(speaker, text, CancellationToken.None); }
                    catch { /* best-effort; live TTS warms again on dialogue open */ }
                }
            });

            plugin.Registry.Register(newPatron, new ConversionRecord(warmedPairs, station, storyId, story));
            bar.availablePatrons.Add(newPatron);

            Plugin.Log.LogInfo(
                $"[vganima] Added LLM-authored broker '{newPatron.name}' to bar at '{station.name}' " +
                $"(seat {newPatron.seat}, isMale={newPatron.isMale}, storyId={storyId}, " +
                $"{bar.availablePatrons.Count} patrons total)");

            var barUI = UObject.FindAnyObjectByType<BarUI>();
            barUI?.RefreshPatrons();
        }
        catch (Exception ex)
        {
            Plugin.Log.LogError($"[vganima] FinalizeBrokerInjection threw: {ex}");
        }
    }

    private static string BuildSystemPrompt()
    {
        // v1 system prompt, verbatim from spec §7 (trimmed whitespace only).
        return
            "You are a writer for bar-broker NPCs in a space-trading game. Your job is to produce\n" +
            "three short dialogue blocks for one specific broker, addressed to the player's captain.\n\n" +
            "The player will tell you about themselves, their ship, where they are, and the broker.\n" +
            "You ONLY output valid JSON matching this schema — no preamble, no markdown fences:\n\n" +
            "{\n" +
            "  \"schema\": \"vganima/story/v1\",\n" +
            "  \"pitch\":    [ /* 3 to 5 short in-character lines pitching a casual job */ ],\n" +
            "  \"check_in\": [ /* 1 to 2 lines for when the captain returns mid-job */ ],\n" +
            "  \"payout\":   [ /* 2 to 4 lines for when the captain turns the job in */ ]\n" +
            "}\n\n" +
            "Rules for every line:\n" +
            "- ASCII only (no em-dashes, smart quotes, or emoji; hyphens and straight apostrophes OK)\n" +
            "- Maximum 120 characters\n" +
            "- Non-empty, no leading/trailing whitespace\n" +
            "- In character for the broker; reference the player's state or the location when it fits naturally\n" +
            "- Do NOT describe a specific mission objective yet — keep the work vague (\"a quick errand\",\n" +
            "  \"a simple survey\"). Mission details come from the game engine, not you.\n\n" +
            "If the player is high-level, the broker should sound respectful. If low-level, more\n" +
            "paternal. If hostile factions overlap with the broker's faction, lean wary. Let the\n" +
            "player's active story arcs and recent archive nudge the tone.";
    }

    private static string BuildUserPrompt(string contextJson, BrokerInfo brokerInfo, SpaceStation station)
    {
        // User prompt per spec §7.
        var gender = brokerInfo.IsMale ? "male" : "female";
        return
            "Player and world context:\n" +
            contextJson + "\n\n" +
            $"Broker to voice: {brokerInfo.Name}, {gender}, at {station.name}, " +
            $"aligned with {brokerInfo.StationFaction}.\n\n" +
            "Produce one JSON object matching the vganima/story/v1 schema.";
    }

    /// <summary>A broker is "active" if its storyId is an active story mission
    /// that hasn't been archived yet (i.e. not Initial and not Done).
    /// Active brokers survive daily bar rollover.</summary>
    private static bool IsActive(ConversionRecord record, IGamePlayerView player)
    {
        if (player.IsArchived(record.StoryId)) return false;
        return player.GetActive(record.StoryId) != null;
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

/// <summary>
/// Prefix on <see cref="BarUI.RefreshPatrons"/> that rebuilds
/// <see cref="ConversionRegistry{TKey,TValue}"/> entries for any
/// seed-prefixed brokers already in the bar — handles the save/load path
/// where the registry is empty but vanilla persisted our brokers.
///
/// After Task 8: a rehydrated broker has no LlmStory (the prior session's
/// one died with the record — spec §9). The prefix fires a fresh async LLM
/// call for each rehydrated broker; if the call fails, the broker is left
/// unregistered and falls through to vanilla ShowSalesmanInfo, as before.
/// </summary>
[HarmonyPatch(typeof(BarUI))]
internal static class RegistryRehydratePatches
{
    [HarmonyPrefix]
    [HarmonyPatch(nameof(BarUI.RefreshPatrons))]
    private static void RefreshPatrons_Prefix()
    {
        try
        {
            if (Plugin.Instance is not { } plugin) return;
            var station = SpaceStation.current;
            if (station?.bar == null) return;

            var bar = station.bar;

            // First pass: discover existing storyIds from already-registered
            // brokers in this bar.
            var alreadyAssigned = new HashSet<string>();
            foreach (var p in bar.availablePatrons)
                if (plugin.Registry.TryGet(p, out var existingRecord))
                    alreadyAssigned.Add(existingRecord.StoryId);

            // Second pass: rebuild records for seed-prefixed brokers NOT yet
            // in the registry. Each gets an async LLM call.
            foreach (var patron in bar.availablePatrons)
            {
                if (patron is not Salesman salesman) continue;
                var seed = salesman.seed;
                if (string.IsNullOrEmpty(seed)) continue;
                if (!seed.StartsWith(BarRefreshPatches.BrokerSeedPrefix)) continue;
                if (plugin.Registry.TryGet(patron, out _)) continue;

                var storyId = plugin.Assigner.Assign(seed, alreadyAssigned, plugin.PlayerView);
                if (storyId == null)
                {
                    Plugin.Log.LogWarning(
                        $"[vganima] Rehydrate: assigner returned null for seed={seed} — " +
                        $"leaving broker unregistered (will fall through to vanilla)");
                    continue;
                }

                // LLM dispatch: only if enabled. Without an LLM client we have
                // no way to fill LlmStory on rehydrate, so the broker falls
                // through to vanilla per spec §12. (Matching the v0.1 behaviour
                // when the LLM call fails.)
                if (plugin.LlmClient == null)
                {
                    Plugin.Log.LogDebug(
                        $"[vganima] Rehydrate: LLM disabled, leaving broker '{patron.name}' unregistered");
                    continue;
                }

                alreadyAssigned.Add(storyId);

                var brokerInfo = new BrokerInfo(
                    Name:           salesman.name,
                    IsMale:         salesman.isMale,
                    Seed:           seed,
                    StationFaction: station.faction?.identifier ?? string.Empty);

                LlmContext context;
                try
                {
                    context = plugin.Gatherer.Gather(plugin.GameStateView, brokerInfo);
                }
                catch (Exception ex)
                {
                    Plugin.Log.LogError(
                        $"[vganima] Rehydrate ContextGatherer threw for '{salesman.name}': {ex}");
                    continue;
                }

                var contextJson = JsonConvert.SerializeObject(context);
                var systemPrompt = BuildRehydrateSystemPrompt();
                var userPrompt = BuildRehydrateUserPrompt(contextJson, brokerInfo, station);

                Plugin.Log.LogInfo(
                    $"[vganima] Rehydrate: dispatching LLM for '{salesman.name}' " +
                    $"at '{station.name}' (seed={seed}, storyId={storyId})");

                _ = RehydrateDispatchAsync(plugin, bar, station, salesman, storyId,
                    systemPrompt, userPrompt);
            }
        }
        catch (Exception ex)
        {
            Plugin.Log.LogError($"[vganima] RefreshPatrons_Prefix (rehydrate) threw: {ex}");
        }
    }

    private static async Task RehydrateDispatchAsync(
        Plugin plugin, Bar bar, SpaceStation station, Salesman patron,
        string storyId, string systemPrompt, string userPrompt)
    {
        string rawContent;
        try
        {
            rawContent = await plugin.LlmClient!
                .CompleteAsync(systemPrompt, userPrompt, CancellationToken.None)
                .ConfigureAwait(false);
        }
        catch (TaskCanceledException)
        {
            Plugin.Log.LogWarning(
                $"[vganima] Rehydrate LLM timeout; leaving broker '{patron.name}' unregistered");
            return;
        }
        catch (System.Net.Http.HttpRequestException ex)
        {
            Plugin.Log.LogWarning(
                $"[vganima] Rehydrate LLM request failed: {ex.Message}; " +
                $"leaving broker '{patron.name}' unregistered");
            return;
        }
        catch (Exception ex)
        {
            Plugin.Log.LogError(
                $"[vganima] Rehydrate LLM call threw for '{patron.name}': {ex}");
            return;
        }

        LlmStory story;
        try
        {
            story = plugin.Validator.Parse(rawContent);
        }
        catch (LlmValidationException ex)
        {
            var preview = rawContent.Length > 500 ? rawContent.Substring(0, 500) : rawContent;
            Plugin.Log.LogInfo(
                $"[vganima] Rehydrate LLM response failed validation: {ex.Message}; " +
                $"leaving broker '{patron.name}' unregistered. First 500 chars: {preview}");
            return;
        }
        catch (Exception ex)
        {
            Plugin.Log.LogError(
                $"[vganima] Rehydrate unexpected validation failure for '{patron.name}': {ex}");
            return;
        }

        plugin.Scheduler.Enqueue(() => FinalizeRehydrate(
            plugin, bar, station, patron, storyId, story));
    }

    private static void FinalizeRehydrate(
        Plugin plugin, Bar bar, SpaceStation station, Salesman patron,
        string storyId, LlmStory story)
    {
        try
        {
            // Double-check the patron is still in the bar + player still here.
            if (!bar.availablePatrons.Contains(patron))
            {
                Plugin.Log.LogDebug(
                    $"[vganima] Rehydrate: patron '{patron.name}' no longer in bar; dropping");
                return;
            }
            if (plugin.Registry.TryGet(patron, out _))
            {
                Plugin.Log.LogDebug(
                    $"[vganima] Rehydrate: patron '{patron.name}' already registered (race); dropping");
                return;
            }

            // Warm all three blocks so subsequent broker-state transitions
            // don't trigger cold-cache TTS latency.
            var warmedPairs = new List<(string Speaker, string Text)>();
            foreach (var line in story.Pitch)   if (!string.IsNullOrWhiteSpace(line)) warmedPairs.Add((patron.name, line));
            foreach (var line in story.CheckIn) if (!string.IsNullOrWhiteSpace(line)) warmedPairs.Add((patron.name, line));
            foreach (var line in story.Payout)  if (!string.IsNullOrWhiteSpace(line)) warmedPairs.Add((patron.name, line));
            _ = Task.Run(async () =>
            {
                foreach (var (speaker, text) in warmedPairs)
                {
                    try { await plugin.Vgtts.WarmCacheAsync(speaker, text, CancellationToken.None); }
                    catch { /* best-effort */ }
                }
            });

            plugin.Registry.Register(patron, new ConversionRecord(warmedPairs, station, storyId, story));

            Plugin.Log.LogInfo(
                $"[vganima] Rehydrated LLM-authored broker '{patron.name}' at '{station.name}' " +
                $"(storyId={storyId})");
        }
        catch (Exception ex)
        {
            Plugin.Log.LogError($"[vganima] FinalizeRehydrate threw: {ex}");
        }
    }

    private static string BuildRehydrateSystemPrompt()
    {
        // Identical to BarRefreshPatches.BuildSystemPrompt — duplicated
        // rather than reached via InternalsVisibleTo so the two classes stay
        // independently modifiable. If the prompt grows, extract to a shared
        // module.
        return
            "You are a writer for bar-broker NPCs in a space-trading game. Your job is to produce\n" +
            "three short dialogue blocks for one specific broker, addressed to the player's captain.\n\n" +
            "The player will tell you about themselves, their ship, where they are, and the broker.\n" +
            "You ONLY output valid JSON matching this schema — no preamble, no markdown fences:\n\n" +
            "{\n" +
            "  \"schema\": \"vganima/story/v1\",\n" +
            "  \"pitch\":    [ /* 3 to 5 short in-character lines pitching a casual job */ ],\n" +
            "  \"check_in\": [ /* 1 to 2 lines for when the captain returns mid-job */ ],\n" +
            "  \"payout\":   [ /* 2 to 4 lines for when the captain turns the job in */ ]\n" +
            "}\n\n" +
            "Rules for every line:\n" +
            "- ASCII only (no em-dashes, smart quotes, or emoji; hyphens and straight apostrophes OK)\n" +
            "- Maximum 120 characters\n" +
            "- Non-empty, no leading/trailing whitespace\n" +
            "- In character for the broker; reference the player's state or the location when it fits naturally\n" +
            "- Do NOT describe a specific mission objective yet — keep the work vague (\"a quick errand\",\n" +
            "  \"a simple survey\"). Mission details come from the game engine, not you.\n\n" +
            "If the player is high-level, the broker should sound respectful. If low-level, more\n" +
            "paternal. If hostile factions overlap with the broker's faction, lean wary. Let the\n" +
            "player's active story arcs and recent archive nudge the tone.";
    }

    private static string BuildRehydrateUserPrompt(string contextJson, BrokerInfo brokerInfo, SpaceStation station)
    {
        var gender = brokerInfo.IsMale ? "male" : "female";
        return
            "Player and world context:\n" +
            contextJson + "\n\n" +
            $"Broker to voice: {brokerInfo.Name}, {gender}, at {station.name}, " +
            $"aligned with {brokerInfo.StationFaction}.\n\n" +
            "Produce one JSON object matching the vganima/story/v1 schema.";
    }
}
