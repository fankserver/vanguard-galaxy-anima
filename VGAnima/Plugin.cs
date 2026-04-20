using System;
using System.Linq;
using BepInEx;
using BepInEx.Logging;
using HarmonyLib;
using Source.Galaxy.POI.Station;
using VGAnima.Cache;
using VGAnima.Config;
using VGAnima.Llm;
using VGAnima.Missions;
using VGAnima.Patches;
using VGAnima.Pitch;
using VGAnima.Tts;
using VGAnima.Unity;

namespace VGAnima;

[BepInPlugin(PluginGuid, PluginName, PluginVersion)]
[BepInProcess("VanguardGalaxy.exe")]
[BepInDependency("vgtts", BepInDependency.DependencyFlags.SoftDependency)]
public class Plugin : BaseUnityPlugin
{
    public const string PluginGuid = "vganima";
    public const string PluginName = "Vanguard Galaxy Anima";
    public const string PluginVersion = "0.1.0";

    internal static Plugin Instance { get; private set; } = null!;
    internal static ManualLogSource Log { get; private set; } = null!;

    internal AnimaConfig Cfg { get; private set; } = null!;
    internal IMissionAssigner Assigner { get; private set; } = null!;
    internal IPitchProvider PitchProvider { get; private set; } = null!;
    internal IGamePlayerView PlayerView { get; private set; } = null!;
    internal VgttsBridge Vgtts { get; private set; } = null!;
    internal ConversionRegistry<BarPatron, ConversionRecord> Registry { get; private set; } = null!;

    internal ILlmClient? LlmClient { get; private set; }
    internal IGameStateView GameStateView { get; private set; } = null!;
    internal ContextGatherer Gatherer { get; private set; } = null!;
    internal ResponseValidator Validator { get; private set; } = null!;
    internal UnityMainThreadScheduler Scheduler { get; private set; } = null!;

    private Harmony _harmony = null!;

    private void Awake()
    {
        Instance = this;
        Log = Logger;

        Cfg = new AnimaConfig(Config);

        // Register plugin-authored StoryMissions before any save loads so
        // Mission.FromJson(string) can rehydrate them (vanilla registry
        // lookup throws KeyNotFoundException if the storyId is missing).
        TestStoryMissions.Register();

        Assigner      = new TestMissionAssigner();
        PlayerView    = new GamePlayerView();
        Vgtts         = new VgttsBridge();
        Registry      = new ConversionRegistry<BarPatron, ConversionRecord>();

        GameStateView = new GameStateView();
        Gatherer      = new ContextGatherer();
        Validator     = new ResponseValidator();
        Scheduler     = gameObject.AddComponent<UnityMainThreadScheduler>();

        // LlmClient stays null when the master switch is off or BaseUrl is blank —
        // BarRefreshPatches treats null as "LLM disabled, log and skip" per
        // spec §10 (see docs/superpowers/notes/... if we ever re-document).
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

        // LlmPitchProvider looks up LlmStory by NPC name via the registry.
        // FindByValue is O(N); number of live brokers is <10 at all times,
        // so this is comfortably sub-millisecond.
        PitchProvider = new LlmPitchProvider(name =>
        {
            var rec = Registry.FindByValue(r => r.Station != null && name != null &&
                r.Station.bar != null &&
                r.Station.bar.availablePatrons.Exists(p => p.name == name));
            return rec?.LlmStory;
        });

        Log.LogInfo($"[vganima] VGTTS detected: {(Vgtts.IsAvailable ? "yes" : "no")}");
        Log.LogInfo($"[vganima] LLM enabled: {(LlmClient != null ? "yes" : "no")}  " +
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

        Log.LogInfo($"{PluginName} v{PluginVersion} loaded ({_harmony.GetPatchedMethods().Count()} patches)");
    }

    private void OnDestroy()
    {
        _harmony?.UnpatchSelf();
        if (LlmClient is IDisposable disposable) disposable.Dispose();
    }

    /// <summary>Loggable marker for an optional API key — never emits the key
    /// itself. Used in the boot log so operators can confirm whether auth is
    /// wired without leaking the token into BepInEx's log file.</summary>
    internal static string RedactApiKey(string? apiKey) =>
        string.IsNullOrEmpty(apiKey) ? "<empty>" : "<set>";
}
