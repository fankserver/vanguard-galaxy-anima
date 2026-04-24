using System;
using System.Linq;
using BepInEx;
using BepInEx.Logging;
using HarmonyLib;
using Source.Galaxy.POI.Station;
using UnityEngine;
using VGAnima.Cache;
using VGAnima.Config;
using VGAnima.Llm;
using VGAnima.Missions;
using VGAnima.Patches;
using VGAnima.Persistence;
using VGAnima.Pitch;
using VGAnima.Tts;
using VGAnima.Unity;

namespace VGAnima;

[BepInPlugin(PluginGuid, PluginName, PluginVersion)]
[BepInProcess("VanguardGalaxy.exe")]
[BepInDependency("vgtts",             BepInDependency.DependencyFlags.SoftDependency)]
[BepInDependency("vgmissionjournal",  BepInDependency.DependencyFlags.SoftDependency)]
public class Plugin : BaseUnityPlugin
{
    public const string PluginGuid = "vganima";
    public const string PluginName = "Vanguard Galaxy Anima";
    public const string PluginVersion = "0.2.0";

    internal static Plugin Instance { get; private set; } = null!;
    internal static ManualLogSource Log { get; private set; } = null!;

    internal AnimaConfig Cfg { get; private set; } = null!;

    /// <summary>v2-mission: post-LLM mission builder + registrar. Replaces
    /// the v1 <see cref="IMissionAssigner"/> slot.</summary>
    internal LlmMissionAssigner MissionAssigner { get; private set; } = null!;

    internal IPitchProvider PitchProvider { get; private set; } = null!;
    internal IGamePlayerView PlayerView { get; private set; } = null!;
    internal VgttsBridge Vgtts { get; private set; } = null!;
    internal ConversionRegistry<BarPatron, ConversionRecord> Registry { get; private set; } = null!;

    internal ILlmClient? LlmClient { get; private set; }
    internal IGameStateView GameStateView { get; private set; } = null!;
    internal ContextGatherer Gatherer { get; private set; } = null!;
    internal ResponseValidator Validator { get; private set; } = null!;
    internal UnityMainThreadScheduler Scheduler { get; private set; } = null!;

    internal PersistedBrokerRegistry PersistedRegistry { get; private set; } = null!;
    internal SidecarIO SidecarIO { get; private set; } = null!;
    internal IClock Clock { get; private set; } = null!;
    /// <summary>Typed soft-dep on VGMissionJournal. Source of truth for
    /// resolved-mission history in the journal's local/network/rumors
    /// windows. When the plugin isn't installed, the bridge's
    /// <see cref="MissionJournal.VgMissionJournalBridge.IsAvailable"/>
    /// is false and every query returns empty — journal still builds,
    /// just with empty resolved windows.</summary>
    internal MissionJournal.VgMissionJournalBridge MissionJournalBridge { get; private set; } = null!;

    private Harmony _harmony = null!;

    private void Awake()
    {
        Instance = this;
        Log = Logger;

        Cfg = new AnimaConfig(Config);

        PlayerView      = new GamePlayerView();
        Vgtts           = new VgttsBridge();
        Registry        = new ConversionRegistry<BarPatron, ConversionRecord>();

        // Cross-session persistence singletons. SidecarIO takes a clock for
        // quarantine timestamps. Registry is the in-memory source of truth
        // during a session; flushed to disk by SaveWritePatch on vanilla save,
        // loaded by SaveLoadPatch on vanilla load.
        PersistedRegistry = new PersistedBrokerRegistry();
        Clock             = new GameClock();
        SidecarIO         = new SidecarIO(() => DateTime.UtcNow);
        MissionJournalBridge = new MissionJournal.VgMissionJournalBridge();
        Log.LogInfo($"VGMissionJournal detected: {(MissionJournalBridge.IsAvailable ? "yes" : "no")}");

        MissionAssigner = new LlmMissionAssigner(
            register: Source.MissionSystem.StoryMission.Add,
            registry: PersistedRegistry,
            clock:    Clock);

        GameStateView = new GameStateView();
        Gatherer      = new ContextGatherer();
        Validator     = new ResponseValidator();
        Scheduler     = gameObject.AddComponent<UnityMainThreadScheduler>();

        if (Cfg.LlmEnabled.Value && !string.IsNullOrEmpty(Cfg.LlmBaseUrl.Value))
        {
            LlmClient = new HttpLlmClient(
                baseUrl:        Cfg.LlmBaseUrl.Value,
                model:          Cfg.LlmModel.Value,
                apiKey:         Cfg.LlmApiKey.Value,
                enableThinking: Cfg.LlmEnableThinking.Value,
                maxTokens:      Cfg.LlmMaxTokens.Value,
                temperature:    Cfg.LlmTemperature.Value,
                timeout:        TimeSpan.FromSeconds(Cfg.LlmTimeoutSeconds.Value));
        }

        PitchProvider = new LlmPitchProvider(name =>
        {
            var rec = Registry.FindByValue(r => r.Station != null && name != null &&
                r.Station.bar != null &&
                r.Station.bar.availablePatrons.Exists(p => p.name == name));
            return rec?.LlmStory;
        });

        Log.LogInfo($"VGTTS detected: {(Vgtts.IsAvailable ? "yes" : "no")}");
        Log.LogInfo($"LLM enabled: {(LlmClient != null ? "yes" : "no")}  " +
                    $"Chance: {Cfg.MissionChance.Value}  " +
                    $"BaseUrl: {(string.IsNullOrEmpty(Cfg.LlmBaseUrl.Value) ? "(unset)" : Cfg.LlmBaseUrl.Value)}  " +
                    $"Model: {Cfg.LlmModel.Value}  " +
                    $"ApiKey: {RedactApiKey(Cfg.LlmApiKey.Value)}  " +
                    $"MaxTokens: {Cfg.LlmMaxTokens.Value}  " +
                    $"Temperature: {Cfg.LlmTemperature.Value}");

        _harmony = new Harmony(PluginGuid);
        _harmony.PatchAll(typeof(SalesmanPatches));
        _harmony.PatchAll(typeof(BarRefreshPatches));
        _harmony.PatchAll(typeof(RegistryRehydratePatches));
        _harmony.PatchAll(typeof(BarUIDebugPatches));
        _harmony.PatchAll(typeof(BarPatronImageDebugPatches));
        _harmony.PatchAll(typeof(SaveWritePatch));
        _harmony.PatchAll(typeof(SaveLoadPatch));
        _harmony.PatchAll(typeof(MissionLookupPatch));
        // BarPurchasePatches uses nested-type Harmony annotations, same
        // gotcha as MissionLifecyclePatches — patch the nested type
        // directly so PatchAll actually attaches.
        _harmony.PatchAll(typeof(BarPurchasePatches.OnButtonPurchase));
        _harmony.PatchAll(typeof(ShopPurchasePatches.OnBuyAmount));
        // MissionLifecyclePatches has no [HarmonyPatch] on the outer type —
        // the four nested classes carry the annotations. Harmony.PatchAll(Type)
        // does NOT traverse nested types, so passing the outer type silently
        // attaches zero patches. Patch each nested type directly.
        _harmony.PatchAll(typeof(MissionLifecyclePatches.OnAcceptPatch));
        _harmony.PatchAll(typeof(MissionLifecyclePatches.OnCompletePatch));
        _harmony.PatchAll(typeof(MissionLifecyclePatches.OnFailPatch));
        _harmony.PatchAll(typeof(MissionLifecyclePatches.OnArchivePatch));
        _harmony.PatchAll(typeof(MissionLifecyclePatches.OnAbandonPatch));
        _harmony.PatchAll(typeof(SystemEntryPatch));

        // Wire persistence singletons into Harmony patches (all four use the
        // same PersistedBrokerRegistry + SidecarIO instances).
        SaveWritePatch.Registry          = PersistedRegistry;
        SaveWritePatch.Io                = SidecarIO;
        SaveWritePatch.Log               = Log;

        SaveLoadPatch.Registry           = PersistedRegistry;
        SaveLoadPatch.Io                 = SidecarIO;
        SaveLoadPatch.Log                = Log;

        MissionLookupPatch.Registry      = PersistedRegistry;
        MissionLifecyclePatches.Registry = PersistedRegistry;
        MissionLifecyclePatches.Clock    = Clock;

        BarRefreshPatches.PersistedRegistry        = PersistedRegistry;
        RegistryRehydratePatches.PersistedRegistry = PersistedRegistry;

        SystemEntryPatch.Registry                  = PersistedRegistry;
        SystemEntryPatch.Clock                     = Clock;
        SystemEntryPatch.Log                       = Log;

        // Dead-sidecar startup sweep: delete sidecars whose vanilla save file
        // was removed outside the game. Bounded by save-directory size; runs
        // once at plugin load.
        try
        {
            var savesPath = Source.Util.SaveGame.SavesPath;
            var swept = DeadSidecarSweeper.Sweep(savesPath);
            if (swept.Count > 0) Log.LogInfo($"Swept {swept.Count} dead sidecar(s) from {savesPath}");
        }
        catch (Exception e)
        {
            Log.LogError($"Dead-sidecar sweep failed: {e}");
            // Never rethrow; sweep failure is non-fatal.
        }

        // ApplicationQuit safety net (spec §5): flush to the most recently
        // active save slot when the player closes the game. If no slot was
        // ever active this session (player never saved/loaded), nothing to
        // flush — matches vanilla's "quit without save = lose changes" semantics.
        Application.quitting += OnAppQuitting;

        Log.LogInfo($"{PluginName} v{PluginVersion} loaded ({_harmony.GetPatchedMethods().Count()} patches)");
    }

    private void OnAppQuitting()
    {
        var path = SaveLoadPatch.LastKnownSavePath ?? SaveWritePatch.LastKnownSavePath;
        if (path is null) return;
        try
        {
            var sidecarPath = SidecarPathResolver.From(path);
            var entries     = System.Linq.Enumerable.ToArray(PersistedRegistry.All());
            var completed   = System.Linq.Enumerable.ToArray(PersistedRegistry.CompletedMissions);
            var visited     = System.Linq.Enumerable.ToArray(PersistedRegistry.VisitedSystems.Values);
            SidecarIO.Write(sidecarPath, new SidecarSchema(
                Version:           SidecarSchema.CurrentVersion,
                Entries:           entries,
                CompletedMissions: completed.Length == 0 ? null : completed,
                VisitedSystems:    visited.Length   == 0 ? null : visited));
            Log.LogInfo($"ApplicationQuit: flushed {entries.Length} entr{(entries.Length == 1 ? "y" : "ies")} + {completed.Length} completed + {visited.Length} visited to {sidecarPath}");
        }
        catch (Exception e) { Log.LogError($"Quit-time flush failed: {e}"); }
    }

    private void OnDestroy()
    {
        Application.quitting -= OnAppQuitting;
        _harmony?.UnpatchSelf();
        if (LlmClient is IDisposable disposable) disposable.Dispose();
    }

    internal static string RedactApiKey(string? apiKey) =>
        string.IsNullOrEmpty(apiKey) ? "<empty>" : "<set>";
}
