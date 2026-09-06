using System;
using System.Linq;
using BepInEx;
using BepInEx.Logging;
using BepInEx.Bootstrap;
using VGModAPI;
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
[BepInDependency(ModApi.PluginId, "0.1.8")]
[BepInDependency("vgtts",             BepInDependency.DependencyFlags.SoftDependency)]
[BepInDependency("vgmissionjournal",  BepInDependency.DependencyFlags.SoftDependency)]
public class Plugin : BaseUnityPlugin
{
    public const string PluginGuid = "vganima";
    public const string PluginName = "Vanguard Galaxy Anima";
    public const string PluginVersion = "0.3.0";

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
    private MissionEventObserver? _missionObserver;
    private bool _active;
    private bool _stopped;
    private static bool MissionApiAvailable => ModApi.Missions != null && ModApi.Current?.Capabilities.Any(c => c.Name == "mission-transitions" && c.Available) == true;
    internal Guid? ProviderSession
    {
        get
        {
            var session = ModApi.Current?.CurrentSession;
            return _active && MissionApiAvailable && session?.Phase is SessionPhase.PlayerReady or SessionPhase.GameplayInitialized ? session.Id : null;
        }
    }
    internal bool CanPublishFor(Guid? session) => session.HasValue && ProviderSession == session;

    private void Awake()
    {
        Instance = this;
        Log = Logger;

        if (!Chainloader.PluginInfos.TryGetValue(ModApi.PluginId, out var apiPlugin) || apiPlugin.Metadata.Version.Major != 0 || apiPlugin.Metadata.Version.Minor != 1 || !MissionApiAvailable)
        {
            enabled = false;
            Log.LogError("Requires VGModAPI 0.1.8–0.1.x with enabled mission events ([Missions] Enabled = true); no direct mission-hook fallback.");
            return;
        }
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
            clock:    Clock,
            canAssign: () => ProviderSession.HasValue);

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

        try { InitializeHooks(); }
        catch (Exception error)
        {
            StopProvider();
            Log.LogError("Provider initialization failed; hooks and save writes disabled: " + error);
        }
    }

    private void InitializeHooks()
    {
        _harmony = new Harmony(PluginGuid);
        _harmony.PatchAll(typeof(SalesmanPatches));
        _harmony.PatchAll(typeof(BarRefreshPatches));
        _harmony.PatchAll(typeof(RegistryRehydratePatches));
        _harmony.PatchAll(typeof(BarUIDebugPatches));
        _harmony.PatchAll(typeof(BarPatronImageDebugPatches));
        _harmony.PatchAll(typeof(SaveWritePatch));
        _harmony.PatchAll(typeof(SaveLoadPatch));
        _harmony.PatchAll(typeof(MissionLookupPatch));
        // Harmony does not traverse nested patch classes.
        _harmony.PatchAll(typeof(BarPurchasePatches.OnButtonPurchase));
        _harmony.PatchAll(typeof(ShopPurchasePatches.OnBuyAmount));
        _missionObserver = new MissionEventObserver(ModApi.Missions!, PersistedRegistry, error =>
        {
            Log.LogError("Mission provider stopped after observer failure: " + error.Message);
            StopProvider();
        });
        _harmony.PatchAll(typeof(SystemEntryPatch));

        // Wire persistence singletons into Harmony patches (all four use the
        // same PersistedBrokerRegistry + SidecarIO instances).
        SaveWritePatch.Registry          = PersistedRegistry;
        SaveWritePatch.Io                = SidecarIO;
        SaveWritePatch.Log               = Log;
        SaveWritePatch.CanWrite          = () => _active && MissionApiAvailable;

        SaveLoadPatch.Registry           = PersistedRegistry;
        SaveLoadPatch.Io                 = SidecarIO;
        SaveLoadPatch.Log                = Log;

        MissionLookupPatch.Registry      = PersistedRegistry;

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

        _active = true;
        Log.LogInfo($"{PluginName} v{PluginVersion} loaded ({_harmony.GetPatchedMethods().Count()} patches)");
    }

    private void Update()
    {
        if (_active && !MissionApiAvailable)
        {
            StopProvider();
            Log.LogError("Mission API unavailable; provider stopped until restart.");
        }
    }

    private void OnAppQuitting()
    {
        if (!_active || !MissionApiAvailable) return;
        var path = SaveLoadPatch.LastKnownSavePath ?? SaveWritePatch.LastKnownSavePath;
        if (path is null) return;
        try
        {
            var sidecarPath = SidecarPathResolver.From(path);
            var entries = System.Linq.Enumerable.ToArray(PersistedRegistry.All());
            var visited = System.Linq.Enumerable.ToArray(PersistedRegistry.VisitedSystems.Values);
            SidecarIO.Write(sidecarPath, new SidecarSchema(
                Version:        SidecarSchema.CurrentVersion,
                Entries:        entries,
                VisitedSystems: visited.Length == 0 ? null : visited));
            Log.LogInfo($"ApplicationQuit: flushed {entries.Length} entr{(entries.Length == 1 ? "y" : "ies")} + {visited.Length} visited to {sidecarPath}");
        }
        catch (Exception e) { Log.LogError($"Quit-time flush failed: {e}"); }
    }

    private void OnDestroy() => StopProvider();

    private void StopProvider()
    {
        if (_stopped) return;
        _stopped = true; _active = false; enabled = false;
        SaveWritePatch.Registry = null;
        SaveLoadPatch.Registry = null;
        Application.quitting -= OnAppQuitting;
        Cleanup(() => _missionObserver?.Dispose());
        Cleanup(() => _harmony?.UnpatchSelf());
        Cleanup(() => { if (LlmClient is IDisposable disposable) disposable.Dispose(); });
    }

    private static void Cleanup(Action action)
    {
        try { action(); }
        catch (Exception error) { Log?.LogError("Provider cleanup failed; restart required: " + error.Message); }
    }

    internal static string RedactApiKey(string? apiKey) =>
        string.IsNullOrEmpty(apiKey) ? "<empty>" : "<set>";
}
