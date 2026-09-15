using System;
using System.Linq;
using BepInEx;
using BepInEx.Logging;
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
[BepInDependency(ModApi.PluginId, "0.2.8")]
[BepInDependency("vgtts",             BepInDependency.DependencyFlags.SoftDependency)]
[BepInDependency("vgmissionjournal",  BepInDependency.DependencyFlags.SoftDependency)]
public class Plugin : BaseUnityPlugin
{
    public const string PluginGuid = "vganima";
    public const string PluginName = "Anima";
    public const string PluginVersion = "0.4.0";

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
    internal bool ManagedBarsSelected { get; private set; }
    internal ManagedBrokerRosters? ManagedBars { get; private set; }
    private Harmony? _loadSafetyHarmony;
    private float _nextCapabilityCheck;
    private MissionEventObserver? _missionObserver;
    private SystemVisitObserver? _visitObserver;
    private ILifecycleService? _lifecycle;
    private string? _lastSaveDestination;
    private bool _active;
    private bool _stopped;

    /// <summary>Service root captured once after API bootstrap. The 0.2.x root
    /// and its services are stable for the API lifetime — never null-check the
    /// references themselves; typed <see cref="IServiceStatus.Availability"/>
    /// carries health (there is no capability list). Access via this cached
    /// reference so post-shutdown reads report stopped state instead of the
    /// <c>ModApi.Services</c> bootstrap/shutdown throw.</summary>
    internal ModServices? ServicesOrNull { get; private set; }
    private static ModServices? SafeServices() { try { return ModApi.Services; } catch (Exception) { return null; } }
    private static bool Available(IServiceStatus? status) { try { return status?.Availability.IsAvailable == true; } catch { return false; } }
    private static ServiceAvailability AvailabilityOf(IServiceStatus? status)
    {
        try
        {
            var availability = status?.Availability;
            return availability ?? new ServiceAvailability(ServiceUnavailableReason.ApiStopped, "VGModAPI services unavailable");
        }
        catch { return new ServiceAvailability(ServiceUnavailableReason.ApiStopped, "VGModAPI services unavailable"); }
    }

    /// <summary>Witnessed mission transitions additionally require available
    /// session tracking (the mission adapter binds on it). Root member access
    /// itself is guarded: post-shutdown references must report unavailable,
    /// never throw into Update polling or the quit flush.</summary>
    private bool MissionApiAvailable
    {
        get
        {
            try
            {
                var s = ServicesOrNull;
                return s != null && Available(s.Missions) && Available(s.Lifecycle.SessionTracking);
            }
            catch { return false; }
        }
    }
    /// <summary>Optional and off by default in the API ([Travel] Enabled).
    /// Visit recording exists only while it is true; there is no travel-hook fallback.</summary>
    private bool TravelApiAvailable
    {
        get { try { return Available(ServicesOrNull?.Travel); } catch { return false; } }
    }
    /// <summary>True only while witnessed arrivals are actually being recorded.
    /// When false the visit map is preserved but never pitched: <c>regionally_known</c>
    /// is omitted rather than describing the player with stale counts.</summary>
    internal bool VisitHistoryRecording => _active && TravelApiAvailable && _visitObserver?.IsRecording == true;
    /// <summary>Current witnessed session, independent of provider activity.</summary>
    internal SessionSnapshot? ObservedSession
    {
        get { try { return ServicesOrNull?.Lifecycle.CurrentSession; } catch { return null; } }
    }
    /// <summary>The captured running game for live patron actions, or null.</summary>
    internal IGame? CurrentGame
    {
        get { try { return ServicesOrNull?.Game.Current; } catch { return null; } }
    }
    internal Guid? ProviderSession
    {
        get
        {
            var session = ObservedSession;
            return _active && MissionApiAvailable && session?.Phase is SessionPhase.PlayerReady or SessionPhase.GameplayInitialized ? session.Id : null;
        }
    }
    internal bool CanPublishFor(Guid? session) => session.HasValue && ProviderSession == session;

    private void Awake()
    {
        Instance = this;
        Log = Logger;

        ServicesOrNull = SafeServices();
        if (!MissionApiAvailable)
        {
            enabled = false;
            Log.LogError("Requires VGModAPI >= 0.2.8 with enabled mission events ([Missions] Enabled = true) and available session tracking; no direct mission-hook fallback.");
            return;
        }
        var identity = AvailabilityOf(ServicesOrNull!.Missions.IdentityContinuity);
        if (!identity.IsAvailable)
            Log.LogWarning($"Mission identity continuity unavailable ({identity.Reason}): {identity.Detail}. Cross-save witnessed identity may fall back to session-local evidence.");
        Cfg = new AnimaConfig(Config);

        PlayerView      = new GamePlayerView();
        Vgtts           = new VgttsBridge();
        Registry        = new ConversionRegistry<BarPatron, ConversionRecord>();

        // Cross-session persistence singletons. SidecarIO takes a clock for
        // quarantine timestamps. Registry is the in-memory source of truth
        // during a session; flushed to disk on witnessed vanilla save success
        // (Lifecycle SaveSucceeded) and rehydrated by SaveLoadPatch on load.
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
        // Keep reconstruction and missing-definition protection alive after an observer stop.
        SaveLoadPatch.Registry = PersistedRegistry;
        SaveLoadPatch.Io = SidecarIO;
        SaveLoadPatch.Log = Log;
        MissionLookupPatch.Registry = PersistedRegistry;
        _loadSafetyHarmony = new Harmony(PluginGuid + ".load-safety");
        _loadSafetyHarmony.PatchAll(typeof(MissionLookupPatch));
        _loadSafetyHarmony.PatchAll(typeof(SaveLoadPatch));

        var barsAvailability = AvailabilityOf(ServicesOrNull!.Bars);
        _lifecycle = ServicesOrNull.Lifecycle;
        ManagedBarsSelected = barsAvailability.IsAvailable;
        Log.LogInfo(ManagedBarsSelected
            ? "Bars service available: managed broker roster mode selected (API-owned presentation)."
            : $"Bars service unavailable ({barsAvailability.Reason}): {barsAvailability.Detail} — native roster fallback mode; bars stay vanilla-owned.");
        _harmony = new Harmony(PluginGuid);
        // Witnessed save outcomes drive the sidecar flush; subscribe before
        // reading current session state. Events never replay.
        _lifecycle.Changed += OnLifecycleChanged;

        _harmony.PatchAll(typeof(SalesmanPatches));
        _harmony.PatchAll(typeof(BarRefreshPatches));
        _harmony.PatchAll(typeof(RegistryRehydratePatches));
        _harmony.PatchAll(typeof(BarUIDebugPatches));
        _harmony.PatchAll(typeof(BarPatronImageDebugPatches));
        // Harmony does not traverse nested patch classes.
        _harmony.PatchAll(typeof(BarPurchasePatches.OnButtonPurchase));
        _harmony.PatchAll(typeof(ShopPurchasePatches.OnBuyAmount));
        _missionObserver = new MissionEventObserver(ServicesOrNull!.Missions, PersistedRegistry, error =>
        {
            Log.LogError("Mission provider stopped after observer failure: " + error.Message);
            StopProvider();
        }, entry => !ManagedBarsSelected || ManagedBars?.Remove(entry.Broker.Seed) == true);
        BindVisitObserver();

        // Wire the persistence singleton into the roster observation patches.
        BarRefreshPatches.PersistedRegistry        = PersistedRegistry;
        RegistryRehydratePatches.PersistedRegistry = PersistedRegistry;

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
        Log.LogInfo($"{PluginName} v{PluginVersion} loaded ({_harmony.GetPatchedMethods().Count()} authoring/observation patches, {_loadSafetyHarmony.GetPatchedMethods().Count()} load-safety patches)");
    }

    /// <summary>Binds system-visit recording to witnessed API arrivals, or
    /// leaves the feature off. The API's travel group is opt-in and disabled by
    /// default, so unavailability is an expected degraded state, not a fault:
    /// nothing is recorded, recorded history stays intact, and no direct travel
    /// hook is installed instead. A refused subscription degrades the same way
    /// — it must not escape into the mission provider's installation.</summary>
    private void BindVisitObserver()
    {
        if (!TravelApiAvailable)
        {
            Log.LogWarning("VGModAPI native travel events unavailable ([Travel] Enabled = false or unbound): system visits are not recorded and regional recognition is omitted from prompts. Existing visit history is preserved; there is no travel-hook fallback.");
            return;
        }
        _visitObserver = SystemVisitObserver.TryBind(ServicesOrNull!.Travel, PersistedRegistry,
            failed: error =>
            {
                Log.LogError("System-visit recording stopped after travel observer failure; recorded history is preserved and regional recognition is omitted: " + error.Message);
                StopVisitRecording();
            },
            bindingFailed: error =>
                Log.LogError("Travel subscription refused; system visits are not recorded and regional recognition is omitted until restart. Mission authoring, save writes and the load safeguards are unaffected, and no travel-hook fallback is installed: " + error.Message));
        if (_visitObserver == null) return;
        SaveLoadPatch.VisitObserver = _visitObserver;
        Log.LogInfo("Native travel events bound: system visits recorded from witnessed arrivals.");
    }

    /// <summary>Ends visit recording for this process without touching the
    /// mission provider: only the recognition feature needs a restart.</summary>
    private void StopVisitRecording()
    {
        SaveLoadPatch.VisitObserver = null;
        var observer = _visitObserver;
        _visitObserver = null;
        try { observer?.Dispose(); }
        catch (Exception error) { Log?.LogError("Travel subscription disposal failed; system-visit recording stays off until restart. Mission provider and load safeguards are unaffected: " + error.Message); }
    }

    private void Start()
    {
        // Chainloader authenticates the instance only after Awake has returned.
        if (!_active || !ManagedBarsSelected) return;
        try { ManagedBars = new ManagedBrokerRosters(ServicesOrNull?.Bars ?? throw new InvalidOperationException("Bar service disappeared after startup selection."), this); }
        catch (Exception error) { Log.LogError("Managed bar provider unavailable: " + error); StopProvider(); }
    }

    private void Update()
    {
        if (!_active || Time.unscaledTime < _nextCapabilityCheck) return;
        _nextCapabilityCheck = Time.unscaledTime + 1f;
        try { ManagedBars?.ReconcileRetirements(this); }
        catch (Exception error) { Log.LogError("Managed broker retirement retry failed: " + error); }
        try { ManagedBars?.DrainPending(this); }
        catch (Exception error) { Log.LogError("Managed roster drain failed: " + error); }
        // Losing the optional travel capability degrades only visit recording;
        // mission authoring and the load safeguards are independent of it.
        if (_visitObserver != null && !TravelApiAvailable)
        {
            StopVisitRecording();
            Log.LogWarning("Travel API unavailable; system-visit recording stopped until restart, while mission authoring and save writes continue. Recorded history is preserved and regional recognition is omitted.");
        }
        if (MissionApiAvailable) return;
        StopProvider();
        Log.LogError("Mission API unavailable; provider stopped until restart. Load safeguards remain active.");
    }

    /// <summary>Witnessed lifecycle facts drive provider work:
    /// PlayerReady pre-declares restored managed contacts before the first
    /// bar refresh, and the sidecar publishes only on a witnessed
    /// <see cref="LifecycleEventKind.SaveSucceeded"/> against its actual
    /// destination (skipped/failed saves never publish — the retired
    /// SaveWritePatch postfix could not tell the difference).</summary>
    private void OnLifecycleChanged(LifecycleEvent e)
    {
        if (e == null) return;
        try
        {
            switch (e.Kind)
            {
                case LifecycleEventKind.SessionStarting:
                    // The remembered save destination belongs to the replaced attempt.
                    _lastSaveDestination = null;
                    break;
                case LifecycleEventKind.PlayerReady:
                    if (_active && ManagedBarsSelected)
                        ManagedBars?.RegisterRestoredDefinitions(this);
                    break;
                case LifecycleEventKind.SaveSucceeded:
                    if (!_active || !MissionApiAvailable) return;
                    if (e.Destination is null)
                    {
                        Log.LogWarning("SaveSucceeded without a destination; sidecar not flushed.");
                        return;
                    }
                    TryFlushSidecar(e.Destination, "SaveSucceeded");
                    break;
                // SaveStarted/SaveSkipped/SaveFailed publish nothing; failures
                // keep prior sidecar bytes intact (best-effort semantics).
            }
        }
        catch (Exception error)
        {
            Log.LogError($"Lifecycle handler failed for {e.Kind}: {error}");
        }
    }

    /// <summary>Atomic sidecar write (tmp + rename inside <see cref="SidecarIO"/>).
    /// Persistence failures are logged and never poison vanilla.</summary>
    private bool TryFlushSidecar(string savePath, string reason)
    {
        try
        {
            var sidecarPath = SidecarPathResolver.From(savePath);
            var entries = System.Linq.Enumerable.ToArray(PersistedRegistry.All());
            var visited = System.Linq.Enumerable.ToArray(PersistedRegistry.VisitedSystems.Values);
            SidecarIO.Write(sidecarPath, new SidecarSchema(
                Version:        SidecarSchema.CurrentVersion,
                Entries:        entries,
                VisitedSystems: visited.Length == 0 ? null : visited,
                BarReservations: PersistedRegistry.BarReservations.Count == 0 ? null : System.Linq.Enumerable.ToArray(PersistedRegistry.BarReservations)));
            _lastSaveDestination = savePath;
            Log.LogInfo($"{reason}: flushed {entries.Length} entr{(entries.Length == 1 ? "y" : "ies")} + {visited.Length} visited to {sidecarPath}");
            return true;
        }
        catch (Exception e)
        {
            Log.LogError($"Sidecar flush for `{savePath}` failed ({reason}): {e}");
            return false;
        }
    }

    private void OnAppQuitting()
    {
        if (!_active || !MissionApiAvailable) return;
        var path = SaveLoadPatch.LastKnownSavePath ?? _lastSaveDestination;
        if (path is null) return;
        // ApplicationQuit safety net: flush the most recently active slot even
        // if the player never saved this session (mirrors the loaded-slot
        // latch semantics of the retired postfix flush).
        TryFlushSidecar(path, "ApplicationQuit");
    }

    private void OnDestroy()
    {
        StopProvider();
        Cleanup(() => _loadSafetyHarmony?.UnpatchSelf());
        SaveLoadPatch.Registry = null;
        MissionLookupPatch.Registry = null;
    }

    private void StopProvider()
    {
        if (_stopped) return;
        _stopped = true; _active = false; enabled = false;
        try { if (_lifecycle != null) _lifecycle.Changed -= OnLifecycleChanged; } catch { }
        _lifecycle = null;
        Application.quitting -= OnAppQuitting;
        Cleanup(() => ManagedBars?.Dispose());
        Cleanup(() => _missionObserver?.Dispose());
        StopVisitRecording();
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
