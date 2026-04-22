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
using VGAnima.Persistence;
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

    /// <summary>Wired by <see cref="Plugin.Awake"/> to the shared
    /// <see cref="PersistedBrokerRegistry"/> singleton (same one
    /// <see cref="SaveLoadPatch"/> / <see cref="SaveWritePatch"/> use).
    /// Null outside prod / pre-T15 integration — <see cref="IsActive"/>
    /// then falls through to the vanilla mission-list check alone.</summary>
    public static PersistedBrokerRegistry? PersistedRegistry;

    /// <summary>Procedural voice defaults — match VGTTS's own defaults so mod
    /// load order doesn't change perceived voice.</summary>
    private const string ProceduralMaleVoice   = "kokoro:12";
    private const string ProceduralFemaleVoice = "kokoro:9";

    private static readonly ConditionalWeakTable<Bar, List<BarPatron>> _snapshots = new();

    /// <summary>In-flight injection guard. The LLM dispatch is async, so two
    /// MissionChance rolls for the same station in rapid succession can both
    /// start injecting before either finishes. The existing "broker already
    /// present" check only sees patrons already added to the bar — a broker
    /// whose LLM call hasn't returned yet isn't visible. Guarded by a HashSet
    /// of station guids keyed on entry; released on every exit path of
    /// <see cref="DispatchAsync"/> and <see cref="FinalizeBrokerInjection"/>.
    /// The lock is cheap because contention is ~one claim per potential
    /// injection.</summary>
    private static readonly HashSet<string> _injectionsInFlight = new();
    private static readonly object _injectionsInFlightLock = new();

    private static bool TryClaimInjection(string stationGuid)
    {
        lock (_injectionsInFlightLock)
        {
            if (_injectionsInFlight.Contains(stationGuid)) return false;
            _injectionsInFlight.Add(stationGuid);
            return true;
        }
    }

    private static void ReleaseInjection(string stationGuid)
    {
        lock (_injectionsInFlightLock)
        {
            _injectionsInFlight.Remove(stationGuid);
        }
    }

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
            if (IsActive(record, plugin.PlayerView, PersistedRegistry))
            {
                pinned.Add(p);
                // Bump lastSeen on the persisted entry so a future pruning policy
                // (TTL/LRU) sees fresh activity whenever the bar re-refreshes over
                // a registered broker. Spec §5 bullet: "Bar refresh touches a
                // persisted broker → bump lastSeen timestamps".
                if (PersistedRegistry is not null && plugin.Clock is not null)
                {
                    PersistedRegistry.BumpLastSeen(
                        record.StoryId,
                        gameSeconds: plugin.Clock.GameSeconds,
                        realUtc:     plugin.Clock.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ"));
                }
            }
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

            // Orphan purge — runs every bar refresh (not one-shot). Offered
            // entries are scoped to the current bar's station: entries whose
            // stationId matches the bar we just processed AND whose seed
            // isn't in this bar's patron list get dropped. Entries for other
            // stations survive — we can't verify their brokers from here
            // and premature purging would drop live state (cost of the
            // earlier one-shot design: loading at Spire XIV purged every
            // Outrider 3A entry before the player's bar at Outrider 3A
            // ever got to rehydrate).
            // Accepted entries are purged globally against vanilla's mission
            // lists regardless of the current station.
            if (PersistedRegistry is { } reg && Plugin.Instance is { } plugin)
            {
                try
                {
                    // Bar.spaceStation is private — mirror the Traverse pattern used
                    // by StartInjectMissionBroker below.
                    var station = Traverse.Create(__instance).Field<SpaceStation>("spaceStation").Value;
                    var currentStationId = station?.guid;

                    var activeIds   = new List<string>();
                    var archivedIds = new List<string>();
                    foreach (var entry in reg.All())
                    {
                        if (plugin.PlayerView.GetActive(entry.StoryId) is not null)
                            activeIds.Add(entry.StoryId);
                        else if (plugin.PlayerView.IsArchived(entry.StoryId))
                            archivedIds.Add(entry.StoryId);
                    }

                    var seeds = new List<string>();
                    foreach (var patron in __instance.availablePatrons)
                        if (patron is Salesman s && !string.IsNullOrEmpty(s.seed))
                            seeds.Add(s.seed);

                    var dropped = OrphanPurger.Purge(
                        reg, activeIds, archivedIds, seeds, currentStationId);
                    if (dropped.Count > 0)
                        Plugin.Log.LogInfo(
                            $"Orphan-purged {dropped.Count} stale entr{(dropped.Count == 1 ? "y" : "ies")} " +
                            $"at station {currentStationId}: {string.Join(", ", dropped)}");
                }
                catch (Exception e)
                {
                    Plugin.Log.LogError($"Orphan purge failed: {e}");
                }
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

        // Claim this station's injection slot BEFORE minting the seed or
        // creating the patron. Releases in DispatchAsync / FinalizeBrokerInjection
        // exit paths. Prevents two rapid MissionChance rolls from both firing
        // the LLM + both registering brokers at the same station (the
        // "already present" check upstream only sees patrons already in
        // bar.availablePatrons — a broker whose LLM call is still in flight
        // isn't visible yet).
        if (!TryClaimInjection(station.guid))
        {
            Plugin.Log.LogDebug(
                $"Injection already in flight at '{station.name}'; skipping this refresh");
            return;
        }

        // Mint the seed. Each broker gets a unique nonce suffix so multiple
        // VGAnima brokers at the same station don't collapse to an identical
        // vanilla-regenerated identity (name / portrait / gender come from
        // the Salesman seed via vanilla's RNG — a deterministic seed makes
        // them all look like the same character).
        var candidateSeed = $"{BrokerSeedPrefix}{station.guid}-{Guid.NewGuid():N}";

        // v2-mission: no pre-flight storyId decision. The LLM authors the
        // mission, and LlmMissionAssigner registers it after validation.

        // Create the patron up-front (not added to the bar yet) so we know its
        // name + isMale for the broker section of the LLM context, and can
        // re-use the same object on the main-thread continuation.
        //
        // We deliberately DO NOT override `_name` or `description` here —
        // vanilla's BarPatron.ToJson only persists the salesman seed, so any
        // in-session override would be lost on load and require a restoration
        // hook to re-apply. That pattern compounds for every override we add
        // (portrait, voice, title, ...), turning the load path into a
        // fix-up chain. Instead, the patron keeps its vanilla seed-derived
        // identity end-to-end; VGAnima authorship shows through the
        // LLM-written pitch/check_in/payout dialogue content, not by
        // rebranding the NPC.
        var newPatron = new Salesman(candidateSeed, station);
        newPatron.Initialize();

        var genderSeats = sprites
            .Where(s => s.isMale == newPatron.isMale)
            .Select(s => s.seatIndex)
            .Distinct()
            .ToList();
        if (genderSeats.Count == 0)
        {
            Plugin.Log.LogWarning($"No patronSprites for isMale={newPatron.isMale}; aborting");
            ReleaseInjection(station.guid);
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
            // Journal section is built per-broker from the registry's
            // completed-mission log. Toggled by Style.IncludePlayerJournal;
            // when off we pass null and the LlmContext omits the field.
            // See JournalContextBuilder for the three-window filter logic.
            LlmJournalSection? journal = null;
            if (plugin.Cfg.IncludePlayerJournal.Value && plugin.PersistedRegistry != null)
            {
                journal = JournalContextBuilder.Build(
                    plugin.PersistedRegistry.CompletedMissions,
                    stationId:         station.guid,
                    factionIdentifier: brokerInfo.StationFaction);
            }
            context = plugin.Gatherer.Gather(plugin.GameStateView, brokerInfo, journal);
        }
        catch (Exception ex)
        {
            Plugin.Log.LogError($"ContextGatherer threw at '{station.name}': {ex}");
            ReleaseInjection(station.guid);
            return;
        }

        var contextJson = JsonConvert.SerializeObject(context);
        var systemPrompt = BuildSystemPrompt(Plugin.Instance.Cfg.StageDirectionLevel.Value);
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
            ReleaseInjection(station.guid);
            return;
        }
        catch (System.Net.Http.HttpRequestException ex)
        {
            stopwatch.Stop();
            Plugin.Log.LogWarning(
                $"LLM request failed after {stopwatch.ElapsedMilliseconds}ms: {ex.Message}; " +
                $"skipping broker at '{station.name}'");
            ReleaseInjection(station.guid);
            return;
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            Plugin.Log.LogError(
                $"LLM call threw after {stopwatch.ElapsedMilliseconds}ms; " +
                $"skipping broker at '{station.name}': {ex}");
            ReleaseInjection(station.guid);
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
            ReleaseInjection(station.guid);
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
            ReleaseInjection(station.guid);
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
        // NOTE: the injection claim is NOT released here — it carries through
        // to FinalizeBrokerInjection's finally block, which releases after
        // the patron is fully in bar.availablePatrons (by which point the
        // upstream "Broker already present" check can see and reject
        // subsequent injections on its own).
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
                        story.Mission, missionLevel, station, candidateSeed,
                        brokerStory: story,
                        brokerName:  newPatron.name,
                        systemName:  station.system?.name);
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

            plugin.Registry.Register(newPatron,
                new ConversionRecord(
                    warmedPairs, station, storyId, story,
                    stationId:   station.guid));
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
        finally
        {
            // Release the in-flight claim — whether success, early return
            // (player left station, race), or exception. After this point
            // the patron is either in bar.availablePatrons (upstream
            // idempotency check handles subsequent attempts) or nowhere
            // (the slot is free for a fresh injection).
            ReleaseInjection(station.guid);
        }
    }

    // internal (not private) so VGAnima.Tests can snapshot the live prompt
    // for offline LLM evaluation harnesses. No runtime caller outside this
    // file.
    internal static string BuildSystemPrompt(int stageDirectionLevel = 0)
    {
        // v2-mission system prompt per spec §8.
        var basePrompt =
            "You are a writer for bar-broker NPCs in a space-trading game. Your job is to\n" +
            "produce ONE dialogue-and-mission JSON object the broker will offer the player.\n\n" +
            "You ONLY output valid JSON matching this schema - no preamble, no markdown fences:\n\n" +
            "{\n" +
            "  \"schema\": \"vganima/mission/v1\",\n" +
            "  \"pitch\":    [ /* 3..5 short in-character lines pitching the job */ ],\n" +
            "  \"check_in\": [ /* 1..2 lines for when the captain returns mid-job */ ],\n" +
            "  \"payout\":   [ /* 2..4 lines for when the captain turns the job in */ ],\n" +
            "  \"mission\": {\n" +
            $"    \"name\":            /* <={MissionBlockValidator.NameSoftMaxLen} chars */,\n" +
            $"    \"description\":     /* <={MissionBlockValidator.DescriptionSoftMaxLen} chars */,\n" +
            $"    \"completion_text\": /* <={MissionBlockValidator.CompletionTextSoftMaxLen} chars */,\n" +
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
            $"    \"description\":     <<={MissionBlockValidator.ObjDescriptionSoftMaxLen} chars> }}\n" +
            "  { \"type\": \"ProtectUnit\",\n" +
            $"    \"protect_text\":    <<={MissionBlockValidator.ProtectTextSoftMaxLen} chars> }}\n" +
            "  { \"type\": \"TriggerObjective\",\n" +
            "    \"trigger\":         one of [DockedWithSpaceStation, ArrivedAtSpaceStation, MoveToArea],\n" +
            "    \"required_amount\": 1..3,\n" +
            $"    \"description\":     <<={MissionBlockValidator.ObjDescriptionSoftMaxLen} chars> }}\n" +
            "  { \"type\": \"CollectItemTypes\",\n" +
            "    \"item_category\":   one of [Ore, Salvage, RefinedProduct, TradeGoods],\n" +
            "    \"required_amount\": 1..50,\n" +
            $"    \"description\":     <<={MissionBlockValidator.ObjDescriptionSoftMaxLen} chars>,\n" +
            "    \"guards_faction\":  <hostile faction from list above, OPTIONAL> }\n" +
            "      Ore and Salvage auto-spawn a dedicated POI on the system map\n" +
            "      (asteroid field for Ore, derelict fleet for Salvage) so the player\n" +
            "      has a specific place to go. RefinedProduct and TradeGoods do NOT\n" +
            "      spawn a POI — those are sourced through refineries / traders, so\n" +
            "      the pitch should frame them as 'bring me N units you've got lying\n" +
            "      around' rather than 'go to this location.'\n" +
            "      guards_faction: HARD-GATED to item_category Ore or Salvage.\n" +
            "      The validator rejects guards_faction on RefinedProduct or\n" +
            "      TradeGoods outright (those categories have no POI to attach\n" +
            "      units to). When valid, it attaches hostile combat units to\n" +
            "      the spawned POI — one POI with defenders inside, no separate\n" +
            "      ClearPoi needed. Mirrors vanilla's defended-salvage pattern.\n" +
            "      If set, the pitch should reference the defenders AND the\n" +
            "      loot (\"the Corsair Syndicate is camping the wreck — fight\n" +
            "      through them and bring back the scrap\").\n" +
            "      DO NOT set guards_faction when `combat` is in\n" +
            "      forbidden_archetypes — defenders count as combat, see the\n" +
            "      archetype section below.\n" +
            "  { \"type\": \"ClearPoi\",\n" +
            "    \"enemy_faction\":   <hostile faction from list above>,\n" +
            $"    \"description\":     <<={MissionBlockValidator.ObjDescriptionSoftMaxLen} chars> }}\n" +
            "      Spawns a dedicated combat zone on the system map. Use ONLY when the\n" +
            "      mission is genuinely 'go fight at a specific place' with NO loot to\n" +
            "      bring back. If the site also has salvage/ore to collect, DO NOT emit\n" +
            "      ClearPoi — use CollectItemTypes with guards_faction instead (one POI,\n" +
            "      one Locate target). When you DO pick pure combat, prefer ClearPoi\n" +
            "      over KillEnemies; required_amount is auto-computed from the spawn,\n" +
            "      so do not specify one.\n\n" +
            "REWARD TYPES:\n" +
            "  { \"type\": \"Credits\",    \"base_value\": 15..100 }\n" +
            "  { \"type\": \"Experience\", \"base_value\": 30..100 }\n" +
            "  { \"type\": \"Reputation\", \"faction\": <faction>, \"amount\": -500..500 }\n\n" +
            "RULES FOR EVERY DIALOGUE LINE:\n" +
            "- ASCII only (no em-dashes, smart quotes, or emoji; hyphens and straight apostrophes OK)\n" +
            $"- Maximum {ResponseValidator.DialogueLineSoftMaxLen} characters\n" +
            "- Non-empty, no leading/trailing whitespace\n" +
            "- In character for the broker; reference the player's state or the location when it fits\n" +
            "- Speak in first person as the broker. NEVER prefix a line with your own name\n" +
            "  (e.g. do NOT write \"Reagan: The Vultures are hiring\"), and NEVER refer to\n" +
            "  yourself in the third person (\"Reagan Dualla is ready to cut the contract\").\n" +
            "  The UI shows the speaker's name separately — adding it to the line duplicates it.\n\n" +
            "COHERENCE RULES:\n" +
            "- MISSION ARCHETYPE — context.mission_guidance has a pre-computed ranked weights\n" +
            "  dict derived from the player's specialization, titles, cargo, active missions,\n" +
            "  station facilities, ship state, and faction state. The top-ranked archetype is\n" +
            "  the default pick. Deviate to a lower-ranked one ONLY if the context makes the\n" +
            "  top pick a bad fit (rare). Do NOT pick an archetype listed in\n" +
            "  mission_guidance.forbidden_archetypes — those are impossible given the context\n" +
            "  (e.g. combat forbidden when no hostile faction exists).\n" +
            "  When `combat` is listed in forbidden_archetypes, ALL of the following are\n" +
            "  forbidden — treat the ENTIRE mission as non-combat:\n" +
            "    * NO ClearPoi objectives.\n" +
            "    * NO KillEnemies objectives.\n" +
            "    * NO guards_faction on CollectItemTypes (guards spawn hostile units at\n" +
            "      the POI, which is combat).\n" +
            "    * NO mentioning defenders, raiders, ambushes, or hostile presence in the\n" +
            "      pitch / check-in / payout lines. The site is safe. The job is peaceful.\n" +
            "  Even when hostile factions exist in context.factions (Marauders etc.),\n" +
            "  they are simply unavailable for THIS mission. Pick a gather/deliver/escort\n" +
            "  shape with no combat element whatsoever.\n" +
            "  The five archetypes map to these objective types:\n" +
            "    * combat  → ClearPoi (preferred) or KillEnemies. Requires a hostile faction.\n" +
            "    * gather  → CollectItemTypes with item_category Ore or RefinedProduct.\n" +
            "    * salvage → CollectItemTypes with item_category Salvage.\n" +
            "    * deliver → TriggerObjective (Docked/Arrived/MoveToArea), optionally paired\n" +
            "                with CollectItemTypes TradeGoods for a 'haul + unload' shape.\n" +
            "    * escort  → ProtectUnit + TriggerObjective travel to destination.\n" +
            "  mission_guidance.rationale lists the signals that drove the weights — use it as\n" +
            "  flavor material (if it mentions @GatlingAmmo, the pitch can reference gunnery).\n" +
            "- Dialogue and mission MUST match BOTH WAYS:\n" +
            "    * If the pitch promises a rescue, include ProtectUnit or KillEnemies — not a lone CollectItemTypes.\n" +
            "    * If the pitch / payout promise \"bring back the haul\" / \"recovered materials\" /\n" +
            "      \"a cut of the loot\" / \"your share of the scrap\" — the mission MUST include a\n" +
            "      CollectItemTypes objective producing those items. Don't promise a haul and then\n" +
            "      only emit ClearPoi — the player sees nothing to deliver, the payout line lies.\n" +
            "    * If the mission is pure combat (only ClearPoi / KillEnemies), the payout language\n" +
            "      must be combat-framed (\"the zone is clear\", \"threat neutralized\", \"bounty paid\"),\n" +
            "      NOT loot-framed.\n" +
            "- AT MOST ONE COMBAT OBJECTIVE PER STEP. Do NOT put ClearPoi and KillEnemies into\n" +
            "  the same step. ClearPoi auto-completes when its spawned zone is cleared; KillEnemies\n" +
            "  counts ANY kill of that faction anywhere — mixing them creates a 'main mission done,\n" +
            "  stragglers still pending' shape that's awkward. Pick one combat verb per step.\n" +
            "- AT MOST ONE POI-SPAWNING OBJECTIVE PER STEP. ClearPoi and CollectItemTypes with\n" +
            "  item_category Ore or Salvage both spawn a dedicated POI on the map; a step's\n" +
            "  Locate button can only track ONE. Two in the same step orphans the second POI.\n" +
            "    * Defended gather site (fight and loot the same place) → emit ONE\n" +
            "      CollectItemTypes objective with guards_faction set; do NOT also emit ClearPoi.\n" +
            "    * Clear one zone, then gather elsewhere → emit TWO SEPARATE STEPS (step 1 =\n" +
            "      ClearPoi, step 2 = CollectItemTypes). Vanilla missions use this shape when\n" +
            "      the combat and gather sites are genuinely different places.\n" +
            "- MULTI-STEP IS A FIRST-CLASS SHAPE, NOT A FALLBACK. You have 1..3 steps. Use 2-3\n" +
            "  steps when the narrative mentions multiple locations, phased objectives, or\n" +
            "  compound tasks. The Locate button advances step-by-step, so each step is its own\n" +
            "  waypoint for the player. Good multi-step shapes:\n" +
            "    * \"Clear three pirate camps\" → 3 steps, each with one ClearPoi pointing at a\n" +
            "      different spawned POI. Three Locate waypoints in sequence.\n" +
            "    * \"Fight through the Corsairs and recover repair materials\" → 2 steps:\n" +
            "      [ClearPoi(Marauders)] then [CollectItemTypes(Salvage)]. Two separate POIs.\n" +
            "    * \"Mine the field, then haul the output to Station X\" → 2 steps:\n" +
            "      [CollectItemTypes(Ore)] then [TriggerObjective(DockedWithSpaceStation)].\n" +
            "    * \"Take out three Marauder patrols, then deliver the intel\" → 3 steps:\n" +
            "      [ClearPoi] [ClearPoi] [TriggerObjective]. Three waypoints.\n" +
            "  The one-step defended-collect pattern is the right shape for a SINGLE site with\n" +
            "  defenders AND loot at the same place. Do NOT collapse a multi-site or\n" +
            "  multi-phase narrative into one step just because it validates — the pitch will\n" +
            "  promise more than the mission delivers.\n" +
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
            "  archetype matches — pick base_value by archetype, not by step count.\n";

        // Opt-in stage-direction flavor. Default (level 0) appends nothing so
        // the prompt is byte-identical to the pre-feature baseline — no risk
        // of regressing existing behavior for users who don't enable it.
        // Level 1 (sparse) and level 2 (rich) append differently-worded rules
        // that instruct the LLM to include bracketed physical actions in
        // dialogue, e.g. "[Spits on the floor] The Corsairs are circling."
        return basePrompt + BuildStageDirectionRule(stageDirectionLevel) +
               "\nReply with ONLY the JSON object.";
    }

    /// <summary>Returns the stage-direction instruction block for the given
    /// intensity level (0 = off, 1 = sparse, 2 = rich). Clamps unknown values
    /// to the nearest supported level: &lt;=0 → off, &gt;=2 → rich.</summary>
    internal static string BuildStageDirectionRule(int level)
    {
        if (level <= 0) return string.Empty;

        var frequency = level >= 2
            ? "MOST dialogue lines across the pitch, check_in, and payout " +
              "should include one"
            : "1-2 dialogue lines total across the pitch, check_in, and " +
              "payout should include one (use sparingly — most lines stay " +
              "plain dialogue)";

        return
            "- STAGE DIRECTIONS: bracketed physical actions that imply the\n" +
            "  broker's presence, emotion, or physical state. " + frequency + ":\n" +
            "    Examples: \"[Spits on the floor] The Corsairs are circling.\"\n" +
            "              \"[Glances at the door] Keep this between us.\"\n" +
            "              \"[Wipes blood from his lip] Yeah, I won't ask.\"\n" +
            "  Rules:\n" +
            "    * Use square brackets, not parentheses or asterisks.\n" +
            "    * Keep each direction short (<=30 chars including brackets).\n" +
            "    * Physical / observable only — no thoughts, no narration.\n" +
            "    * ASCII only, same as the rest of the dialogue.\n" +
            "    * The bracketed text COUNTS against the line's 108-char\n" +
            "      limit — budget accordingly.\n" +
            "    * Place at the start of a line when it sets up the spoken\n" +
            "      words; at the end when it reacts to them. Do not embed\n" +
            "      mid-sentence.\n";
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
    /// OR if the persisted-broker registry holds an entry for it. The
    /// registry-hit path is what makes unaccepted brokers survive rotation
    /// across sessions — spec §4 explicitly lists offered-state entries as
    /// pin-worthy.</summary>
    private static bool IsActive(
        ConversionRecord record,
        IGamePlayerView player,
        PersistedBrokerRegistry? persistedRegistry)
    {
        // Registry hit (offered or accepted) pins regardless of vanilla
        // mission-list state. Unaccepted brokers have no vanilla mission yet,
        // so without this branch they wouldn't survive rotation.
        if (persistedRegistry?.Get(record.StoryId) is not null) return true;

        // Fallback: existing vanilla-mission-list check. Still used for pre-T15
        // sessions (persistedRegistry null) and for records whose storyId
        // somehow left the persisted registry but is still in vanilla's list.
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
/// where the session-level registry is empty but vanilla persisted our
/// brokers.
///
/// After Task 14 (persistence): rehydration pulls the <see cref="LlmStory"/>
/// and mission metadata from the in-memory <see cref="PersistedBrokerRegistry"/>
/// (loaded from the sidecar by <see cref="SaveLoadPatch"/>). No LLM call
/// is made on this path anymore — one LLM call per broker per save, full
/// stop. On a registry miss (e.g. sidecar missing/corrupt, orphan-purged
/// entry, hand-crafted seed), the patron is left as a plain salesman and
/// falls through to vanilla <c>ShowSalesmanInfo</c>.
/// </summary>
[HarmonyPatch(typeof(BarUI))]
internal static class RegistryRehydratePatches
{
    /// <summary>Wired by <c>Plugin.Awake</c> to the shared
    /// <see cref="PersistedBrokerRegistry"/> singleton (same one
    /// <see cref="SaveLoadPatch"/> / <see cref="SaveWritePatch"/> use).
    /// Null outside prod / pre-T15 integration — hook is a no-op then.</summary>
    public static PersistedBrokerRegistry? PersistedRegistry;

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

            foreach (var patron in bar.availablePatrons)
            {
                if (patron is not Salesman salesman) continue;
                var seed = salesman.seed;
                if (string.IsNullOrEmpty(seed)) continue;
                if (!seed.StartsWith(BarRefreshPatches.BrokerSeedPrefix, StringComparison.Ordinal)) continue;
                if (plugin.Registry.TryGet(patron, out _)) continue;    // already rehydrated

                // Registry miss → no LLM call; leave patron as plain salesman.
                // This replaces the v2-mission legacy-storyId fallback.
                // Post-persistence, the only way a VGAnima-seeded patron
                // can show up without a registry entry is:
                //   (a) sidecar missing/corrupt (placeholder factory covers),
                //   (b) entry was purged by orphan cleanup,
                //   (c) someone hand-crafted a seed prefix — not our problem.
                var entry = PersistedRegistry?.FindBySeed(seed);
                if (entry is null)
                {
                    Plugin.Log.LogInfo(
                        $"Rehydrate: seed-prefixed patron '{salesman.name}' (seed={seed}) " +
                        "has no registry entry; leaving as plain salesman");
                    continue;
                }

                // Restore ConversionRecord directly from the persisted entry.
                // No LLM call, no new mission assignment — the mission factory
                // was already registered by SaveLoadPatch. No name / description
                // override either: the injection path no longer mutates those
                // fields, so the salesman's vanilla seed-derived identity is
                // the same before the save and after the reload. Warm-cache
                // keys use `salesman.name` because that's what VGTTS and
                // dialogue playback see.
                var story = entry.Broker.Story;
                var warmedLines = new List<(string Speaker, string Text)>();
                foreach (var line in story.Pitch)   if (!string.IsNullOrWhiteSpace(line)) warmedLines.Add((salesman.name, line));
                foreach (var line in story.CheckIn) if (!string.IsNullOrWhiteSpace(line)) warmedLines.Add((salesman.name, line));
                foreach (var line in story.Payout)  if (!string.IsNullOrWhiteSpace(line)) warmedLines.Add((salesman.name, line));

                plugin.Registry.Register(patron, new ConversionRecord(
                    warmedLines:   warmedLines,
                    station:       station,
                    storyId:       entry.StoryId,
                    llmStory:      story,
                    stationId:     entry.Broker.StationId));

                // Warm VGTTS cache for every rehydrated dialogue line — matches
                // the normal injection path (see CheckUpdatePatrons_Postfix).
                // Without this, cold-load brokers miss the cache on dialogue
                // open and VGTTS falls back to live synthesis, which is both
                // slower and marks the speaker as "procedural" in its logs.
                _ = Task.Run(async () =>
                {
                    foreach (var (speaker, text) in warmedLines)
                    {
                        try { await plugin.Vgtts.WarmCacheAsync(speaker, text, CancellationToken.None); }
                        catch { /* best-effort; live TTS warms again on dialogue open */ }
                    }
                });

                Plugin.Log.LogInfo(
                    $"Rehydrate: restored broker '{salesman.name}' (seed={seed}, storyId={entry.StoryId}) from sidecar " +
                    $"(warming {warmedLines.Count} TTS line(s))");
            }
        }
        catch (Exception ex)
        {
            Plugin.Log.LogError($"RefreshPatrons_Prefix (rehydrate) threw: {ex}");
        }
    }

    [System.Obsolete("Superseded by PersistedBrokerRegistry-driven rehydration in T14. Remove after T17 E2E verification.")]
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

    [System.Obsolete("Superseded by PersistedBrokerRegistry-driven rehydration in T14. Remove after T17 E2E verification.")]
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

            plugin.Registry.Register(patron,
                new ConversionRecord(
                    warmedPairs, station, storyId, story,
                    stationId:   station.guid));

            Plugin.Log.LogInfo(
                $"Rehydrated LLM-authored broker '{patron.name}' at '{station.name}' " +
                $"(storyId={storyId})");
        }
        catch (Exception ex)
        {
            Plugin.Log.LogError($"FinalizeRehydrate threw: {ex}");
        }
    }

    [System.Obsolete("Superseded by PersistedBrokerRegistry-driven rehydration in T14. Remove after T17 E2E verification.")]
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
            $"    \"name\":            /* <={MissionBlockValidator.NameSoftMaxLen} chars */,\n" +
            $"    \"description\":     /* <={MissionBlockValidator.DescriptionSoftMaxLen} chars */,\n" +
            $"    \"completion_text\": /* <={MissionBlockValidator.CompletionTextSoftMaxLen} chars */,\n" +
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
            $"    \"description\":     <<={MissionBlockValidator.ObjDescriptionSoftMaxLen} chars> }}\n" +
            "  { \"type\": \"ProtectUnit\",\n" +
            $"    \"protect_text\":    <<={MissionBlockValidator.ProtectTextSoftMaxLen} chars> }}\n" +
            "  { \"type\": \"TriggerObjective\",\n" +
            "    \"trigger\":         one of [DockedWithSpaceStation, ArrivedAtSpaceStation, MoveToArea],\n" +
            "    \"required_amount\": 1..3,\n" +
            $"    \"description\":     <<={MissionBlockValidator.ObjDescriptionSoftMaxLen} chars> }}\n" +
            "  { \"type\": \"CollectItemTypes\",\n" +
            "    \"item_category\":   one of [Ore, Salvage, RefinedProduct, TradeGoods],\n" +
            "    \"required_amount\": 1..50,\n" +
            $"    \"description\":     <<={MissionBlockValidator.ObjDescriptionSoftMaxLen} chars>,\n" +
            "    \"guards_faction\":  <hostile faction from list above, OPTIONAL> }\n" +
            "      Ore and Salvage auto-spawn a dedicated POI on the system map\n" +
            "      (asteroid field for Ore, derelict fleet for Salvage) so the player\n" +
            "      has a specific place to go. RefinedProduct and TradeGoods do NOT\n" +
            "      spawn a POI — those are sourced through refineries / traders, so\n" +
            "      the pitch should frame them as 'bring me N units you've got lying\n" +
            "      around' rather than 'go to this location.'\n" +
            "      guards_faction: HARD-GATED to item_category Ore or Salvage.\n" +
            "      The validator rejects guards_faction on RefinedProduct or\n" +
            "      TradeGoods outright (those categories have no POI to attach\n" +
            "      units to). When valid, it attaches hostile combat units to\n" +
            "      the spawned POI — one POI with defenders inside, no separate\n" +
            "      ClearPoi needed. Mirrors vanilla's defended-salvage pattern.\n" +
            "      If set, the pitch should reference the defenders AND the\n" +
            "      loot (\"the Corsair Syndicate is camping the wreck — fight\n" +
            "      through them and bring back the scrap\").\n" +
            "      DO NOT set guards_faction when `combat` is in\n" +
            "      forbidden_archetypes — defenders count as combat, see the\n" +
            "      archetype section below.\n" +
            "  { \"type\": \"ClearPoi\",\n" +
            "    \"enemy_faction\":   <hostile faction from list above>,\n" +
            $"    \"description\":     <<={MissionBlockValidator.ObjDescriptionSoftMaxLen} chars> }}\n" +
            "      Spawns a dedicated combat zone on the system map. Use ONLY when the\n" +
            "      mission is genuinely 'go fight at a specific place' with NO loot to\n" +
            "      bring back. If the site also has salvage/ore to collect, DO NOT emit\n" +
            "      ClearPoi — use CollectItemTypes with guards_faction instead (one POI,\n" +
            "      one Locate target). When you DO pick pure combat, prefer ClearPoi\n" +
            "      over KillEnemies; required_amount is auto-computed from the spawn,\n" +
            "      so do not specify one.\n\n" +
            "REWARD TYPES:\n" +
            "  { \"type\": \"Credits\",    \"base_value\": 15..100 }\n" +
            "  { \"type\": \"Experience\", \"base_value\": 30..100 }\n" +
            "  { \"type\": \"Reputation\", \"faction\": <faction>, \"amount\": -500..500 }\n\n" +
            "RULES FOR EVERY DIALOGUE LINE:\n" +
            "- ASCII only (no em-dashes, smart quotes, or emoji; hyphens and straight apostrophes OK)\n" +
            $"- Maximum {ResponseValidator.DialogueLineSoftMaxLen} characters\n" +
            "- Non-empty, no leading/trailing whitespace\n" +
            "- In character for the broker; reference the player's state or the location when it fits\n" +
            "- Speak in first person as the broker. NEVER prefix a line with your own name\n" +
            "  (e.g. do NOT write \"Reagan: The Vultures are hiring\"), and NEVER refer to\n" +
            "  yourself in the third person (\"Reagan Dualla is ready to cut the contract\").\n" +
            "  The UI shows the speaker's name separately — adding it to the line duplicates it.\n\n" +
            "COHERENCE RULES:\n" +
            "- MISSION ARCHETYPE — context.mission_guidance has a pre-computed ranked weights\n" +
            "  dict derived from the player's specialization, titles, cargo, active missions,\n" +
            "  station facilities, ship state, and faction state. The top-ranked archetype is\n" +
            "  the default pick. Deviate to a lower-ranked one ONLY if the context makes the\n" +
            "  top pick a bad fit (rare). Do NOT pick an archetype listed in\n" +
            "  mission_guidance.forbidden_archetypes — those are impossible given the context\n" +
            "  (e.g. combat forbidden when no hostile faction exists).\n" +
            "  When `combat` is listed in forbidden_archetypes, ALL of the following are\n" +
            "  forbidden — treat the ENTIRE mission as non-combat:\n" +
            "    * NO ClearPoi objectives.\n" +
            "    * NO KillEnemies objectives.\n" +
            "    * NO guards_faction on CollectItemTypes (guards spawn hostile units at\n" +
            "      the POI, which is combat).\n" +
            "    * NO mentioning defenders, raiders, ambushes, or hostile presence in the\n" +
            "      pitch / check-in / payout lines. The site is safe. The job is peaceful.\n" +
            "  Even when hostile factions exist in context.factions (Marauders etc.),\n" +
            "  they are simply unavailable for THIS mission. Pick a gather/deliver/escort\n" +
            "  shape with no combat element whatsoever.\n" +
            "  The five archetypes map to these objective types:\n" +
            "    * combat  → ClearPoi (preferred) or KillEnemies. Requires a hostile faction.\n" +
            "    * gather  → CollectItemTypes with item_category Ore or RefinedProduct.\n" +
            "    * salvage → CollectItemTypes with item_category Salvage.\n" +
            "    * deliver → TriggerObjective (Docked/Arrived/MoveToArea), optionally paired\n" +
            "                with CollectItemTypes TradeGoods for a 'haul + unload' shape.\n" +
            "    * escort  → ProtectUnit + TriggerObjective travel to destination.\n" +
            "  mission_guidance.rationale lists the signals that drove the weights — use it as\n" +
            "  flavor material (if it mentions @GatlingAmmo, the pitch can reference gunnery).\n" +
            "- Dialogue and mission MUST match BOTH WAYS:\n" +
            "    * If the pitch promises a rescue, include ProtectUnit or KillEnemies — not a lone CollectItemTypes.\n" +
            "    * If the pitch / payout promise \"bring back the haul\" / \"recovered materials\" /\n" +
            "      \"a cut of the loot\" / \"your share of the scrap\" — the mission MUST include a\n" +
            "      CollectItemTypes objective producing those items. Don't promise a haul and then\n" +
            "      only emit ClearPoi — the player sees nothing to deliver, the payout line lies.\n" +
            "    * If the mission is pure combat (only ClearPoi / KillEnemies), the payout language\n" +
            "      must be combat-framed (\"the zone is clear\", \"threat neutralized\", \"bounty paid\"),\n" +
            "      NOT loot-framed.\n" +
            "- AT MOST ONE COMBAT OBJECTIVE PER STEP. Do NOT put ClearPoi and KillEnemies into\n" +
            "  the same step. ClearPoi auto-completes when its spawned zone is cleared; KillEnemies\n" +
            "  counts ANY kill of that faction anywhere — mixing them creates a 'main mission done,\n" +
            "  stragglers still pending' shape that's awkward. Pick one combat verb per step.\n" +
            "- AT MOST ONE POI-SPAWNING OBJECTIVE PER STEP. ClearPoi and CollectItemTypes with\n" +
            "  item_category Ore or Salvage both spawn a dedicated POI on the map; a step's\n" +
            "  Locate button can only track ONE. Two in the same step orphans the second POI.\n" +
            "    * Defended gather site (fight and loot the same place) → emit ONE\n" +
            "      CollectItemTypes objective with guards_faction set; do NOT also emit ClearPoi.\n" +
            "    * Clear one zone, then gather elsewhere → emit TWO SEPARATE STEPS (step 1 =\n" +
            "      ClearPoi, step 2 = CollectItemTypes). Vanilla missions use this shape when\n" +
            "      the combat and gather sites are genuinely different places.\n" +
            "- MULTI-STEP IS A FIRST-CLASS SHAPE, NOT A FALLBACK. You have 1..3 steps. Use 2-3\n" +
            "  steps when the narrative mentions multiple locations, phased objectives, or\n" +
            "  compound tasks. The Locate button advances step-by-step, so each step is its own\n" +
            "  waypoint for the player. Good multi-step shapes:\n" +
            "    * \"Clear three pirate camps\" → 3 steps, each with one ClearPoi pointing at a\n" +
            "      different spawned POI. Three Locate waypoints in sequence.\n" +
            "    * \"Fight through the Corsairs and recover repair materials\" → 2 steps:\n" +
            "      [ClearPoi(Marauders)] then [CollectItemTypes(Salvage)]. Two separate POIs.\n" +
            "    * \"Mine the field, then haul the output to Station X\" → 2 steps:\n" +
            "      [CollectItemTypes(Ore)] then [TriggerObjective(DockedWithSpaceStation)].\n" +
            "    * \"Take out three Marauder patrols, then deliver the intel\" → 3 steps:\n" +
            "      [ClearPoi] [ClearPoi] [TriggerObjective]. Three waypoints.\n" +
            "  The one-step defended-collect pattern is the right shape for a SINGLE site with\n" +
            "  defenders AND loot at the same place. Do NOT collapse a multi-site or\n" +
            "  multi-phase narrative into one step just because it validates — the pitch will\n" +
            "  promise more than the mission delivers.\n" +
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

    [System.Obsolete("Superseded by PersistedBrokerRegistry-driven rehydration in T14. Remove after T17 E2E verification.")]
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
