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
using VGAnima.Missions;
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
            Plugin.Log.LogError($"CheckUpdatePatrons_Postfix threw: {ex}");
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
            Plugin.Log.LogDebug($"Evicted rolled-off broker '{patron.name}' (storyId={record.StoryId})");
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
            Plugin.Log.LogDebug("LLM disabled; skipping broker injection");
            return;
        }

        var stationForDebug = Traverse.Create(bar).Field<SpaceStation>("spaceStation").Value;
        var stationNameForDebug = stationForDebug?.name ?? "<unknown>";
        Plugin.Log.LogDebug(
            $"StartInjectMissionBroker evaluating bar at '{stationNameForDebug}' " +
            $"(patrons={bar.availablePatrons.Count}/{MaxTotalPatrons})");

        if (bar.availablePatrons.Count >= MaxTotalPatrons)
        {
            Plugin.Log.LogDebug(
                $"Bar at '{stationNameForDebug}' already at MaxTotalPatrons " +
                $"({MaxTotalPatrons}); no injection");
            return;
        }

        // Idempotent: don't inject twice on re-entry.
        foreach (var p in bar.availablePatrons)
        {
            if (plugin.Registry.TryGet(p, out var existing))
            {
                Plugin.Log.LogDebug(
                    $"Broker already present at '{stationNameForDebug}' " +
                    $"(storyId={existing.StoryId}); skipping re-injection");
                return;
            }
        }

        var station = stationForDebug;
        if (station == null)
        {
            Plugin.Log.LogDebug("Bar has no spaceStation field; skipping");
            return;
        }

        // Only inject when the player is docked at this station.
        if (SpaceStation.current != station)
        {
            Plugin.Log.LogDebug(
                $"Player not docked at '{station.name}' " +
                $"(SpaceStation.current={SpaceStation.current?.name ?? "<null>"}); skipping");
            return;
        }

        // Stale saved-broker conflict detection — advise the user to advance
        // in-game time rather than auto-deleting (risk of nuking vanilla patrons).
        var dupSeats = bar.availablePatrons.GroupBy(p => p.seat)
            .Where(g => g.Count() > 1).Select(g => g.Key).ToList();
        var dupNames = bar.availablePatrons.GroupBy(p => p.name)
            .Where(g => g.Count() > 1).Select(g => g.Key).ToList();
        if (dupSeats.Count > 0 || dupNames.Count > 0)
        {
            Plugin.Log.LogWarning(
                $"Bar at '{station.name}' has stale duplicates: " +
                $"names=[{string.Join(",", dupNames)}] seats=[{string.Join(",", dupSeats)}]. " +
                $"Likely a broker saved by an older plugin version. " +
                $"Advance in-game time (day-refresh) to clear the bar.");
            return;
        }

        var barUI = UObject.FindAnyObjectByType<BarUI>();
        if (barUI == null)
        {
            Plugin.Log.LogDebug("BarUI not in scene yet; deferring broker injection");
            return;
        }

        var sprites = Traverse.Create(barUI)
            .Field<List<BarPatronSprite>>("patronSprites")
            .Value;
        if (sprites == null || sprites.Count == 0)
        {
            Plugin.Log.LogWarning("BarUI.patronSprites empty; aborting injection");
            return;
        }

        // Per-bar probability roll.
        var roll = UnityEngine.Random.value;
        if (roll > plugin.Cfg.MissionChance.Value)
        {
            Plugin.Log.LogDebug(
                $"MissionChance roll {roll:F2} > {plugin.Cfg.MissionChance.Value:F2} " +
                $"at '{station.name}'; no broker this refresh");
            return;
        }
        Plugin.Log.LogDebug(
            $"MissionChance roll {roll:F2} <= {plugin.Cfg.MissionChance.Value:F2} " +
            $"at '{station.name}'; proceeding with injection");

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

        // v2-mission: no pre-flight storyId decision. The LLM authors the
        // mission, and LlmMissionAssigner registers it after validation.

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
            Plugin.Log.LogWarning($"No patronSprites for isMale={newPatron.isMale}; aborting");
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
            Plugin.Log.LogError($"ContextGatherer threw at '{station.name}': {ex}");
            return;
        }

        var contextJson = JsonConvert.SerializeObject(context);
        var systemPrompt = BuildSystemPrompt();
        var userPrompt = BuildUserPrompt(contextJson, brokerInfo, station);

        Plugin.Log.LogDebug(
            $"Prompts built for '{station.name}' " +
            $"(system={systemPrompt.Length}, user={userPrompt.Length}, context={contextJson.Length} chars)");

        // Fire and forget. The task continuation hops back to the main thread
        // via the scheduler; if the player leaves the station or the bar tears
        // down in the interim, the continuation no-ops.
        Plugin.Log.LogInfo(
            $"Dispatching LLM for broker at '{station.name}' " +
            $"(seed={candidateSeed}, timeout={plugin.Cfg.LlmTimeoutSeconds.Value}s)");

        _ = DispatchAsync(plugin, bar, station, newPatron, candidateSeed,
            systemPrompt, userPrompt);
    }

    private static async Task DispatchAsync(
        Plugin plugin, Bar bar, SpaceStation station, Salesman newPatron,
        string candidateSeed,
        string systemPrompt, string userPrompt)
    {
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        string rawContent;
        try
        {
            rawContent = await plugin.LlmClient!
                .CompleteAsync(systemPrompt, userPrompt, CancellationToken.None)
                .ConfigureAwait(false);
            stopwatch.Stop();
            Plugin.Log.LogDebug(
                $"LLM response received for '{station.name}' in {stopwatch.ElapsedMilliseconds}ms " +
                $"({rawContent.Length} chars)");
        }
        catch (TaskCanceledException)
        {
            stopwatch.Stop();
            Plugin.Log.LogWarning(
                $"LLM timeout after {stopwatch.ElapsedMilliseconds}ms " +
                $"(limit={plugin.Cfg.LlmTimeoutSeconds.Value}s); skipping broker at '{station.name}'");
            return;
        }
        catch (System.Net.Http.HttpRequestException ex)
        {
            stopwatch.Stop();
            Plugin.Log.LogWarning(
                $"LLM request failed after {stopwatch.ElapsedMilliseconds}ms: {ex.Message}; " +
                $"skipping broker at '{station.name}'");
            return;
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            Plugin.Log.LogError(
                $"LLM call threw after {stopwatch.ElapsedMilliseconds}ms; " +
                $"skipping broker at '{station.name}': {ex}");
            return;
        }

        LlmStory story;
        try
        {
            // Re-gather the hostility context so the validator can reject
            // kill-ally missions. Uses the view directly (no cached copy)
            // so the check races against actual current state — acceptable
            // because reputation changes slowly and the cost is < 1ms.
            IReadOnlyList<string>? atWar = null;
            IReadOnlyDictionary<string, int>? reputation = null;
            try
            {
                atWar      = plugin.GameStateView.AtWar;
                reputation = plugin.GameStateView.Reputation;
            }
            catch (Exception gatherEx)
            {
                Plugin.Log.LogWarning(
                    $"hostility context gather failed: {gatherEx.Message}; " +
                    $"proceeding without enemy_faction cross-check");
            }

            story = plugin.Validator.Parse(rawContent, atWar, reputation);
        }
        catch (LlmValidationException ex)
        {
            Plugin.Log.LogInfo(
                $"LLM response failed validation: {ex.Message}; " +
                $"skipping broker at '{station.name}'.\n" +
                $"---- system prompt ----\n{systemPrompt}\n" +
                $"---- user prompt ----\n{userPrompt}\n" +
                $"---- raw response ----\n{rawContent}\n" +
                $"---- end ----");
            return;
        }
        catch (Exception ex)
        {
            Plugin.Log.LogError(
                $"Unexpected validation failure; skipping broker at '{station.name}': {ex}\n" +
                $"---- system prompt ----\n{systemPrompt}\n" +
                $"---- user prompt ----\n{userPrompt}\n" +
                $"---- raw response ----\n{rawContent}\n" +
                $"---- end ----");
            return;
        }

        Plugin.Log.LogDebug(
            $"Validation passed for '{station.name}'; " +
            $"enqueueing main-thread finalize (mission={story.Mission?.Name ?? "<legacy>"})\n" +
            $"---- system prompt ----\n{systemPrompt}\n" +
            $"---- user prompt ----\n{userPrompt}\n" +
            $"---- raw response ----\n{rawContent}\n" +
            $"---- end ----");

        // Hop back to the main thread to touch game state + add the patron.
        plugin.Scheduler.Enqueue(() => FinalizeBrokerInjection(
            plugin, bar, station, newPatron, candidateSeed, story));
    }

    private static void FinalizeBrokerInjection(
        Plugin plugin, Bar bar, SpaceStation station, Salesman newPatron,
        string candidateSeed, LlmStory story)
    {
        try
        {
            Plugin.Log.LogDebug(
                $"FinalizeBrokerInjection running on main thread for '{station.name}'");
            // Re-check: player may have left the station between LLM dispatch
            // and continuation (spec §11: "If the bar has closed (player left
            // station), checks SpaceStation.current == station and skips").
            if (SpaceStation.current != station)
            {
                Plugin.Log.LogDebug(
                    $"Player left '{station.name}' before LLM returned; dropping broker");
                return;
            }
            if (bar.availablePatrons.Contains(newPatron))
            {
                Plugin.Log.LogDebug("Broker already added (race); skipping");
                return;
            }

            // Build + register the mission if the LLM returned a v2 mission block.
            // Legacy fallback: if story.Mission is null (LLM returned the old
            // dialogue-only schema), pitch the legacy TestStoryMissions mission
            // so early-testing saves still work.
            string storyId;
            if (story.Mission != null)
            {
                try
                {
                    // Area-anchored reward scaling: pass the station's level so
                    // rewards match the local zone, not the player's character
                    // level. Mirrors vanilla MissionGenerator. See
                    // MissionFactoryFromJson.Build XML docs for the why.
                    var missionLevel = station.level;
                    storyId = plugin.MissionAssigner.Assign(
                        story.Mission, missionLevel, station, candidateSeed);
                    Plugin.Log.LogInfo(
                        $"LLM-authored mission '{story.Mission.Name}' registered " +
                        $"with storyId={storyId} (missionLevel={missionLevel})");
                }
                catch (Exception ex)
                {
                    Plugin.Log.LogError(
                        $"MissionFactoryFromJson threw for '{story.Mission.Name}'; " +
                        $"skipping broker at '{station.name}': {ex}");
                    return;
                }
            }
            else
            {
#pragma warning disable CS0618
                storyId = TestStoryMissions.JobsiteSurveyId;
#pragma warning restore CS0618
                Plugin.Log.LogWarning(
                    $"LLM returned v1 dialogue-only schema; falling back to legacy " +
                    $"'{storyId}' for broker at '{station.name}'");
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
                $"Added LLM-authored broker '{newPatron.name}' to bar at '{station.name}' " +
                $"(seat {newPatron.seat}, isMale={newPatron.isMale}, storyId={storyId}, " +
                $"{bar.availablePatrons.Count} patrons total)");

            var barUI = UObject.FindAnyObjectByType<BarUI>();
            barUI?.RefreshPatrons();
        }
        catch (Exception ex)
        {
            Plugin.Log.LogError($"FinalizeBrokerInjection threw: {ex}");
        }
    }

    private static string BuildSystemPrompt()
    {
        // v2-mission system prompt per spec §8.
        return
            "You are a writer for bar-broker NPCs in a space-trading game. Your job is to\n" +
            "produce ONE dialogue-and-mission JSON object the broker will offer the player.\n\n" +
            "You ONLY output valid JSON matching this schema - no preamble, no markdown fences:\n\n" +
            "{\n" +
            "  \"schema\": \"vganima/mission/v1\",\n" +
            "  \"pitch\":    [ /* 3..5 short in-character lines pitching the job */ ],\n" +
            "  \"check_in\": [ /* 1..2 lines for when the captain returns mid-job */ ],\n" +
            "  \"payout\":   [ /* 2..4 lines for when the captain turns the job in */ ],\n" +
            "  \"mission\": {\n" +
            "    \"name\":            /* <=60 chars */,\n" +
            "    \"description\":     /* <=500 chars */,\n" +
            "    \"completion_text\": /* <=200 chars */,\n" +
            "    \"source_faction\":  /* one of: Marauders PoliceGuild BountyGuild\n" +
            "                          TradingGuild MiningGuild IndustrialGuild SalvageGuild\n" +
            "                          Stranded MercenaryGuild Smugglers Darkspacers Puppeteers\n" +
            "                          Fanatics HolyRadicals Amalgam Gold Red Blue */,\n" +
            "    \"steps\": [ /* 1..3 steps, each with 1..2 objectives */\n" +
            "      { \"objectives\": [ /* objective objects */ ] } ],\n" +
            "    \"rewards\": [ /* 1..5 reward objects */ ]\n" +
            "  }\n" +
            "}\n\n" +
            "OBJECTIVE TYPES (each objective object has a `type` plus fields):\n" +
            "  { \"type\": \"KillEnemies\",\n" +
            "    \"enemy_faction\":   <faction from list above>,\n" +
            "    \"required_amount\": 1..5,\n" +
            "    \"description\":     <<=120 chars> }\n" +
            "  { \"type\": \"ProtectUnit\",\n" +
            "    \"protect_text\":    <<=120 chars> }\n" +
            "  { \"type\": \"TriggerObjective\",\n" +
            "    \"trigger\":         one of [DockedWithSpaceStation, ArrivedAtSpaceStation, MoveToArea],\n" +
            "    \"required_amount\": 1..3,\n" +
            "    \"description\":     <<=120 chars> }\n" +
            "  { \"type\": \"CollectItemTypes\",\n" +
            "    \"item_category\":   one of [Ore, Salvage, RefinedProduct, TradeGoods, Junk],\n" +
            "    \"required_amount\": 1..50,\n" +
            "    \"description\":     <<=120 chars> }\n" +
            "  { \"type\": \"ClearPoi\",\n" +
            "    \"enemy_faction\":   <hostile faction from list above>,\n" +
            "    \"description\":     <<=120 chars> }\n" +
            "      Spawns a dedicated combat zone on the system map. Use ONLY when the\n" +
            "      mission is genuinely 'go fight at a specific place.' When you DO pick\n" +
            "      combat, prefer ClearPoi over KillEnemies; required_amount is auto-\n" +
            "      computed from the spawn, so do not specify one.\n\n" +
            "REWARD TYPES:\n" +
            "  { \"type\": \"Credits\",    \"base_value\": 15..100 }\n" +
            "  { \"type\": \"Experience\", \"base_value\": 30..100 }\n" +
            "  { \"type\": \"Reputation\", \"faction\": <faction>, \"amount\": -500..500 }\n\n" +
            "RULES FOR EVERY DIALOGUE LINE:\n" +
            "- ASCII only (no em-dashes, smart quotes, or emoji; hyphens and straight apostrophes OK)\n" +
            "- Maximum 120 characters\n" +
            "- Non-empty, no leading/trailing whitespace\n" +
            "- In character for the broker; reference the player's state or the location when it fits\n\n" +
            "COHERENCE RULES:\n" +
            "- MISSION ARCHETYPE — context.mission_guidance has a pre-computed ranked weights\n" +
            "  dict derived from the player's specialization, titles, cargo, active missions,\n" +
            "  station facilities, ship state, and faction state. The top-ranked archetype is\n" +
            "  the default pick. Deviate to a lower-ranked one ONLY if the context makes the\n" +
            "  top pick a bad fit (rare). Do NOT pick an archetype listed in\n" +
            "  mission_guidance.forbidden_archetypes — those are impossible given the context\n" +
            "  (e.g. combat forbidden when no hostile faction exists).\n" +
            "  The five archetypes map to these objective types:\n" +
            "    * combat  → ClearPoi (preferred) or KillEnemies. Requires a hostile faction.\n" +
            "    * gather  → CollectItemTypes with item_category Ore or RefinedProduct.\n" +
            "    * salvage → CollectItemTypes with item_category Salvage or Junk.\n" +
            "    * deliver → TriggerObjective (Docked/Arrived/MoveToArea), optionally paired\n" +
            "                with CollectItemTypes TradeGoods for a 'haul + unload' shape.\n" +
            "    * escort  → ProtectUnit + TriggerObjective travel to destination.\n" +
            "  mission_guidance.rationale lists the signals that drove the weights — use it as\n" +
            "  flavor material (if it mentions @GatlingAmmo, the pitch can reference gunnery).\n" +
            "- Dialogue and mission must match: if the pitch promises a rescue, include ProtectUnit\n" +
            "  or KillEnemies, not a lone CollectItemTypes.\n" +
            "- AT MOST ONE COMBAT OBJECTIVE PER STEP. Do NOT put ClearPoi and KillEnemies into\n" +
            "  the same step. ClearPoi auto-completes when its spawned zone is cleared; KillEnemies\n" +
            "  counts ANY kill of that faction anywhere — mixing them creates a 'main mission done,\n" +
            "  stragglers still pending' shape that's awkward. Pick one combat verb per step.\n" +
            "- FACTION NAMING: the context exposes each faction under its identifier (JSON key)\n" +
            "  with a display_name, relation (friendly|neutral|hostile), and reputation value.\n" +
            "    * In dialogue lines (pitch / check_in / payout), ALWAYS use the display_name.\n" +
            "    * In mission block fields (source_faction, enemy_faction, reward faction),\n" +
            "      ALWAYS use the identifier (the JSON key).\n" +
            "  Example: dialogue says \"the Corsair Syndicate is harassing us\" but the mission\n" +
            "  block sets enemy_faction=\"Marauders\".\n" +
            "- source_faction is the hiring broker's faction. Only pick an identifier whose\n" +
            "  relation in context.factions is \"friendly\" — disliked or hostile gilds would\n" +
            "  not hire the player.\n" +
            "- For KillEnemies, enemy_faction MUST be an identifier whose relation is \"hostile\".\n" +
            "  Do NOT target friendly or neutral factions, even if narratively fitting. If no\n" +
            "  faction has relation=\"hostile\", omit KillEnemies entirely and use other\n" +
            "  objective types.\n" +
            "- At least one objective across all steps must NOT be ProtectUnit (a mission of pure\n" +
            "  protect is degenerate).\n" +
            "- Reward base_values MUST stay within context.reward_clamps. The schema block above\n" +
            "  lists the same ranges; the context repeats them so you cannot miss them.\n" +
            "- TYPICAL REWARD MAGNITUDES (match vanilla's mission-board feel — the formula\n" +
            "  scales base_values by missionLevel internally, so do NOT inflate bases for\n" +
            "  higher-level stations):\n" +
            "    * Deliver / fetch / courier only:      credits 20-30, xp 40-55.\n" +
            "    * Mine / salvage / collect:            credits 25-40, xp 45-60.\n" +
            "    * Kill / escort / protect / clear:     credits 35-50, xp 55-75.\n" +
            "    * Hazardous multi-objective mission:   credits 60-100, xp 75-100.\n" +
            "    * Reputation amount: 200-350 standard; up to 500 for faction-defining favors.\n" +
            "      Even simple gather / deliver jobs pay 200-300 — DO NOT emit positive amounts\n" +
            "      below 150. Vanilla's procedural floor is 200; going lower feels like an insult.\n" +
            "  STEP COUNT IS NARRATIVE STRUCTURE, NOT A REWARD MULTIPLIER. Vanilla's\n" +
            "  single-step and multi-step missions pay the same when the objective\n" +
            "  archetype matches — pick base_value by archetype, not by step count.\n\n" +
            "Reply with ONLY the JSON object.";
    }

    private static string BuildUserPrompt(string contextJson, BrokerInfo brokerInfo, SpaceStation station)
    {
        var gender = brokerInfo.IsMale ? "male" : "female";
        return
            "Player and world context:\n" +
            contextJson + "\n\n" +
            $"Broker to voice: {brokerInfo.Name}, {gender}, at {station.name}, " +
            $"aligned with {brokerInfo.StationFaction}.\n\n" +
            "Produce one JSON object matching the vganima/mission/v1 schema.";
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
            if (station?.bar == null) { Plugin.Log.LogInfo("BarUI.RefreshPatrons ran with no current station"); return; }
            var roster = string.Join(", ", station.bar.availablePatrons
                .Select((p, i) => $"[{i}] {p.name}/seat{p.seat}/M={p.isMale}"));
            Plugin.Log.LogInfo($"BarUI.RefreshPatrons finished — list: {roster}");
        }
        catch (Exception ex) { Plugin.Log.LogError($"RefreshPatrons_Postfix threw: {ex}"); }
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
                $"BarUI instantiated prefab for: {patron?.name}/seat{patron?.seat}/M={patron?.isMale}");
        }
        catch (Exception ex) { Plugin.Log.LogError($"SetPatronData_Postfix threw: {ex}"); }
    }

    [HarmonyPostfix]
    [HarmonyPatch(nameof(BarPatronImage.SetPatronSprite))]
    private static void SetPatronSprite_Postfix(BarPatronImage __instance)
    {
        try
        {
            var patron = Traverse.Create(__instance).Field<BarPatron>("patron").Value;
            Plugin.Log.LogInfo(
                $"SetPatronSprite invoked for: {patron?.name}/seat{patron?.seat}/M={patron?.isMale}");
        }
        catch (Exception ex) { Plugin.Log.LogError($"SetPatronSprite_Postfix threw: {ex}"); }
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

                // Rehydrated brokers from legacy saves offered a fixed storyId.
                // v2-mission doesn't persist the LLM-authored storyId across sessions
                // (spec §7 known limitation) so we fall back to the legacy factory.
#pragma warning disable CS0618
                var storyId = TestStoryMissions.JobsiteSurveyId;
#pragma warning restore CS0618
                if (plugin.PlayerView.IsArchived(storyId))
                {
                    Plugin.Log.LogWarning(
                        $"Rehydrate: legacy storyId {storyId} is archived; " +
                        $"leaving broker '{patron.name}' unregistered");
                    continue;
                }

                // LLM dispatch: only if enabled. Without an LLM client we have
                // no way to fill LlmStory on rehydrate, so the broker falls
                // through to vanilla per spec §12. (Matching the v0.1 behaviour
                // when the LLM call fails.)
                if (plugin.LlmClient == null)
                {
                    Plugin.Log.LogDebug(
                        $"Rehydrate: LLM disabled, leaving broker '{patron.name}' unregistered");
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
                        $"Rehydrate ContextGatherer threw for '{salesman.name}': {ex}");
                    continue;
                }

                var contextJson = JsonConvert.SerializeObject(context);
                var systemPrompt = BuildRehydrateSystemPrompt();
                var userPrompt = BuildRehydrateUserPrompt(contextJson, brokerInfo, station);

                Plugin.Log.LogInfo(
                    $"Rehydrate: dispatching LLM for '{salesman.name}' " +
                    $"at '{station.name}' (seed={seed}, storyId={storyId})");

                _ = RehydrateDispatchAsync(plugin, bar, station, salesman, storyId,
                    systemPrompt, userPrompt);
            }
        }
        catch (Exception ex)
        {
            Plugin.Log.LogError($"RefreshPatrons_Prefix (rehydrate) threw: {ex}");
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
                $"Rehydrate LLM timeout; leaving broker '{patron.name}' unregistered");
            return;
        }
        catch (System.Net.Http.HttpRequestException ex)
        {
            Plugin.Log.LogWarning(
                $"Rehydrate LLM request failed: {ex.Message}; " +
                $"leaving broker '{patron.name}' unregistered");
            return;
        }
        catch (Exception ex)
        {
            Plugin.Log.LogError(
                $"Rehydrate LLM call threw for '{patron.name}': {ex}");
            return;
        }

        LlmStory story;
        try
        {
            story = plugin.Validator.Parse(rawContent);
        }
        catch (LlmValidationException ex)
        {
            Plugin.Log.LogInfo(
                $"Rehydrate LLM response failed validation: {ex.Message}; " +
                $"leaving broker '{patron.name}' unregistered.\n" +
                $"---- system prompt ----\n{systemPrompt}\n" +
                $"---- user prompt ----\n{userPrompt}\n" +
                $"---- raw response ----\n{rawContent}\n" +
                $"---- end ----");
            return;
        }
        catch (Exception ex)
        {
            Plugin.Log.LogError(
                $"Rehydrate unexpected validation failure for '{patron.name}': {ex}\n" +
                $"---- system prompt ----\n{systemPrompt}\n" +
                $"---- user prompt ----\n{userPrompt}\n" +
                $"---- raw response ----\n{rawContent}\n" +
                $"---- end ----");
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
                    $"Rehydrate: patron '{patron.name}' no longer in bar; dropping");
                return;
            }
            if (plugin.Registry.TryGet(patron, out _))
            {
                Plugin.Log.LogDebug(
                    $"Rehydrate: patron '{patron.name}' already registered (race); dropping");
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
                $"Rehydrated LLM-authored broker '{patron.name}' at '{station.name}' " +
                $"(storyId={storyId})");
        }
        catch (Exception ex)
        {
            Plugin.Log.LogError($"FinalizeRehydrate threw: {ex}");
        }
    }

    private static string BuildRehydrateSystemPrompt()
    {
        // v2-mission system prompt per spec §8. Duplicated verbatim from
        // BarRefreshPatches.BuildSystemPrompt so the two classes stay
        // independently modifiable. If the prompt grows, extract to a shared
        // module.
        return
            "You are a writer for bar-broker NPCs in a space-trading game. Your job is to\n" +
            "produce ONE dialogue-and-mission JSON object the broker will offer the player.\n\n" +
            "You ONLY output valid JSON matching this schema - no preamble, no markdown fences:\n\n" +
            "{\n" +
            "  \"schema\": \"vganima/mission/v1\",\n" +
            "  \"pitch\":    [ /* 3..5 short in-character lines pitching the job */ ],\n" +
            "  \"check_in\": [ /* 1..2 lines for when the captain returns mid-job */ ],\n" +
            "  \"payout\":   [ /* 2..4 lines for when the captain turns the job in */ ],\n" +
            "  \"mission\": {\n" +
            "    \"name\":            /* <=60 chars */,\n" +
            "    \"description\":     /* <=500 chars */,\n" +
            "    \"completion_text\": /* <=200 chars */,\n" +
            "    \"source_faction\":  /* one of: Marauders PoliceGuild BountyGuild\n" +
            "                          TradingGuild MiningGuild IndustrialGuild SalvageGuild\n" +
            "                          Stranded MercenaryGuild Smugglers Darkspacers Puppeteers\n" +
            "                          Fanatics HolyRadicals Amalgam Gold Red Blue */,\n" +
            "    \"steps\": [ /* 1..3 steps, each with 1..2 objectives */\n" +
            "      { \"objectives\": [ /* objective objects */ ] } ],\n" +
            "    \"rewards\": [ /* 1..5 reward objects */ ]\n" +
            "  }\n" +
            "}\n\n" +
            "OBJECTIVE TYPES (each objective object has a `type` plus fields):\n" +
            "  { \"type\": \"KillEnemies\",\n" +
            "    \"enemy_faction\":   <faction from list above>,\n" +
            "    \"required_amount\": 1..5,\n" +
            "    \"description\":     <<=120 chars> }\n" +
            "  { \"type\": \"ProtectUnit\",\n" +
            "    \"protect_text\":    <<=120 chars> }\n" +
            "  { \"type\": \"TriggerObjective\",\n" +
            "    \"trigger\":         one of [DockedWithSpaceStation, ArrivedAtSpaceStation, MoveToArea],\n" +
            "    \"required_amount\": 1..3,\n" +
            "    \"description\":     <<=120 chars> }\n" +
            "  { \"type\": \"CollectItemTypes\",\n" +
            "    \"item_category\":   one of [Ore, Salvage, RefinedProduct, TradeGoods, Junk],\n" +
            "    \"required_amount\": 1..50,\n" +
            "    \"description\":     <<=120 chars> }\n" +
            "  { \"type\": \"ClearPoi\",\n" +
            "    \"enemy_faction\":   <hostile faction from list above>,\n" +
            "    \"description\":     <<=120 chars> }\n" +
            "      Spawns a dedicated combat zone on the system map. Use ONLY when the\n" +
            "      mission is genuinely 'go fight at a specific place.' When you DO pick\n" +
            "      combat, prefer ClearPoi over KillEnemies; required_amount is auto-\n" +
            "      computed from the spawn, so do not specify one.\n\n" +
            "REWARD TYPES:\n" +
            "  { \"type\": \"Credits\",    \"base_value\": 15..100 }\n" +
            "  { \"type\": \"Experience\", \"base_value\": 30..100 }\n" +
            "  { \"type\": \"Reputation\", \"faction\": <faction>, \"amount\": -500..500 }\n\n" +
            "RULES FOR EVERY DIALOGUE LINE:\n" +
            "- ASCII only (no em-dashes, smart quotes, or emoji; hyphens and straight apostrophes OK)\n" +
            "- Maximum 120 characters\n" +
            "- Non-empty, no leading/trailing whitespace\n" +
            "- In character for the broker; reference the player's state or the location when it fits\n\n" +
            "COHERENCE RULES:\n" +
            "- MISSION ARCHETYPE — context.mission_guidance has a pre-computed ranked weights\n" +
            "  dict derived from the player's specialization, titles, cargo, active missions,\n" +
            "  station facilities, ship state, and faction state. The top-ranked archetype is\n" +
            "  the default pick. Deviate to a lower-ranked one ONLY if the context makes the\n" +
            "  top pick a bad fit (rare). Do NOT pick an archetype listed in\n" +
            "  mission_guidance.forbidden_archetypes — those are impossible given the context\n" +
            "  (e.g. combat forbidden when no hostile faction exists).\n" +
            "  The five archetypes map to these objective types:\n" +
            "    * combat  → ClearPoi (preferred) or KillEnemies. Requires a hostile faction.\n" +
            "    * gather  → CollectItemTypes with item_category Ore or RefinedProduct.\n" +
            "    * salvage → CollectItemTypes with item_category Salvage or Junk.\n" +
            "    * deliver → TriggerObjective (Docked/Arrived/MoveToArea), optionally paired\n" +
            "                with CollectItemTypes TradeGoods for a 'haul + unload' shape.\n" +
            "    * escort  → ProtectUnit + TriggerObjective travel to destination.\n" +
            "  mission_guidance.rationale lists the signals that drove the weights — use it as\n" +
            "  flavor material (if it mentions @GatlingAmmo, the pitch can reference gunnery).\n" +
            "- Dialogue and mission must match: if the pitch promises a rescue, include ProtectUnit\n" +
            "  or KillEnemies, not a lone CollectItemTypes.\n" +
            "- AT MOST ONE COMBAT OBJECTIVE PER STEP. Do NOT put ClearPoi and KillEnemies into\n" +
            "  the same step. ClearPoi auto-completes when its spawned zone is cleared; KillEnemies\n" +
            "  counts ANY kill of that faction anywhere — mixing them creates a 'main mission done,\n" +
            "  stragglers still pending' shape that's awkward. Pick one combat verb per step.\n" +
            "- FACTION NAMING: the context exposes each faction under its identifier (JSON key)\n" +
            "  with a display_name, relation (friendly|neutral|hostile), and reputation value.\n" +
            "    * In dialogue lines (pitch / check_in / payout), ALWAYS use the display_name.\n" +
            "    * In mission block fields (source_faction, enemy_faction, reward faction),\n" +
            "      ALWAYS use the identifier (the JSON key).\n" +
            "  Example: dialogue says \"the Corsair Syndicate is harassing us\" but the mission\n" +
            "  block sets enemy_faction=\"Marauders\".\n" +
            "- source_faction is the hiring broker's faction. Only pick an identifier whose\n" +
            "  relation in context.factions is \"friendly\" — disliked or hostile gilds would\n" +
            "  not hire the player.\n" +
            "- For KillEnemies, enemy_faction MUST be an identifier whose relation is \"hostile\".\n" +
            "  Do NOT target friendly or neutral factions, even if narratively fitting. If no\n" +
            "  faction has relation=\"hostile\", omit KillEnemies entirely and use other\n" +
            "  objective types.\n" +
            "- At least one objective across all steps must NOT be ProtectUnit (a mission of pure\n" +
            "  protect is degenerate).\n" +
            "- Reward base_values MUST stay within context.reward_clamps. The schema block above\n" +
            "  lists the same ranges; the context repeats them so you cannot miss them.\n" +
            "- TYPICAL REWARD MAGNITUDES (match vanilla's mission-board feel — the formula\n" +
            "  scales base_values by missionLevel internally, so do NOT inflate bases for\n" +
            "  higher-level stations):\n" +
            "    * Deliver / fetch / courier only:      credits 20-30, xp 40-55.\n" +
            "    * Mine / salvage / collect:            credits 25-40, xp 45-60.\n" +
            "    * Kill / escort / protect / clear:     credits 35-50, xp 55-75.\n" +
            "    * Hazardous multi-objective mission:   credits 60-100, xp 75-100.\n" +
            "    * Reputation amount: 200-350 standard; up to 500 for faction-defining favors.\n" +
            "      Even simple gather / deliver jobs pay 200-300 — DO NOT emit positive amounts\n" +
            "      below 150. Vanilla's procedural floor is 200; going lower feels like an insult.\n" +
            "  STEP COUNT IS NARRATIVE STRUCTURE, NOT A REWARD MULTIPLIER. Vanilla's\n" +
            "  single-step and multi-step missions pay the same when the objective\n" +
            "  archetype matches — pick base_value by archetype, not by step count.\n\n" +
            "Reply with ONLY the JSON object.";
    }

    private static string BuildRehydrateUserPrompt(string contextJson, BrokerInfo brokerInfo, SpaceStation station)
    {
        var gender = brokerInfo.IsMale ? "male" : "female";
        return
            "Player and world context:\n" +
            contextJson + "\n\n" +
            $"Broker to voice: {brokerInfo.Name}, {gender}, at {station.name}, " +
            $"aligned with {brokerInfo.StationFaction}.\n\n" +
            "Produce one JSON object matching the vganima/mission/v1 schema.";
    }
}
